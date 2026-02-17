using Microsoft.EntityFrameworkCore;
using Storarr.Models;

namespace Storarr.Data
{
    public class StorarrDbContext : DbContext
    {
        public StorarrDbContext(DbContextOptions<StorarrDbContext> options)
            : base(options)
        {
        }

        public DbSet<MediaItem> MediaItems { get; set; } = null!;
        public DbSet<Config> Configs { get; set; } = null!;
        public DbSet<ActivityLog> ActivityLogs { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<MediaItem>(entity =>
            {
                entity.HasIndex(e => e.FilePath).IsUnique();
                entity.HasIndex(e => e.JellyfinId);
                entity.HasIndex(e => e.SonarrId);
                entity.HasIndex(e => e.RadarrId);
                entity.HasIndex(e => e.CurrentState);
            });

            modelBuilder.Entity<Config>(entity =>
            {
                entity.HasData(new Config
                {
                    Id = 1,
                    // Service URLs and API Keys (pre-configured for testing)
                    JellyfinUrl = "http://localhost:8096",
                    JellyfinApiKey = "YOUR_JELLYFIN_API_KEY",
                    JellyseerrUrl = "http://localhost:5055",
                    JellyseerrApiKey = "YOUR_JELLYSEERR_API_KEY",
                    SonarrUrl = "http://localhost:8989",
                    SonarrApiKey = "YOUR_SONARR_API_KEY",
                    RadarrUrl = "http://localhost:7878",
                    RadarrApiKey = "YOUR_RADARR_API_KEY",
                    // Default settings
                    FirstRunComplete = true,
                    LibraryMode = LibraryMode.NewContentOnly,
                    MediaLibraryPath = "/media",
                    SymlinkToMkvValue = 7,
                    SymlinkToMkvUnit = TimeUnit.Days,
                    MkvToSymlinkValue = 30,
                    MkvToSymlinkUnit = TimeUnit.Days
                });
            });

            modelBuilder.Entity<ActivityLog>(entity =>
            {
                entity.HasIndex(e => e.Timestamp);
                entity.HasOne(e => e.MediaItem)
                    .WithMany()
                    .HasForeignKey(e => e.MediaItemId)
                    .OnDelete(DeleteBehavior.Cascade);
            });
        }
    }
}
