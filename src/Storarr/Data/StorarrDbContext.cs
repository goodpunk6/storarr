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
                    JellyfinUrl = "https://jelly.mannyg.stream",
                    JellyfinApiKey = "deda990a369a438f8c40af798b273162",
                    JellyseerrUrl = "https://requests.mannyg.stream",
                    JellyseerrApiKey = "MTc2OTY1MDM3NjU1MDc1NmEzYzBjLWY4OTctNDM4Zi1hZjNlLTdmYmU5YjQyMjg0MQ==",
                    SonarrUrl = "https://sonarr.mannyg.stream",
                    SonarrApiKey = "e93097d3bc5348f5816be9b15bcf52a9",
                    RadarrUrl = "https://radarr.mannyg.stream",
                    RadarrApiKey = "7bf1e6972098410fba1d4144d1169a3e",
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
