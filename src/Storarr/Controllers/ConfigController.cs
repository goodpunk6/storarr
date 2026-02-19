using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Storarr.Data;
using Storarr.DTOs;
using Storarr.Models;
using Storarr.Services;

namespace Storarr.Controllers
{
    [ApiController]
    [Route("api/v1/[controller]")]
    public class ConfigController : ControllerBase
    {
        private readonly StorarrDbContext _dbContext;
        private readonly IJellyfinService _jellyfinService;
        private readonly IJellyseerrService _jellyseerrService;
        private readonly ISonarrService _sonarrService;
        private readonly IRadarrService _radarrService;
        private readonly IDownloadClientService _downloadClientService;
        private readonly ILogger<ConfigController> _logger;

        public ConfigController(
            StorarrDbContext dbContext,
            IJellyfinService jellyfinService,
            IJellyseerrService jellyseerrService,
            ISonarrService sonarrService,
            IRadarrService radarrService,
            IDownloadClientService downloadClientService,
            ILogger<ConfigController> logger)
        {
            _dbContext = dbContext;
            _jellyfinService = jellyfinService;
            _jellyseerrService = jellyseerrService;
            _sonarrService = sonarrService;
            _radarrService = radarrService;
            _downloadClientService = downloadClientService;
            _logger = logger;
        }

