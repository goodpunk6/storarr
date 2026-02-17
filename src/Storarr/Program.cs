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
                dbContext.Database.EnsureCreated();
            }

            host.Run();
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

            // CORS
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
