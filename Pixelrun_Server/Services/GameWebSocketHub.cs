using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Pixelrun_Server.Services;

namespace Pixelrun_Server
{
    // State of one connected player in multiplayer
    public class MpPlayerState
    {
        public int Id { get; set; }
        public string Username { get; set; } = "";
        public float X { get; set; }
        public float Y { get; set; }
        public int Level { get; set; }
        public bool FacingRight { get; set; } = true;
        public string Anim { get; set; } = "idle";
    }

    // DTO sent to each client
    record MpOtherPlayer(int Id, string Username, float X, float Y, bool FacingRight, string Anim);

    public class MultiplayerHub
    {
        // Global across all requests (singleton lifetime required — registered as Singleton)
        private static readonly ConcurrentDictionary<int, (WebSocket Ws, MpPlayerState State)> _clients = new();

        /// <summary>Returns { level -> count } for all active WebSocket connections.</summary>
        public static Dictionary<int, int> GetLevelCounts()
        {
            var result = new Dictionary<int, int>();
            foreach (var kv in _clients)
            {
                if (kv.Value.Ws.State != WebSocketState.Open) continue;
                int lv = kv.Value.State.Level;
                result[lv] = result.TryGetValue(lv, out int c) ? c + 1 : 1;
            }
            return result;
        }

        /// <summary>Returns snapshot of all online players.</summary>
        public static List<MpPlayerState> GetOnlinePlayers()
            => _clients
                .Where(kv => kv.Value.Ws.State == WebSocketState.Open)
                .Select(kv => kv.Value.State)
                .ToList();

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<MultiplayerHub> _logger;

        public MultiplayerHub(IServiceScopeFactory scopeFactory, ILogger<MultiplayerHub> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        public async Task HandleAsync(HttpContext ctx, WebSocket ws)
        {
            // --- Auth (create short scope to resolve scoped TokenService) ---
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
            _clients[playerId.Value] = (ws, state);
            _logger.LogInformation("[MP] Player {Id} connected ({Total} total)", playerId, _clients.Count);

            var buf = new byte[512];
            try
            {
                while (ws.State == WebSocketState.Open)
                {
                    WebSocketReceiveResult result;
                    try { result = await ws.ReceiveAsync(buf, default); }
                    catch { break; }

                    if (result.MessageType == WebSocketMessageType.Close) break;
                    if (result.Count == 0) continue;

                    // --- Parse incoming position ---
                    try
                    {
                        var doc = JsonDocument.Parse(new ReadOnlyMemory<byte>(buf, 0, result.Count));
                        var root = doc.RootElement;
                        state.X = root.TryGetProperty("x", out var vx) ? vx.GetSingle() : state.X;
                        state.Y = root.TryGetProperty("y", out var vy) ? vy.GetSingle() : state.Y;
                        state.Level = root.TryGetProperty("lv", out var vl) ? vl.GetInt32() : state.Level;
                        state.FacingRight = root.TryGetProperty("fr", out var vf) ? vf.GetBoolean() : state.FacingRight;
                        state.Anim = root.TryGetProperty("anim", out var va) ? va.GetString() ?? "idle" : state.Anim;
                        if (root.TryGetProperty("name", out var vn)) state.Username = vn.GetString() ?? state.Username;
                    }
                    catch { continue; } // ignore malformed

                    // --- Build response: other players on same level ---
                    var others = _clients
                        .Where(kv => kv.Key != playerId.Value
                                  && kv.Value.State.Level == state.Level
                                  && kv.Value.Ws.State == WebSocketState.Open)
                        .Select(kv => new MpOtherPlayer(
                            kv.Value.State.Id,
                            kv.Value.State.Username,
                            kv.Value.State.X,
                            kv.Value.State.Y,
                            kv.Value.State.FacingRight,
                            kv.Value.State.Anim))
                        .ToArray();

                    var response = JsonSerializer.Serialize(new { players = others },
                        new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

                    var bytes = Encoding.UTF8.GetBytes(response);
                    try
                    {
                        await ws.SendAsync(bytes, WebSocketMessageType.Text, true, default);
                    }
                    catch { break; }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[MP] Player {Id} error: {Msg}", playerId, ex.Message);
            }
            finally
            {
                _clients.TryRemove(playerId.Value, out _);
                _logger.LogInformation("[MP] Player {Id} disconnected ({Total} remaining)", playerId, _clients.Count);
                if (ws.State is WebSocketState.Open or WebSocketState.CloseReceived)
                {
                    try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Bye", default); }
                    catch { }
                }
            }
        }
    }
}