        [HttpGet]
        public async Task<ActionResult<ConfigDto>> GetConfig()
        {
            _logger.LogDebug("[ConfigController] GetConfig called");

            try
            {
                var config = await _dbContext.Configs.FindAsync(Config.SingletonId);
                if (config == null)
                {
                    _logger.LogWarning("[ConfigController] Config not found in database");
                    return NotFound();
                }

                _logger.LogDebug("[ConfigController] Returning config - FirstRunComplete: {FirstRun}, LibraryMode: {Mode}",
                    config.FirstRunComplete, config.LibraryMode);

                return Ok(MapToDto(config));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ConfigController] Error in GetConfig");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPut]
        public async Task<ActionResult<ConfigDto>> UpdateConfig([FromBody] UpdateConfigDto dto)
        {
            _logger.LogDebug("[ConfigController] UpdateConfig called");

            try
            {
                var config = await _dbContext.Configs.FindAsync(Config.SingletonId);
                if (config == null)
                {
                    _logger.LogWarning("[ConfigController] Config not found for update");
                    return NotFound();
                }

                // First-run setup
                if (dto.FirstRunComplete.HasValue)
                {
                    _logger.LogDebug("[ConfigController] Setting FirstRunComplete to {Value}", dto.FirstRunComplete.Value);
                    config.FirstRunComplete = dto.FirstRunComplete.Value;
                }

                if (!string.IsNullOrEmpty(dto.LibraryMode) && Enum.TryParse<LibraryMode>(dto.LibraryMode, out var libraryMode))
                {
                    _logger.LogDebug("[ConfigController] Setting LibraryMode to {Mode}", libraryMode);
                    config.LibraryMode = libraryMode;
                }

                // Transition thresholds
                if (dto.SymlinkToMkvValue.HasValue)
                {
                    _logger.LogDebug("[ConfigController] Setting SymlinkToMkvValue to {Value}", dto.SymlinkToMkvValue.Value);
                    config.SymlinkToMkvValue = dto.SymlinkToMkvValue.Value;
                }
                if (!string.IsNullOrEmpty(dto.SymlinkToMkvUnit) && Enum.TryParse<TimeUnit>(dto.SymlinkToMkvUnit, out var symlinkUnit))
                    config.SymlinkToMkvUnit = symlinkUnit;

                if (dto.MkvToSymlinkValue.HasValue)
                    config.MkvToSymlinkValue = dto.MkvToSymlinkValue.Value;
                if (!string.IsNullOrEmpty(dto.MkvToSymlinkUnit) && Enum.TryParse<TimeUnit>(dto.MkvToSymlinkUnit, out var mkvUnit))
                    config.MkvToSymlinkUnit = mkvUnit;

                if (dto.MediaLibraryPath != null)
                    config.MediaLibraryPath = dto.MediaLibraryPath.Trim();

                // Media services - trim all URLs and API keys
                if (dto.JellyfinUrl != null)
                    config.JellyfinUrl = dto.JellyfinUrl.Trim();
                if (!string.IsNullOrEmpty(dto.JellyfinApiKey) && !IsMasked(dto.JellyfinApiKey))
                    config.JellyfinApiKey = dto.JellyfinApiKey.Trim();

                if (dto.JellyseerrUrl != null)
                    config.JellyseerrUrl = dto.JellyseerrUrl.Trim();
                if (!string.IsNullOrEmpty(dto.JellyseerrApiKey) && !IsMasked(dto.JellyseerrApiKey))
                    config.JellyseerrApiKey = dto.JellyseerrApiKey.Trim();

                if (dto.SonarrUrl != null)
                    config.SonarrUrl = dto.SonarrUrl.Trim();
                if (!string.IsNullOrEmpty(dto.SonarrApiKey) && !IsMasked(dto.SonarrApiKey))
                    config.SonarrApiKey = dto.SonarrApiKey.Trim();

                if (dto.RadarrUrl != null)
                    config.RadarrUrl = dto.RadarrUrl.Trim();
                if (!string.IsNullOrEmpty(dto.RadarrApiKey) && !IsMasked(dto.RadarrApiKey))
                    config.RadarrApiKey = dto.RadarrApiKey.Trim();

                // Download Client 1
                if (dto.DownloadClient1Enabled.HasValue)
                    config.DownloadClient1Enabled = dto.DownloadClient1Enabled.Value;
                if (!string.IsNullOrEmpty(dto.DownloadClient1Type) && Enum.TryParse<DownloadClientType>(dto.DownloadClient1Type, out var dc1Type))
                    config.DownloadClient1Type = dc1Type;
                if (dto.DownloadClient1Url != null)
                    config.DownloadClient1Url = dto.DownloadClient1Url.Trim();
                if (dto.DownloadClient1Username != null)
                    config.DownloadClient1Username = dto.DownloadClient1Username.Trim();
                if (!string.IsNullOrEmpty(dto.DownloadClient1Password) && !IsMasked(dto.DownloadClient1Password))
                    config.DownloadClient1Password = dto.DownloadClient1Password.Trim();
                if (!string.IsNullOrEmpty(dto.DownloadClient1ApiKey) && !IsMasked(dto.DownloadClient1ApiKey))
                    config.DownloadClient1ApiKey = dto.DownloadClient1ApiKey.Trim();

                // Download Client 2
                if (dto.DownloadClient2Enabled.HasValue)
                    config.DownloadClient2Enabled = dto.DownloadClient2Enabled.Value;
                if (!string.IsNullOrEmpty(dto.DownloadClient2Type) && Enum.TryParse<DownloadClientType>(dto.DownloadClient2Type, out var dc2Type))
                    config.DownloadClient2Type = dc2Type;
                if (dto.DownloadClient2Url != null)
                    config.DownloadClient2Url = dto.DownloadClient2Url.Trim();
                if (dto.DownloadClient2Username != null)
                    config.DownloadClient2Username = dto.DownloadClient2Username.Trim();
                if (!string.IsNullOrEmpty(dto.DownloadClient2Password) && !IsMasked(dto.DownloadClient2Password))
                    config.DownloadClient2Password = dto.DownloadClient2Password.Trim();
                if (!string.IsNullOrEmpty(dto.DownloadClient2ApiKey) && !IsMasked(dto.DownloadClient2ApiKey))
                    config.DownloadClient2ApiKey = dto.DownloadClient2ApiKey.Trim();

                // Download Client 3
                if (dto.DownloadClient3Enabled.HasValue)
                    config.DownloadClient3Enabled = dto.DownloadClient3Enabled.Value;
                if (!string.IsNullOrEmpty(dto.DownloadClient3Type) && Enum.TryParse<DownloadClientType>(dto.DownloadClient3Type, out var dc3Type))
                    config.DownloadClient3Type = dc3Type;
                if (dto.DownloadClient3Url != null)
                    config.DownloadClient3Url = dto.DownloadClient3Url.Trim();
                if (!string.IsNullOrEmpty(dto.DownloadClient3ApiKey) && !IsMasked(dto.DownloadClient3ApiKey))
                    config.DownloadClient3ApiKey = dto.DownloadClient3ApiKey.Trim();

                await _dbContext.SaveChangesAsync();
                _logger.LogInformation("[ConfigController] Config saved successfully");

                return Ok(MapToDto(config));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ConfigController] Error in UpdateConfig");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPost("firstrun")]
        public async Task<ActionResult<ConfigDto>> CompleteFirstRun([FromBody] FirstRunSetupDto dto)
        {
            _logger.LogInformation("[ConfigController] CompleteFirstRun called with LibraryMode: {Mode}", dto.LibraryMode);

            try
            {
                var config = await _dbContext.Configs.FindAsync(Config.SingletonId);
                if (config == null)
                {
                    _logger.LogWarning("[ConfigController] Config not found for first run");
                    return NotFound();
                }

                if (config.FirstRunComplete)
                {
                    _logger.LogWarning("[ConfigController] First run already completed");
                    return BadRequest("First run setup already completed");
                }

                if (!string.IsNullOrEmpty(dto.LibraryMode) && Enum.TryParse<LibraryMode>(dto.LibraryMode, out var libraryMode))
                    config.LibraryMode = libraryMode;

                // Trim all values to prevent whitespace issues
                if (!string.IsNullOrEmpty(dto.JellyfinUrl))
                    config.JellyfinUrl = dto.JellyfinUrl.Trim();
                if (!string.IsNullOrEmpty(dto.JellyfinApiKey))
                    config.JellyfinApiKey = dto.JellyfinApiKey.Trim();

                if (!string.IsNullOrEmpty(dto.JellyseerrUrl))
                    config.JellyseerrUrl = dto.JellyseerrUrl.Trim();
                if (!string.IsNullOrEmpty(dto.JellyseerrApiKey))
                    config.JellyseerrApiKey = dto.JellyseerrApiKey.Trim();

                if (!string.IsNullOrEmpty(dto.SonarrUrl))
                    config.SonarrUrl = dto.SonarrUrl.Trim();
                if (!string.IsNullOrEmpty(dto.SonarrApiKey))
                    config.SonarrApiKey = dto.SonarrApiKey.Trim();

                if (!string.IsNullOrEmpty(dto.RadarrUrl))
                    config.RadarrUrl = dto.RadarrUrl.Trim();
                if (!string.IsNullOrEmpty(dto.RadarrApiKey))
                    config.RadarrApiKey = dto.RadarrApiKey.Trim();

                if (!string.IsNullOrEmpty(dto.MediaLibraryPath))
                    config.MediaLibraryPath = dto.MediaLibraryPath.Trim();

                config.FirstRunComplete = true;
                await _dbContext.SaveChangesAsync();

                _logger.LogInformation("[ConfigController] First run completed successfully");

                return Ok(MapToDto(config));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ConfigController] Error in CompleteFirstRun");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPost("test")]
        public async Task<ActionResult<TestConnectionsResponse>> TestConnections()
        {
            _logger.LogDebug("[ConfigController] TestConnections called");

            try
            {
                var config = await _dbContext.Configs.FindAsync(Config.SingletonId);
                var results = new List<ConnectionTestResult>();

                if (config != null)
                {
                    _logger.LogDebug("[ConfigController] Testing media services...");

                    // Test media services
                    results.Add(await TestService("Jellyfin", async () => { await _jellyfinService.TestConnection(); return ("Connected", true); }));
                    results.Add(await TestService("Jellyseerr", async () => { await _jellyseerrService.TestConnection(); return ("Connected", true); }));
                    results.Add(await TestService("Sonarr", async () => { await _sonarrService.TestConnection(); return ("Connected", true); }));
                    results.Add(await TestService("Radarr", async () => { await _radarrService.TestConnection(); return ("Connected", true); }));

                    // Test download clients - pass appropriate credentials based on client type
                    if (config.DownloadClient1Enabled && !string.IsNullOrEmpty(config.DownloadClient1Url))
                    {
                        _logger.LogDebug("[ConfigController] Testing Download Client 1: {Type} at {Url}",
                            config.DownloadClient1Type, config.DownloadClient1Url);
                        var apiKey = config.DownloadClient1Type == DownloadClientType.Sabnzbd ? config.DownloadClient1ApiKey : null;
                        results.Add(await TestDownloadClient(config.DownloadClient1Type, config.DownloadClient1Url,
                            config.DownloadClient1Username, config.DownloadClient1Password, apiKey));
                    }

                    if (config.DownloadClient2Enabled && !string.IsNullOrEmpty(config.DownloadClient2Url))
                    {
                        _logger.LogDebug("[ConfigController] Testing Download Client 2: {Type} at {Url}",
                            config.DownloadClient2Type, config.DownloadClient2Url);
                        var apiKey = config.DownloadClient2Type == DownloadClientType.Sabnzbd ? config.DownloadClient2ApiKey : null;
                        results.Add(await TestDownloadClient(config.DownloadClient2Type, config.DownloadClient2Url,
                            config.DownloadClient2Username, config.DownloadClient2Password, apiKey));
                    }

                    if (config.DownloadClient3Enabled && !string.IsNullOrEmpty(config.DownloadClient3Url))
                    {
                        _logger.LogDebug("[ConfigController] Testing Download Client 3: {Type} at {Url}",
                            config.DownloadClient3Type, config.DownloadClient3Url);
                        var apiKey = config.DownloadClient3Type == DownloadClientType.Sabnzbd ? config.DownloadClient3ApiKey : null;
                        results.Add(await TestDownloadClient(config.DownloadClient3Type, config.DownloadClient3Url,
                            null, null, apiKey));
                    }
                }

                _logger.LogDebug("[ConfigController] Connection tests completed: {Count} results", results.Count);
                return Ok(new TestConnectionsResponse { Results = results });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ConfigController] Error in TestConnections");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        private async Task<ConnectionTestResult> TestDownloadClient(DownloadClientType type, string url, string? username, string? password, string? apiKey)
        {
            var name = type switch
            {
                DownloadClientType.QBittorrent => "qBittorrent",
                DownloadClientType.Transmission => "Transmission",
                DownloadClientType.Sabnzbd => "SABnzbd",
                _ => type.ToString()
            };

            try
            {
                var success = await _downloadClientService.TestConnection(type, url, username, password, apiKey);
                _logger.LogDebug("[ConfigController] Download client {Name} test result: {Success}", name, success);
                return new ConnectionTestResult { Service = name, Success = success };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[ConfigController] Download client {Name} test failed", name);
                return new ConnectionTestResult { Service = name, Success = false, Error = ex.Message };
            }
        }

        private async Task<ConnectionTestResult> TestService(string serviceName, Func<Task<(string version, bool success)>> testFunc)
        {
            try
            {
                var (version, success) = await testFunc();
                _logger.LogDebug("[ConfigController] Service {Service} test result: {Success}", serviceName, success);
                return new ConnectionTestResult { Service = serviceName, Success = true, Version = version };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[ConfigController] Service {Service} test failed", serviceName);
                return new ConnectionTestResult { Service = serviceName, Success = false, Error = ex.Message };
            }
        }

        private ConfigDto MapToDto(Config config)
        {
            return new ConfigDto
            {
                FirstRunComplete = config.FirstRunComplete,
                LibraryMode = config.LibraryMode.ToString(),
                SymlinkToMkvValue = config.SymlinkToMkvValue,
                SymlinkToMkvUnit = config.SymlinkToMkvUnit.ToString(),
                MkvToSymlinkValue = config.MkvToSymlinkValue,
                MkvToSymlinkUnit = config.MkvToSymlinkUnit.ToString(),
                MediaLibraryPath = config.MediaLibraryPath,
                JellyfinUrl = config.JellyfinUrl,
                JellyfinApiKey = MaskApiKey(config.JellyfinApiKey),
                JellyseerrUrl = config.JellyseerrUrl,
                JellyseerrApiKey = MaskApiKey(config.JellyseerrApiKey),
                SonarrUrl = config.SonarrUrl,
                SonarrApiKey = MaskApiKey(config.SonarrApiKey),
                RadarrUrl = config.RadarrUrl,
                RadarrApiKey = MaskApiKey(config.RadarrApiKey),
                DownloadClient1Enabled = config.DownloadClient1Enabled,
                DownloadClient1Type = config.DownloadClient1Type.ToString(),
                DownloadClient1Url = config.DownloadClient1Url,
                DownloadClient1Username = config.DownloadClient1Username,
                DownloadClient1Password = MaskPassword(config.DownloadClient1Password),
                DownloadClient1ApiKey = MaskApiKey(config.DownloadClient1ApiKey),
                DownloadClient2Enabled = config.DownloadClient2Enabled,
                DownloadClient2Type = config.DownloadClient2Type.ToString(),
                DownloadClient2Url = config.DownloadClient2Url,
                DownloadClient2Username = config.DownloadClient2Username,
                DownloadClient2Password = MaskPassword(config.DownloadClient2Password),
                DownloadClient2ApiKey = MaskApiKey(config.DownloadClient2ApiKey),
                DownloadClient3Enabled = config.DownloadClient3Enabled,
                DownloadClient3Type = config.DownloadClient3Type.ToString(),
                DownloadClient3Url = config.DownloadClient3Url,
                DownloadClient3ApiKey = MaskApiKey(config.DownloadClient3ApiKey)
            };
        }

        private static string? MaskApiKey(string? apiKey)
        {
            if (string.IsNullOrEmpty(apiKey))
                return apiKey;
            return MaskedSentinel;
        }

        private static string? MaskPassword(string? password)
        {
            if (string.IsNullOrEmpty(password))
                return password;
            return MaskedSentinel;
        }

        public const string MaskedSentinel = "__MASKED__";

        private static bool IsMasked(string value)
        {
            return value == MaskedSentinel;
        }
    }
}
