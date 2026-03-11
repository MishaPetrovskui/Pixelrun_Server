using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Pixelrun_Server.Models;
using Pixelrun_Server.Services;

namespace Pixelrun_Server.Controllers
{
    public abstract class GameControllerBase : ControllerBase
    {
        protected int GetPlayerId()
            => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    }

    [ApiController]
    [Route("api/auth")]
    public class AuthController : ControllerBase
    {
        private readonly PlayerService _players;
        private readonly TokenService _tokens;

        public AuthController(PlayerService players, TokenService tokens)
        {
            _players = players;
            _tokens = tokens;
        }

        [HttpPost("register")]
        public ActionResult Register([FromBody] PlayerRegisterDTO dto)
        {
            var player = _players.Register(dto);
            var token = _tokens.GenerateToken(player);
            return Ok(new { token, player.Id, player.Username });
        }

        [HttpPost("login")]
        public ActionResult Login([FromBody] PlayerLoginDTO dto)
        {
            var player = _players.Validate(dto);
            if (player == null) return Unauthorized(new { error = "Неверный email или пароль" });
            var token = _tokens.GenerateToken(player);
            return Ok(new
            {
                token,
                player.Id,
                player.Username,
                player.Coins,
                player.EquippedPlayerSkin,
                player.EquippedBarSkin,
                player.EquippedSlashSkin
            });
        }
    }

    [ApiController]
    [Route("api/player")]
    [Authorize]
    public class PlayerController : GameControllerBase
    {
        private readonly PlayerService _players;
        public PlayerController(PlayerService players) => _players = players;

        [HttpGet("me")]
        public ActionResult GetMe()
        {
            var p = _players.GetById(GetPlayerId());
            if (p == null) return NotFound();
            return Ok(new
            {
                p.Id,
                p.Username,
                p.Email,
                p.Coins,
                p.EquippedPlayerSkin,
                p.EquippedBarSkin,
                p.EquippedSlashSkin
            });
        }

        [HttpPost("coins/add")]
        public ActionResult AddCoins([FromBody] CoinsDTO dto)
        {
            _players.AddCoins(GetPlayerId(), dto.Amount);
            return Ok(new { coins = _players.GetById(GetPlayerId())!.Coins });
        }
    }

    [ApiController]
    [Route("api/records")]
    [Authorize]
    public class RecordsController : GameControllerBase
    {
        private readonly RecordService _records;
        private readonly QuestService _quests;
        private readonly PlayerService _players;

        public RecordsController(RecordService records, QuestService quests, PlayerService players)
        {
            _records = records;
            _quests = quests;
            _players = players;
        }

        [HttpPost("submit")]
        public ActionResult Submit([FromBody] LevelRecordDTO dto)
        {
            int pid = GetPlayerId();
            var record = _records.Submit(pid, dto);

            _quests.UpdateProgress(pid, "kills", dto.Kills);
            _quests.UpdateProgress(pid, "coins", dto.Coins);
            _quests.UpdateProgress(pid, "levels", 1);
            _quests.UpdateProgress(pid, "time", (int)dto.Time);

            return Ok(record);
        }

        [HttpGet("my/{level}")]
        public ActionResult GetMy(int level)
        {
            var r = _records.GetPlayerBest(GetPlayerId(), level);
            if (r == null) return NotFound();
            return Ok(r);
        }

        [AllowAnonymous]
        [HttpGet("leaderboard/{level}")]
        public ActionResult Leaderboard(int level, [FromQuery] int top = 10)
        {
            var list = _records.GetLeaderboard(level, top);
            return Ok(list.Select((r, i) => new
            {
                rank = i + 1,
                username = r.Player?.Username,
                r.Time,
                r.Kills,
                r.Coins,
                r.SetAt
            }));
        }
    }

    [ApiController]
    [Route("api/shop")]
    [Authorize]
    public class ShopController : GameControllerBase
    {
        private readonly ShopService _shop;
        public ShopController(ShopService shop) => _shop = shop;

        [AllowAnonymous]
        [HttpGet("skins")]
        public ActionResult GetAllSkins()
            => Ok(_shop.GetAllSkins());

        [HttpGet("owned")]
        public ActionResult GetOwned()
            => Ok(_shop.GetOwnedSkins(GetPlayerId()));

