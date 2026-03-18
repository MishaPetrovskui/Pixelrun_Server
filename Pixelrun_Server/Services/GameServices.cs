using Pixelrun_Server.Models;
using Microsoft.EntityFrameworkCore;

namespace Pixelrun_Server.Services
{
    public class PlayerService
    {
        private readonly GameDbContext _db;
        public PlayerService(GameDbContext db) => _db = db;

        public List<Player> GetAll() => _db.Players.ToList();

        public Player? GetById(int id) => _db.Players.Find(id);

        public Player? Validate(PlayerLoginDTO dto)
        {
            var player = _db.Players.FirstOrDefault(p => p.Email == dto.Email);
            if (player == null) return null;
            if (!BCrypt.Net.BCrypt.Verify(dto.Password, player.PasswordHash)) return null;
            return player;
        }

        public Player Register(PlayerRegisterDTO dto)
        {
            var player = new Player
            {
                Username = dto.Username,
                Email = dto.Email,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password),
                Coins = 0
            };
            _db.Players.Add(player);
            _db.SaveChanges();

            _db.OwnedSkins.AddRange(
                new OwnedSkin { PlayerId = player.Id, SkinId = "default" },
                new OwnedSkin { PlayerId = player.Id, SkinId = "bar_default" },
                new OwnedSkin { PlayerId = player.Id, SkinId = "slash_default" }
            );

            var quests = _db.Quests.ToList();
            foreach (var q in quests)
                _db.PlayerQuests.Add(new PlayerQuest { PlayerId = player.Id, QuestId = q.Id });

            _db.SaveChanges();
            return player;
        }

        public (bool ok, string error, Player? player) UpdateProfile(int playerId, UpdateProfileDTO dto)
        {
            var player = _db.Players.Find(playerId);
            if (player == null) return (false, "Player not found", null);

            if (!string.IsNullOrWhiteSpace(dto.Username))
            {
                if (dto.Username.Length < 3 || dto.Username.Length > 32)
                    return (false, "Username must be 3–32 characters", null);
                bool taken = _db.Players.Any(p => p.Username == dto.Username && p.Id != playerId);
                if (taken) return (false, "Username already taken", null);
                player.Username = dto.Username;
            }

            if (!string.IsNullOrWhiteSpace(dto.Email))
            {
                bool taken = _db.Players.Any(p => p.Email == dto.Email && p.Id != playerId);
                if (taken) return (false, "Email already in use", null);
                player.Email = dto.Email;
            }

            if (!string.IsNullOrWhiteSpace(dto.NewPassword))
            {
                if (string.IsNullOrWhiteSpace(dto.CurrentPassword))
                    return (false, "Current password required to set a new one", null);
                if (!BCrypt.Net.BCrypt.Verify(dto.CurrentPassword, player.PasswordHash))
                    return (false, "Current password is incorrect", null);
                if (dto.NewPassword.Length < 8)
                    return (false, "New password must be at least 8 characters", null);
                player.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword);
            }

