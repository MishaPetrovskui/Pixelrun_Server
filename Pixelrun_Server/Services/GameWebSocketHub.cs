using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Pixelrun_Server.Services;

namespace Pixelrun_Server
{
    public class MpPlayerState
    {
        public int Id { get; set; }
        public string Username { get; set; } = "";
        public float X { get; set; }
        public float Y { get; set; }
        public int Level { get; set; }
        public bool FacingRight { get; set; } = true;
        public string Anim { get; set; } = "idle";
        public string Src { get; set; } = "game";
        /// "playing" | "menu" | "paused"
        public string GamePhase { get; set; } = "playing";
    }

    record MpOtherPlayer(
        int Id,
        string Username,
        float X,
        float Y,
        bool FacingRight,
        string Anim,
        string GamePhase);

    public class MultiplayerHub
    {
        private static readonly ConcurrentDictionary<int, (WebSocket Ws, MpPlayerState State)>
            _gameClients = new();
        private static readonly ConcurrentDictionary<int, (WebSocket Ws, MpPlayerState State)>
            _webClients = new();
        private static readonly ConcurrentDictionary<int, ConcurrentDictionary<int, bool>>
            _killedEnemies = new();

        // ── Public helpers (used by LobbyController) ──────────────────────────

        /// Only counts players that are actually in-game (phase != "menu")
        public static Dictionary<int, int> GetLevelCounts()
        {
            var result = new Dictionary<int, int>();
            foreach (var kv in _gameClients)
            {
                if (kv.Value.Ws.State != WebSocketState.Open) continue;
                if (kv.Value.State.GamePhase == "menu") continue;
                int lv = kv.Value.State.Level;
                result[lv] = result.TryGetValue(lv, out int c) ? c + 1 : 1;
            }
            return result;
        }

        public static List<MpPlayerState> GetOnlinePlayers()
            => _gameClients
               .Where(kv => kv.Value.Ws.State == WebSocketState.Open
                         && kv.Value.State.GamePhase != "menu")
               .Select(kv => kv.Value.State)
               .ToList();

        // ── DI ────────────────────────────────────────────────────────────────
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<MultiplayerHub> _logger;

