using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Pixelrun_Server.Models
{
    public class Player
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }
        public string Username { get; set; } = "";
        public string Email { get; set; } = "";
        public string PasswordHash { get; set; } = "";
        public int Coins { get; set; } = 0;
        public string EquippedPlayerSkin { get; set; } = "default";
        public string EquippedBarSkin { get; set; } = "default";
        public string EquippedSlashSkin { get; set; } = "default";
        public bool IsAdmin { get; set; } = false;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class PlayerLoginDTO
    {
        public string Email { get; set; } = "";
        public string Password { get; set; } = "";
    }

    public class PlayerRegisterDTO
    {
        public string Username { get; set; } = "";
        public string Email { get; set; } = "";
        public string Password { get; set; } = "";
    }

    public class UpdateProfileDTO
    {
        public string? Username { get; set; }
        public string? Email { get; set; }
        public string? NewPassword { get; set; }
        public string? CurrentPassword { get; set; }
    }

    public class LevelRecord
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }
        public int PlayerId { get; set; }
        public int Level { get; set; }
        public float Time { get; set; }
        public int Coins { get; set; }
        public int Kills { get; set; }
        public DateTime SetAt { get; set; } = DateTime.UtcNow;

        [ForeignKey("PlayerId")]
        public Player? Player { get; set; }
    }

    public class LevelRecordDTO
    {
        public int Level { get; set; }
        public float Time { get; set; }
        public int Coins { get; set; }
        public int Kills { get; set; }
    }

    public class Skin
    {
        [Key]
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";
        public int Price { get; set; }
        public string Description { get; set; } = "";
    }

    public class OwnedSkin
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }
        public int PlayerId { get; set; }
        public string SkinId { get; set; } = "";

        [ForeignKey("PlayerId")]
        public Player? Player { get; set; }
        [ForeignKey("SkinId")]
        public Skin? Skin { get; set; }
    }

    public class Quest
    {
        [Key]
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public string Type { get; set; } = "";
        public int TargetValue { get; set; }
        public int Reward { get; set; }
    }

    public class PlayerQuest
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }
        public int PlayerId { get; set; }
        public string QuestId { get; set; } = "";
        public int CurrentValue { get; set; } = 0;
        public bool Completed { get; set; } = false;
        public bool Claimed { get; set; } = false;

        [ForeignKey("PlayerId")]
        public Player? Player { get; set; }
        [ForeignKey("QuestId")]
        public Quest? Quest { get; set; }
    }

    public class QuestProgressDTO
    {
        public string QuestId { get; set; } = "";
        public int Value { get; set; }
    }

    public class EquipDTO
    {
        public string SkinId { get; set; } = "";
    }

    public class CoinsDTO
    {
        public int Amount { get; set; }
    }

    public class SetAdminDTO
    {
        public bool IsAdmin { get; set; }
    }
}