            _db.SaveChanges();
            return (true, "", player);
        }

        public bool AddCoins(int playerId, int amount)
        {
            var player = _db.Players.Find(playerId);
            if (player == null) return false;
            player.Coins += amount;
            _db.SaveChanges();
            return true;
        }

        public bool SpendCoins(int playerId, int amount)
        {
            var player = _db.Players.Find(playerId);
            if (player == null || player.Coins < amount) return false;
            player.Coins -= amount;
            _db.SaveChanges();
            return true;
        }
    }

    public class RecordService
    {
        private readonly GameDbContext _db;
        public RecordService(GameDbContext db) => _db = db;

        public List<LevelRecord> GetLeaderboard(int level, int top = 10)
            => _db.LevelRecords
                .Where(r => r.Level == level)
                .Include(r => r.Player)
                .ToList()
                .GroupBy(r => r.PlayerId)
                .Select(g => g.OrderBy(r => r.Time).First())
                .OrderBy(r => r.Time)
                .Take(top)
                .ToList();

        public LevelRecord? GetPlayerBest(int playerId, int level)
            => _db.LevelRecords
                .Where(r => r.PlayerId == playerId && r.Level == level)
                .OrderBy(r => r.Time)
                .FirstOrDefault();

        public LevelRecord Submit(int playerId, LevelRecordDTO dto)
        {
            var existing = _db.LevelRecords
                .FirstOrDefault(r => r.PlayerId == playerId && r.Level == dto.Level);

            if (existing != null)
            {
                if (existing.Time <= dto.Time) return existing;
                existing.Time = dto.Time;
                existing.Coins = dto.Coins;
                existing.Kills = dto.Kills;
                existing.SetAt = DateTime.UtcNow;
                _db.SaveChanges();
                return existing;
            }

            var record = new LevelRecord
            {
                PlayerId = playerId,
                Level = dto.Level,
                Time = dto.Time,
                Coins = dto.Coins,
                Kills = dto.Kills,
                SetAt = DateTime.UtcNow
            };
            _db.LevelRecords.Add(record);
            _db.SaveChanges();
            return record;
        }
    }

    public class ShopService
    {
        private readonly GameDbContext _db;
        private readonly PlayerService _playerService;

        public ShopService(GameDbContext db, PlayerService playerService)
        {
            _db = db;
            _playerService = playerService;
        }

        public List<Skin> GetAllSkins() => _db.Skins.ToList();

        public List<Skin> GetOwnedSkins(int playerId)
            => _db.OwnedSkins
                .Where(o => o.PlayerId == playerId)
                .Include(o => o.Skin)
                .Select(o => o.Skin!)
                .ToList();

        public (bool ok, string error) BuySkin(int playerId, string skinId)
        {
            var skin = _db.Skins.Find(skinId);
            if (skin == null) return (false, "Скин не найден");

            bool alreadyOwned = _db.OwnedSkins.Any(o => o.PlayerId == playerId && o.SkinId == skinId);
            if (alreadyOwned) return (false, "Уже куплено");

            if (!_playerService.SpendCoins(playerId, skin.Price))
                return (false, "Недостаточно монет");

            _db.OwnedSkins.Add(new OwnedSkin { PlayerId = playerId, SkinId = skinId });
            _db.SaveChanges();
            return (true, "");
        }

        public (bool ok, string error) EquipSkin(int playerId, string skinId)
        {
            bool owns = _db.OwnedSkins.Any(o => o.PlayerId == playerId && o.SkinId == skinId);
            if (!owns) return (false, "Скин не куплен");

            var skin = _db.Skins.Find(skinId);
            var player = _db.Players.Find(playerId);
            if (skin == null || player == null) return (false, "Не найдено");

            switch (skin.Type)
            {
                case "player": player.EquippedPlayerSkin = skinId; break;
                case "bar": player.EquippedBarSkin = skinId; break;
                case "slash": player.EquippedSlashSkin = skinId; break;
                default: return (false, "Неизвестный тип скина");
            }
            _db.SaveChanges();
            return (true, "");
        }
    }

    public class QuestService
    {
        private readonly GameDbContext _db;
        private readonly PlayerService _playerService;

        public QuestService(GameDbContext db, PlayerService playerService)
        {
            _db = db;
            _playerService = playerService;
        }

        public List<PlayerQuest> GetPlayerQuests(int playerId)
            => _db.PlayerQuests
                .Where(pq => pq.PlayerId == playerId)
                .Include(pq => pq.Quest)
                .ToList();

        public void UpdateProgress(int playerId, string type, int value)
        {
            var quests = _db.PlayerQuests
                .Where(pq => pq.PlayerId == playerId && !pq.Completed)
                .Include(pq => pq.Quest)
                .Where(pq => pq.Quest!.Type == type)
                .ToList();

            foreach (var pq in quests)
            {
                if (type == "time")
                    pq.CurrentValue = value;
                else
                    pq.CurrentValue += value;

                bool done = type == "time"
                    ? pq.CurrentValue <= pq.Quest!.TargetValue
                    : pq.CurrentValue >= pq.Quest!.TargetValue;

                if (done) pq.Completed = true;
            }
            _db.SaveChanges();
        }

        public (bool ok, int reward, string error) ClaimReward(int playerId, string questId)
        {
            var pq = _db.PlayerQuests
                .Include(q => q.Quest)
                .FirstOrDefault(q => q.PlayerId == playerId && q.QuestId == questId);

            if (pq == null) return (false, 0, "Квест не найден");
            if (!pq.Completed) return (false, 0, "Квест не выполнен");
            if (pq.Claimed) return (false, 0, "Награда уже получена");

            pq.Claimed = true;
            _db.SaveChanges();

            int reward = pq.Quest!.Reward;
            _playerService.AddCoins(playerId, reward);
            return (true, reward, "");
        }
    }
}