        public MultiplayerHub(IServiceScopeFactory scopeFactory,
                              ILogger<MultiplayerHub> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static async Task BroadcastEnemyKill(int senderId, int level, int enemyIndex)
        {
            var killed = _killedEnemies.GetOrAdd(level, _ => new ConcurrentDictionary<int, bool>());
            killed[enemyIndex] = true;

            var bytes = Encoding.UTF8.GetBytes(
                JsonSerializer.Serialize(new { type = "enemy_kill", enemyIndex },
                    new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

            foreach (var kv in _gameClients)
            {
                if (kv.Key == senderId) continue;
                if (kv.Value.State.Level != level) continue;
                if (kv.Value.Ws.State != WebSocketState.Open) continue;
                try { await kv.Value.Ws.SendAsync(bytes, WebSocketMessageType.Text, true, default); }
                catch { }
            }
        }

        /// Forwards a {"type":"logout"} packet to the game client of this player.
        /// Called when the web client sends a logout event.
        private static async Task ForwardLogoutToGame(int playerId)
        {
            if (!_gameClients.TryGetValue(playerId, out var gc)) return;
            if (gc.Ws.State != WebSocketState.Open) return;
            var bytes = Encoding.UTF8.GetBytes("{\"type\":\"logout\"}");
            try { await gc.Ws.SendAsync(bytes, WebSocketMessageType.Text, true, default); }
            catch { }
        }

        // ── Main WebSocket handler ────────────────────────────────────────────
        public async Task HandleAsync(HttpContext ctx, WebSocket ws)
        {
            string? token = ctx.Request.Query["token"];
            if (string.IsNullOrEmpty(token))
            {
                await ws.CloseAsync(WebSocketCloseStatus.PolicyViolation, "No token", default);
                return;
            }

            int? playerId;
            using (var scope = _scopeFactory.CreateScope())
            {
                var tokens = scope.ServiceProvider.GetRequiredService<TokenService>();
                playerId = tokens.GetPlayerIdFromToken(token);
            }
            if (playerId == null)
            {
                await ws.CloseAsync(WebSocketCloseStatus.PolicyViolation, "Invalid token", default);
                return;
            }

            var state = new MpPlayerState { Id = playerId.Value };
            bool registered = false;
            var buf = new byte[1024];

            try
            {
                while (ws.State == WebSocketState.Open)
                {
                    WebSocketReceiveResult result;
                    try { result = await ws.ReceiveAsync(buf, default); }
                    catch { break; }

                    if (result.MessageType == WebSocketMessageType.Close) break;
                    if (result.Count == 0) continue;

                    string? msgType = null;
                    int enemyIndex = -1;

                    try
                    {
                        var doc = JsonDocument.Parse(new ReadOnlyMemory<byte>(buf, 0, result.Count));
                        var root = doc.RootElement;

                        if (root.TryGetProperty("type", out var vt)) msgType = vt.GetString();
                        if (root.TryGetProperty("x", out var vx)) state.X = vx.GetSingle();
                        if (root.TryGetProperty("y", out var vy)) state.Y = vy.GetSingle();
                        if (root.TryGetProperty("lv", out var vl)) state.Level = vl.GetInt32();
                        if (root.TryGetProperty("fr", out var vf)) state.FacingRight = vf.GetBoolean();
                        if (root.TryGetProperty("anim", out var va)) state.Anim = va.GetString() ?? "idle";
                        if (root.TryGetProperty("name", out var vn)) state.Username = vn.GetString() ?? state.Username;
                        if (root.TryGetProperty("src", out var vs)) state.Src = vs.GetString() ?? state.Src;
                        if (root.TryGetProperty("phase", out var vph)) state.GamePhase = vph.GetString() ?? "playing";
                        if (root.TryGetProperty("enemyIndex", out var ve)) enemyIndex = ve.GetInt32();
                    }
                    catch { continue; }

                    // Register on first message
                    if (!registered)
                    {
                        if (state.Src == "web") _webClients[playerId.Value] = (ws, state);
                        else _gameClients[playerId.Value] = (ws, state);
                        registered = true;

                        // Replay killed enemies for a late-joining game client
                        if (state.Src == "game")
                        {
                            var killed = _killedEnemies.GetOrAdd(
                                state.Level, _ => new ConcurrentDictionary<int, bool>());
                            foreach (var ki in killed.Keys)
                            {
                                var kb = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
                                    new { type = "enemy_kill", enemyIndex = ki },
                                    new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
                                try { await ws.SendAsync(kb, WebSocketMessageType.Text, true, default); }
                                catch { }
                            }
                        }
                        _logger.LogInformation("[MP] {Id} ({Src}) connected phase={Phase}",
                            playerId, state.Src, state.GamePhase);
                    }

                    // Web client sent a logout — relay to game client then close web WS
                    if (msgType == "logout" && state.Src == "web")
                    {
                        await ForwardLogoutToGame(playerId.Value);
                        break;
                    }

                    // Enemy-kill broadcast (game → others on same level)
                    if (msgType == "enemy_kill" && enemyIndex >= 0 && state.Src == "game")
                    {
                        await BroadcastEnemyKill(playerId.Value, state.Level, enemyIndex);
                        continue;
                    }

                    // Build player-list response
                    IEnumerable<MpOtherPlayer> others;
                    if (state.Src == "game")
                    {
                        others = _gameClients
                            .Where(kv => kv.Key != playerId.Value
                                      && kv.Value.State.Level == state.Level
                                      && kv.Value.Ws.State == WebSocketState.Open
                                      && kv.Value.State.GamePhase != "menu")
                            .Select(kv => new MpOtherPlayer(
                                kv.Value.State.Id, kv.Value.State.Username,
                                kv.Value.State.X, kv.Value.State.Y,
                                kv.Value.State.FacingRight, kv.Value.State.Anim,
                                kv.Value.State.GamePhase));
                    }
                    else
                    {
                        others = _gameClients
                            .Where(kv => kv.Value.State.Level == state.Level
                                      && kv.Value.Ws.State == WebSocketState.Open
                                      && kv.Value.State.GamePhase != "menu")
                            .Select(kv => new MpOtherPlayer(
                                kv.Value.State.Id, kv.Value.State.Username,
                                kv.Value.State.X, kv.Value.State.Y,
                                kv.Value.State.FacingRight, kv.Value.State.Anim,
                                kv.Value.State.GamePhase));
                    }

                    var respBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
                        others.ToArray(),
                        new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
                    try { await ws.SendAsync(respBytes, WebSocketMessageType.Text, true, default); }
                    catch { break; }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[MP] {Id} error: {Msg}", playerId, ex.Message);
            }
            finally
            {
                if (state.Src == "web")
                {
                    _webClients.TryRemove(playerId.Value, out _);
                }
                else
                {
                    _gameClients.TryRemove(playerId.Value, out _);
                    bool levelEmpty = !_gameClients.Values
                        .Any(c => c.State.Level == state.Level && c.Ws.State == WebSocketState.Open);
                    if (levelEmpty) _killedEnemies.TryRemove(state.Level, out _);
                }
                _logger.LogInformation("[MP] {Id} ({Src}) disconnected", playerId, state.Src);
                if (ws.State is WebSocketState.Open or WebSocketState.CloseReceived)
                    try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Bye", default); }
                    catch { }
            }
        }
    }
}