        [HttpPost("buy/{skinId}")]
        public ActionResult Buy(string skinId)
        {
            var (ok, error) = _shop.BuySkin(GetPlayerId(), skinId);
            if (!ok) return BadRequest(new { error });
            return Ok(new { message = "Скин куплен" });
        }

        [HttpPost("equip")]
        public ActionResult Equip([FromBody] EquipDTO dto)
        {
            var (ok, error) = _shop.EquipSkin(GetPlayerId(), dto.SkinId);
            if (!ok) return BadRequest(new { error });
            return Ok(new { message = "Скин надет" });
        }
    }

    [ApiController]
    [Route("api/quests")]
    [Authorize]
    public class QuestsController : GameControllerBase
    {
        private readonly QuestService _quests;
        public QuestsController(QuestService quests) => _quests = quests;

        [HttpGet]
        public ActionResult GetQuests()
        {
            var list = _quests.GetPlayerQuests(GetPlayerId());
            return Ok(list.Select(pq => new
            {
                pq.QuestId,
                pq.Quest!.Title,
                pq.Quest.Description,
                pq.Quest.Type,
                pq.Quest.TargetValue,
                pq.Quest.Reward,
                pq.CurrentValue,
                pq.Completed,
                pq.Claimed
            }));
        }

        [HttpPost("claim/{questId}")]
        public ActionResult Claim(string questId)
        {
            var (ok, reward, error) = _quests.ClaimReward(GetPlayerId(), questId);
            if (!ok) return BadRequest(new { error });
            return Ok(new { reward, message = $"Получено {reward} монет!" });
        }
    }

    [ApiController]
    [Route("api/admin")]
    public class AdminController : ControllerBase
    {
        private readonly GameDbContext _db;
        public AdminController(GameDbContext db) => _db = db;

        [HttpGet("users")]
        public ActionResult GetUsers()
        {
            var users = _db.Players
                .Select(p => new {
                    p.Id,
                    p.Username,
                    p.Email,
                    p.Coins,
                    p.EquippedPlayerSkin,
                    p.EquippedBarSkin,
                    p.EquippedSlashSkin,
                    OwnedSkins = _db.OwnedSkins
                        .Where(o => o.PlayerId == p.Id)
                        .Select(o => o.SkinId).ToList(),
                    Records = _db.LevelRecords
                        .Where(r => r.PlayerId == p.Id)
                        .Select(r => new { r.Level, r.Time, r.Coins, r.Kills, r.SetAt }).ToList(),
                    Quests = _db.PlayerQuests
                        .Where(q => q.PlayerId == p.Id)
                        .Select(q => new { q.QuestId, q.CurrentValue, q.Completed, q.Claimed }).ToList()
                }).ToList();

            return Ok(new { total = users.Count, users });
        }

        [HttpGet("users/{id}")]
        public ActionResult GetUser(int id)
        {
            var p = _db.Players.Find(id);
            if (p == null) return NotFound(new { error = $"User {id} not found" });
            return Ok(new
            {
                p.Id,
                p.Username,
                p.Email,
                p.Coins,
                p.EquippedPlayerSkin,
                p.EquippedBarSkin,
                p.EquippedSlashSkin,
                OwnedSkins = _db.OwnedSkins
                    .Where(o => o.PlayerId == id).Select(o => o.SkinId).ToList(),
                Records = _db.LevelRecords
                    .Where(r => r.PlayerId == id).OrderBy(r => r.Level)
                    .Select(r => new { r.Level, r.Time, r.Coins, r.Kills, r.SetAt }).ToList(),
                Quests = _db.PlayerQuests
                    .Where(q => q.PlayerId == id)
                    .Select(q => new { q.QuestId, q.CurrentValue, q.Completed, q.Claimed }).ToList()
            });
        }

        [HttpDelete("users/{id}")]
        public ActionResult DeleteUser(int id)
        {
            var p = _db.Players.Find(id);
            if (p == null) return NotFound(new { error = $"User {id} not found" });
            _db.Players.Remove(p);
            _db.SaveChanges();
            return Ok(new { message = $"User '{p.Username}' deleted" });
        }

        [HttpPatch("users/{id}/coins")]
        public ActionResult SetCoins(int id, [FromBody] CoinsDTO dto)
        {
            var p = _db.Players.Find(id);
            if (p == null) return NotFound(new { error = $"User {id} not found" });
            p.Coins = dto.Amount;
            _db.SaveChanges();
            return Ok(new { p.Id, p.Username, p.Coins });
        }
    }
}