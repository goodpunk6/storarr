using System;
using System.IO;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi.Models;
using Storarr.BackgroundServices;
using Storarr.Data;
using Storarr.Hubs;
using Storarr.Services;

namespace Storarr
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var host = CreateHostBuilder(args).Build();

            // Initialize database BEFORE starting the host (and background services)
            using (var scope = host.Services.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<StorarrDbContext>();

                // Check if __EFMigrationsHistory table exists (indicates migrations have been used)
                var migrationsTableExists = dbContext.Database.ExecuteSqlRaw(
                    "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='__EFMigrationsHistory'") > 0;

                // Check if Configs table exists (indicates database has been initialized)
                var configsTableExists = dbContext.Database.ExecuteSqlRaw(
                    "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Configs'") > 0;

                if (migrationsTableExists)
                {
                    // Use normal migrations if history table exists
                    dbContext.Database.Migrate();
                }
                else if (!configsTableExists)
                {
                    // Fresh database - create all tables using EnsureCreated
                    dbContext.Database.EnsureCreated();
                }
                else
                {
                    // Legacy database without migrations - apply schema changes manually
                    // First ensure the ExcludedItems table exists
                    dbContext.Database.ExecuteSqlRaw(@"
                        CREATE TABLE IF NOT EXISTS ExcludedItems (
                            Id INTEGER PRIMARY KEY AUTOINCREMENT,
                            Title TEXT NOT NULL,
                            Type INTEGER NOT NULL,
                            TmdbId INTEGER NULL,
                            TvdbId INTEGER NULL,
                            SonarrId INTEGER NULL,
                            RadarrId INTEGER NULL,
                            Reason TEXT NULL,
                            CreatedAt TEXT NOT NULL
                        )");

                    // Create indexes if they don't exist
                    dbContext.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_ExcludedItems_TmdbId ON ExcludedItems (TmdbId)");
                    dbContext.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_ExcludedItems_TvdbId ON ExcludedItems (TvdbId)");
                    dbContext.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_ExcludedItems_SonarrId ON ExcludedItems (SonarrId)");
                    dbContext.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_ExcludedItems_RadarrId ON ExcludedItems (RadarrId)");

                    // Add multi-drive columns to Configs table if they don't exist
                    AddColumnIfNotExists(dbContext, "Configs", "MultiDriveEnabled", "INTEGER NOT NULL DEFAULT 0");
                    AddColumnIfNotExists(dbContext, "Configs", "SymlinkStoragePath", "TEXT NULL");
                    AddColumnIfNotExists(dbContext, "Configs", "MkvStoragePath", "TEXT NULL");
                    AddColumnIfNotExists(dbContext, "Configs", "SonarrSymlinkRootFolder", "TEXT NULL");
                    AddColumnIfNotExists(dbContext, "Configs", "SonarrMkvRootFolder", "TEXT NULL");
                    AddColumnIfNotExists(dbContext, "Configs", "RadarrSymlinkRootFolder", "TEXT NULL");
                    AddColumnIfNotExists(dbContext, "Configs", "RadarrMkvRootFolder", "TEXT NULL");
                }
            }

            host.Run();
        }

        private static void AddColumnIfNotExists(StorarrDbContext dbContext, string tableName, string columnName, string columnDefinition)
        {
            try
            {
                // Check if column exists
                var sql = $"SELECT COUNT(*) FROM pragma_table_info('{tableName}') WHERE name='{columnName}'";
                using var command = dbContext.Database.GetDbConnection().CreateCommand();
                command.CommandText = sql;
                dbContext.Database.OpenConnection();
                var result = command.ExecuteScalar();
                var count = result != null ? Convert.ToInt32(result) : 0;
                dbContext.Database.CloseConnection();

                if (count == 0)
                {
                    dbContext.Database.ExecuteSqlRaw($"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnDefinition}");
                }
            }
            catch
            {
                // Column might already exist or other error - ignore
            }
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<Startup>();
                });
    }

    public class Startup
    {
        public IConfiguration Configuration { get; }

        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public void ConfigureServices(IServiceCollection services)
        {
            // Database
            var dataDir = Configuration["DataDirectory"] ?? Path.Combine(Directory.GetCurrentDirectory(), "data");
            Directory.CreateDirectory(dataDir);
            var dbPath = Path.Combine(dataDir, "storarr.db");

            services.AddDbContext<StorarrDbContext>(options =>
                options.UseSqlite($"Data Source={dbPath}"));

            // Memory cache (used for config caching in services)
            services.AddMemoryCache();

            // HTTP clients for services
            services.AddHttpClient<IJellyfinService, JellyfinService>();
            services.AddHttpClient<IJellyseerrService, JellyseerrService>();
            services.AddHttpClient<ISonarrService, SonarrService>();
            services.AddHttpClient<IRadarrService, RadarrService>();
            services.AddScoped<IDownloadClientService, DownloadClientService>();

            // Services
            services.AddScoped<IFileManagementService, FileManagementService>();
            services.AddScoped<ITransitionService, TransitionService>();

            // Controllers
            services.AddControllers()
                .AddJsonOptions(options =>
                {
                    options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
                });

            // SignalR
            services.AddSignalR();

            // Swagger
            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo
                {
                    Title = "Storarr API",
                    Version = "v1",
                    Description = "Tiered Media Storage Manager API"
                });
            });

            // CORS: AllowAnyOrigin is intentional for home-server use where Storarr runs on a
            // trusted LAN. If authentication is added in the future, this policy must be tightened
            // to restrict allowed origins accordingly.
            services.AddCors(options =>
            {
                options.AddPolicy("AllowAll", policy =>
                {
                    policy.AllowAnyOrigin()
                          .AllowAnyMethod()
                          .AllowAnyHeader();
                });
            });

            // Background services
            services.AddHostedService<WatchStatusMonitor>();
            services.AddHostedService<TransitionScheduler>();
            services.AddHostedService<LibraryScanner>();
            services.AddHostedService<DownloadMonitor>();

            // SPA static files
            services.AddSpaStaticFiles(configuration =>
            {
                configuration.RootPath = "wwwroot";
            });
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                app.UseSwagger();
                app.UseSwaggerUI(c =>
                {
                    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Storarr API v1");
                });
            }

            app.UseCors("AllowAll");

            app.UseRouting();

            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
                endpoints.MapHub<NotificationHub>("/hubs/notifications");
            });

            // Serve static files
            app.UseStaticFiles();
            app.UseSpa(spa =>
            {
                spa.Options.SourcePath = "wwwroot";

                if (env.IsDevelopment())
                {
                    spa.UseProxyToSpaDevelopmentServer("http://localhost:3000");
                }
            });
        }
    }
}
