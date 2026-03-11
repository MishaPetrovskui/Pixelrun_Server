using Pixelrun_Server.Models;
using Microsoft.EntityFrameworkCore;
using Pixelrun_Server.Models;

namespace Pixelrun_Server
{
    public class GameDbContext : DbContext
    {
        public GameDbContext(DbContextOptions<GameDbContext> options) : base(options) { }

        public DbSet<Player> Players { get; set; }
        public DbSet<LevelRecord> LevelRecords { get; set; }
        public DbSet<Skin> Skins { get; set; }
        public DbSet<OwnedSkin> OwnedSkins { get; set; }
        public DbSet<Quest> Quests { get; set; }
        public DbSet<PlayerQuest> PlayerQuests { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Skin>().HasData(
                new Skin { Id = "default", Name = "Default", Type = "player", Price = 0, Description = "Default skin" },
                new Skin { Id = "ninja", Name = "Ninja", Type = "player", Price = 200, Description = "Fast ninja" },
                new Skin { Id = "knight", Name = "Knight", Type = "player", Price = 350, Description = "Heavy knight" },
                new Skin { Id = "ghost", Name = "Ghost", Type = "player", Price = 500, Description = "Ghost" },

                new Skin { Id = "bar_default", Name = "Default Bar", Type = "bar", Price = 0, Description = "Default bar" },
                new Skin { Id = "bar_fire", Name = "Fire Bar", Type = "bar", Price = 150, Description = "Fire bar" },
                new Skin { Id = "bar_ice", Name = "Ice Bar", Type = "bar", Price = 150, Description = "Ice bar" },

                new Skin { Id = "slash_default", Name = "Default Slash", Type = "slash", Price = 0, Description = "Default slash" },
                new Skin { Id = "slash_fire", Name = "Fire Slash", Type = "slash", Price = 200, Description = "Fire slash" },
                new Skin { Id = "slash_electric", Name = "Electric Slash", Type = "slash", Price = 250, Description = "Electric slash" }
            );

            modelBuilder.Entity<Quest>().HasData(
                new Quest { Id = "kills_5", Title = "First Blood", Description = "Kill 5 enemies", Type = "kills", TargetValue = 5, Reward = 50 },
                new Quest { Id = "kills_20", Title = "Hunter", Description = "Kill 20 enemies", Type = "kills", TargetValue = 20, Reward = 150 },
                new Quest { Id = "coins_10", Title = "Collector", Description = "Collect 10 coins", Type = "coins", TargetValue = 10, Reward = 30 },
                new Quest { Id = "coins_50", Title = "Rich", Description = "Collect 50 coins", Type = "coins", TargetValue = 50, Reward = 100 },
                new Quest { Id = "levels_1", Title = "First Level", Description = "Complete 1 level", Type = "levels", TargetValue = 1, Reward = 75 },
                new Quest { Id = "levels_5", Title = "Veteran", Description = "Complete 5 levels", Type = "levels", TargetValue = 5, Reward = 300 },
                new Quest { Id = "time_120", Title = "Speedrunner", Description = "Finish level < 2 min", Type = "time", TargetValue = 120, Reward = 200 }
            );
        }
    }
}