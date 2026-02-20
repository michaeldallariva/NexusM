using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using NexusM.Data;
using NexusM.Middleware;
using NexusM.Services;
using Serilog;

namespace NexusM;

public class Program
{
    public static async Task Main(string[] args)
    {
        // ─── Set working directory to exe location ─────────────────
        // When launched from registry (Run on Startup), the working directory
        // is typically C:\Windows\System32. We must set it to the exe directory
        // so ASP.NET can find wwwroot, config files, and other content.
        // For single-file publish, AppContext.BaseDirectory points to a temp
        // extraction folder. Use the actual exe directory instead.
        var exeDir = AppContext.BaseDirectory;
        var mainModule = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
        if (mainModule != null)
        {
            var exeFolder = Path.GetDirectoryName(mainModule);
            if (!string.IsNullOrEmpty(exeFolder))
                exeDir = exeFolder + Path.DirectorySeparatorChar;
        }
        Directory.SetCurrentDirectory(exeDir);

        // ─── Load config early to decide console visibility ──────────
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = exeDir,
            WebRootPath = Path.Combine(exeDir, "wwwroot")
        });
        builder.Services.AddSingleton<ConfigService>();
#pragma warning disable ASP0000 // ConfigService must be resolved early to configure Kestrel and console visibility
        var tempProvider = builder.Services.BuildServiceProvider();
#pragma warning restore ASP0000
        var configService = tempProvider.GetRequiredService<ConfigService>();
        var config = configService.Config;

        // ─── Show console window if configured (app is WinExe by default) ──
        var showConsole = config.Server.ShowConsole;
        if (showConsole && OperatingSystem.IsWindows())
        {
            AllocConsole();
        }

        // ─── Bootstrap logger for startup ────────────────────────────
        // Always write to a file so startup crashes are never silently lost
        var bootstrapLogPath = Path.Combine(exeDir, "logs", "nexusm-bootstrap.log");
        var bootstrapLogDir = Path.GetDirectoryName(bootstrapLogPath);
        if (!string.IsNullOrEmpty(bootstrapLogDir) && !Directory.Exists(bootstrapLogDir))
            Directory.CreateDirectory(bootstrapLogDir);

        var bootstrapConfig = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(bootstrapLogPath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 3,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}");
        if (showConsole)
            bootstrapConfig.WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}");
        Log.Logger = bootstrapConfig.CreateBootstrapLogger();

        try
        {
            Log.Information("═══════════════════════════════════════════════════════════");
            Log.Information("  NexusM - Music Library Manager");
            Log.Information("  Starting up...");
            Log.Information("═══════════════════════════════════════════════════════════");

            // ─── Configure Serilog from .conf settings ───────────────
            var logLevel = config.Logging.LogLevel.ToLowerInvariant() switch
            {
                "debug" => Serilog.Events.LogEventLevel.Debug,
                "warning" => Serilog.Events.LogEventLevel.Warning,
                "error" => Serilog.Events.LogEventLevel.Error,
                _ => Serilog.Events.LogEventLevel.Information
            };

            // Resolve log file path relative to config file location
            var configDir = Path.GetDirectoryName(configService.ConfigFilePath) ?? exeDir;
            var logFilePath = Path.IsPathRooted(config.Logging.LogFile)
                ? config.Logging.LogFile
                : Path.Combine(configDir, config.Logging.LogFile);
            var logDir = Path.GetDirectoryName(logFilePath);
            if (!string.IsNullOrEmpty(logDir) && !Directory.Exists(logDir))
            {
                Directory.CreateDirectory(logDir);
                Log.Information("Created log directory: {Path}", logDir);
            }

            builder.Host.UseSerilog((context, services, loggerConfig) =>
            {
                loggerConfig
                    .MinimumLevel.Is(logLevel);

                if (showConsole)
                    loggerConfig.WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}");

                loggerConfig.WriteTo.File(
                        logFilePath,
                        rollingInterval: RollingInterval.Day,
                        fileSizeLimitBytes: config.Logging.MaxLogSizeMB * 1024 * 1024,
                        retainedFileCountLimit: 10,
                        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}");
            });

            // ─── Kill any stale NexusM instances holding the port ───────
            KillStaleInstances(config.Server.ServerPort);

            // ─── Configure Kestrel web server ────────────────────────
            builder.WebHost.ConfigureKestrel(kestrel =>
            {
                kestrel.ListenAnyIP(config.Server.ServerPort);
                kestrel.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(config.Server.RequestTimeout);
                kestrel.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(120);
                // Allow large file streaming
                kestrel.Limits.MaxRequestBodySize = 100 * 1024 * 1024; // 100MB

                Log.Information("Kestrel configured: {Host}:{Port}", config.Server.ServerHost, config.Server.ServerPort);
            });

            // ─── Ensure Windows Firewall rule exists for the configured port ──
            EnsureFirewallRule(config.Server.ServerPort);

            // ─── Database (SQLite via EF Core) ───────────────────────
            var dbDir = Path.GetDirectoryName(config.Database.DatabasePath);
            if (!string.IsNullOrEmpty(dbDir) && !Directory.Exists(dbDir))
                Directory.CreateDirectory(dbDir);

            builder.Services.AddDbContext<MusicDbContext>(options =>
            {
                options.UseSqlite($"Data Source={config.Database.DatabasePath}");
            });

            // ─── Pictures Database (separate SQLite) ──────────────
            var picDbDir = Path.GetDirectoryName(config.Database.PicturesDatabasePath);
            if (!string.IsNullOrEmpty(picDbDir) && !Directory.Exists(picDbDir))
                Directory.CreateDirectory(picDbDir);

            builder.Services.AddDbContext<PicturesDbContext>(options =>
            {
                options.UseSqlite($"Data Source={config.Database.PicturesDatabasePath}");
            });

            // ─── eBooks Database (separate SQLite) ──────────────
            var ebookDbDir = Path.GetDirectoryName(config.Database.EBooksDatabasePath);
            if (!string.IsNullOrEmpty(ebookDbDir) && !Directory.Exists(ebookDbDir))
                Directory.CreateDirectory(ebookDbDir);

            builder.Services.AddDbContext<EBooksDbContext>(options =>
            {
                options.UseSqlite($"Data Source={config.Database.EBooksDatabasePath}");
            });

            // ─── Music Videos Database (separate SQLite) ────────────
            var mvDbDir = Path.GetDirectoryName(config.Database.MusicVideosDatabasePath);
            if (!string.IsNullOrEmpty(mvDbDir) && !Directory.Exists(mvDbDir))
                Directory.CreateDirectory(mvDbDir);

            builder.Services.AddDbContext<MusicVideosDbContext>(options =>
            {
                options.UseSqlite($"Data Source={config.Database.MusicVideosDatabasePath}");
            });

            // ─── Videos (Movies/TV) Database (separate SQLite) ───────
            var videoDbDir = Path.GetDirectoryName(config.Database.VideosDatabasePath);
            if (!string.IsNullOrEmpty(videoDbDir) && !Directory.Exists(videoDbDir))
                Directory.CreateDirectory(videoDbDir);

            builder.Services.AddDbContext<VideosDbContext>(options =>
            {
                options.UseSqlite($"Data Source={config.Database.VideosDatabasePath}");
            });

            // ─── TV Channels Database (separate SQLite) ────────────
            var tvDbDir = Path.GetDirectoryName(config.Database.TvChannelsDatabasePath);
            if (!string.IsNullOrEmpty(tvDbDir) && !Directory.Exists(tvDbDir))
                Directory.CreateDirectory(tvDbDir);

            builder.Services.AddDbContext<TvChannelsDbContext>(options =>
            {
                options.UseSqlite($"Data Source={config.Database.TvChannelsDatabasePath}");
            });

            // ─── Users Database (separate SQLite for authentication) ──
            var usersDbDir = Path.GetDirectoryName(config.Database.UsersDatabasePath);
            if (!string.IsNullOrEmpty(usersDbDir) && !Directory.Exists(usersDbDir))
                Directory.CreateDirectory(usersDbDir);

            builder.Services.AddDbContext<UsersDbContext>(options =>
            {
                options.UseSqlite($"Data Source={config.Database.UsersDatabasePath}");
            });

            // ─── Shares Database (network share credentials) ─────────
            var sharesDbDir = Path.GetDirectoryName(config.Database.SharesDatabasePath);
            if (!string.IsNullOrEmpty(sharesDbDir) && !Directory.Exists(sharesDbDir))
                Directory.CreateDirectory(sharesDbDir);

            builder.Services.AddDbContext<SharesDbContext>(options =>
            {
                options.UseSqlite($"Data Source={config.Database.SharesDatabasePath}");
            });

            // ─── Actors Database (separate SQLite) ──────────────────
            var actorsDbDir = Path.GetDirectoryName(config.Database.ActorsDatabasePath);
            if (!string.IsNullOrEmpty(actorsDbDir) && !Directory.Exists(actorsDbDir))
                Directory.CreateDirectory(actorsDbDir);

            builder.Services.AddDbContext<ActorsDbContext>(options =>
            {
                options.UseSqlite($"Data Source={config.Database.ActorsDatabasePath}");
            });

            // ─── Ratings Database ──────────────────────────────────────
            var ratingsDbDir = Path.GetDirectoryName(config.Database.RatingsDatabasePath);
            if (!string.IsNullOrEmpty(ratingsDbDir) && !Directory.Exists(ratingsDbDir))
                Directory.CreateDirectory(ratingsDbDir);
            builder.Services.AddDbContext<RatingsDbContext>(options =>
                options.UseSqlite($"Data Source={config.Database.RatingsDatabasePath}"));

            // ─── Podcasts Database ─────────────────────────────────────
            var podDbDir = Path.GetDirectoryName(config.Database.PodcastsDatabasePath);
            if (!string.IsNullOrEmpty(podDbDir) && !Directory.Exists(podDbDir))
                Directory.CreateDirectory(podDbDir);
            builder.Services.AddDbContext<PodcastsDbContext>(options =>
                options.UseSqlite($"Data Source={config.Database.PodcastsDatabasePath}"));

            // ─── Services ────────────────────────────────────────────
            builder.Services.AddSingleton<ShareCredentialService>();
            builder.Services.AddSingleton<LibraryScannerService>();
            builder.Services.AddSingleton<PictureScannerService>();
            builder.Services.AddSingleton<EBookScannerService>();
            builder.Services.AddSingleton<FFmpegService>();
            builder.Services.AddSingleton<GpuDetectionService>();
            builder.Services.AddSingleton<TranscodingService>();
            builder.Services.AddSingleton<MusicVideoScannerService>();
            builder.Services.AddSingleton<VideoScannerService>();
            builder.Services.AddSingleton<MetadataService>();
            builder.Services.AddSingleton<PinSecurityService>();
            builder.Services.AddSingleton<RadioService>();
            builder.Services.AddSingleton<TvChannelService>();
            builder.Services.AddSingleton<PodcastService>();
            builder.Services.AddHostedService<PodcastRefreshService>();
            builder.Services.AddSingleton<UserFavouritesService>();
            builder.Services.AddControllers();

            // ─── Cookie Authentication ─────────────────────────────────
            builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
                .AddCookie(options =>
                {
                    options.ExpireTimeSpan = TimeSpan.FromSeconds(config.Server.SessionTimeout);
                    options.SlidingExpiration = true;
                    options.Cookie.Name = "NexusM.Auth";
                    options.Cookie.HttpOnly = true;
                    options.Cookie.SameSite = SameSiteMode.Lax;
                    // API-friendly: return 401/403 instead of redirecting to login page
                    options.Events = new CookieAuthenticationEvents
                    {
                        OnRedirectToLogin = context =>
                        {
                            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                            return Task.CompletedTask;
                        },
                        OnRedirectToAccessDenied = context =>
                        {
                            context.Response.StatusCode = StatusCodes.Status403Forbidden;
                            return Task.CompletedTask;
                        }
                    };
                });
            builder.Services.AddAuthorization();

            // ─── CORS (for potential mobile app / separate frontend) ─
            builder.Services.AddCors(options =>
            {
                options.AddDefaultPolicy(policy =>
                {
                    policy.AllowAnyOrigin()
                          .AllowAnyMethod()
                          .AllowAnyHeader();
                });
            });

            var app = builder.Build();

            // ─── Initialize Database ─────────────────────────────────
            // Helper: add a column only if it doesn't already exist (avoids ERR-level log spam)
            static async Task AddColumnIfMissing(Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade database,
                string table, string column, string columnDef)
            {
                var conn = database.GetDbConnection();
                await conn.OpenAsync();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = $"PRAGMA table_info({table})";
                var exists = false;
                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                        { exists = true; break; }
                    }
                }
                if (!exists)
                {
                    using var alterCmd = conn.CreateCommand();
                    alterCmd.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {columnDef}";
                    await alterCmd.ExecuteNonQueryAsync();
                    Log.Information("Added {Column} column to {Table} table", column, table);
                }
            }

            using (var scope = app.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<MusicDbContext>();
                await db.Database.EnsureCreatedAsync();
                // Deduplicate and enforce case-insensitive unique indices (idempotent — safe on every startup)
                await db.Database.ExecuteSqlRawAsync(
                    "DELETE FROM Tracks WHERE Id NOT IN (SELECT MIN(Id) FROM Tracks GROUP BY LOWER(FilePath))");
                await db.Database.ExecuteSqlRawAsync(
                    "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_Tracks_FilePath_NC\" ON \"Tracks\" (LOWER(\"FilePath\"))");
                await db.Database.ExecuteSqlRawAsync(
                    "DELETE FROM Albums WHERE Id NOT IN (SELECT MIN(Id) FROM Albums GROUP BY LOWER(Name), LOWER(Artist))");
                await db.Database.ExecuteSqlRawAsync(
                    "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_Albums_Name_Artist_NC\" ON \"Albums\" (LOWER(\"Name\"), LOWER(\"Artist\"))");
                await db.Database.ExecuteSqlRawAsync(
                    "DELETE FROM Artists WHERE Id NOT IN (SELECT MIN(Id) FROM Artists GROUP BY LOWER(Name))");
                await db.Database.ExecuteSqlRawAsync(
                    "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_Artists_Name_NC\" ON \"Artists\" (LOWER(\"Name\"))");
                Log.Information("Database initialized at: {Path}", config.Database.DatabasePath);

                var picDb = scope.ServiceProvider.GetRequiredService<PicturesDbContext>();
                await picDb.Database.EnsureCreatedAsync();
                await picDb.Database.ExecuteSqlRawAsync(
                    "DELETE FROM Pictures WHERE Id NOT IN (SELECT MIN(Id) FROM Pictures GROUP BY LOWER(FilePath))");
                await picDb.Database.ExecuteSqlRawAsync(
                    "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_Pictures_FilePath_NC\" ON \"Pictures\" (LOWER(\"FilePath\"))");
                Log.Information("Pictures database initialized at: {Path}", config.Database.PicturesDatabasePath);

                var ebookDb = scope.ServiceProvider.GetRequiredService<EBooksDbContext>();
                await ebookDb.Database.EnsureCreatedAsync();
                await AddColumnIfMissing(ebookDb.Database, "EBooks", "CoverImage", "TEXT NULL");
                // Remove duplicate FilePath rows (keep lowest Id), then enforce case-insensitive uniqueness.
                // This is safe to run every startup — both statements are no-ops when no duplicates exist.
                await ebookDb.Database.ExecuteSqlRawAsync(
                    "DELETE FROM EBooks WHERE Id NOT IN (SELECT MIN(Id) FROM EBooks GROUP BY LOWER(FilePath))");
                await ebookDb.Database.ExecuteSqlRawAsync(
                    "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_EBooks_FilePath_NC\" ON \"EBooks\" (LOWER(\"FilePath\"))");
                Log.Information("eBooks database initialized at: {Path}", config.Database.EBooksDatabasePath);

                var mvDb = scope.ServiceProvider.GetRequiredService<MusicVideosDbContext>();
                await mvDb.Database.EnsureCreatedAsync();
                await AddColumnIfMissing(mvDb.Database, "MusicVideos", "IsFavourite", "INTEGER NOT NULL DEFAULT 0");
                await AddColumnIfMissing(mvDb.Database, "MusicVideos", "PlayCount", "INTEGER NOT NULL DEFAULT 0");
                await mvDb.Database.ExecuteSqlRawAsync(
                    "DELETE FROM MusicVideos WHERE Id NOT IN (SELECT MIN(Id) FROM MusicVideos GROUP BY LOWER(FilePath))");
                await mvDb.Database.ExecuteSqlRawAsync(
                    "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_MusicVideos_FilePath_NC\" ON \"MusicVideos\" (LOWER(\"FilePath\"))");
                Log.Information("Music Videos database initialized at: {Path}", config.Database.MusicVideosDatabasePath);

                var videoDb = scope.ServiceProvider.GetRequiredService<VideosDbContext>();
                await videoDb.Database.EnsureCreatedAsync();
                await AddColumnIfMissing(videoDb.Database, "Videos", "IsFavourite", "INTEGER NOT NULL DEFAULT 0");
                await AddColumnIfMissing(videoDb.Database, "Videos", "TmdbId", "TEXT NULL");
                await AddColumnIfMissing(videoDb.Database, "Videos", "TvMazeId", "TEXT NULL");
                await AddColumnIfMissing(videoDb.Database, "Videos", "ImdbId", "TEXT NULL");
                await AddColumnIfMissing(videoDb.Database, "Videos", "PosterPath", "TEXT NULL");
                await AddColumnIfMissing(videoDb.Database, "Videos", "BackdropPath", "TEXT NULL");
                await AddColumnIfMissing(videoDb.Database, "Videos", "CastJson", "TEXT NULL");
                await AddColumnIfMissing(videoDb.Database, "Videos", "MetadataFetched", "INTEGER NOT NULL DEFAULT 0");
                await AddColumnIfMissing(videoDb.Database, "Videos", "ManuallyEdited", "INTEGER NOT NULL DEFAULT 0");
                await videoDb.Database.ExecuteSqlRawAsync(
                    "DELETE FROM Videos WHERE Id NOT IN (SELECT MIN(Id) FROM Videos GROUP BY LOWER(FilePath))");
                await videoDb.Database.ExecuteSqlRawAsync(
                    "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_Videos_FilePath_NC\" ON \"Videos\" (LOWER(\"FilePath\"))");
                Log.Information("Videos database initialized at: {Path}", config.Database.VideosDatabasePath);

                var usersDb = scope.ServiceProvider.GetRequiredService<UsersDbContext>();
                await usersDb.Database.EnsureCreatedAsync();
                await AddColumnIfMissing(usersDb.Database, "Users", "PinHash", "TEXT NOT NULL DEFAULT ''");
                await AddColumnIfMissing(usersDb.Database, "Users", "PinSalt", "TEXT NOT NULL DEFAULT ''");
                await AddColumnIfMissing(usersDb.Database, "Users", "DisplayName", "TEXT NULL");
                Log.Information("Users database initialized at: {Path}", config.Database.UsersDatabasePath);

                var tvDb = scope.ServiceProvider.GetRequiredService<TvChannelsDbContext>();
                await tvDb.Database.EnsureCreatedAsync();
                Log.Information("TV Channels database initialized at: {Path}", config.Database.TvChannelsDatabasePath);

                var sharesDb = scope.ServiceProvider.GetRequiredService<SharesDbContext>();
                await sharesDb.Database.EnsureCreatedAsync();
                Log.Information("Shares database initialized at: {Path}", config.Database.SharesDatabasePath);

                var actorsDb = scope.ServiceProvider.GetRequiredService<ActorsDbContext>();
                await actorsDb.Database.EnsureCreatedAsync();
                Log.Information("Actors database initialized at: {Path}", config.Database.ActorsDatabasePath);

                var ratingsDb = scope.ServiceProvider.GetRequiredService<RatingsDbContext>();
                await ratingsDb.Database.EnsureCreatedAsync();
                Log.Information("Ratings database initialized at: {Path}", config.Database.RatingsDatabasePath);

                var podcastDb = scope.ServiceProvider.GetRequiredService<PodcastsDbContext>();
                await podcastDb.Database.EnsureCreatedAsync();
                Log.Information("Podcasts database initialized at: {Path}", config.Database.PodcastsDatabasePath);
            }

            // ─── Create default admin user if none exist ──────────────
            var pinSecurity = app.Services.GetRequiredService<PinSecurityService>();
            await pinSecurity.EnsureDefaultAdminAsync();

            // ─── Create assets/albumart/ directory ───────────────────
            var albumArtPath = Path.Combine(exeDir, "assets", "albumart");
            if (!Directory.Exists(albumArtPath))
            {
                Directory.CreateDirectory(albumArtPath);
                Log.Information("Created album art cache directory: {Path}", albumArtPath);
            }

            // ─── Create assets/thumbs/ directory ──────────────────
            var thumbsPath = Path.Combine(exeDir, "assets", "thumbs");
            if (!Directory.Exists(thumbsPath))
            {
                Directory.CreateDirectory(thumbsPath);
                Log.Information("Created picture thumbnails directory: {Path}", thumbsPath);
            }

            // ─── Create assets/ebookcovers/ directory ────────────────
            var ebookCoversPath = Path.Combine(exeDir, "assets", "ebookcovers");
            if (!Directory.Exists(ebookCoversPath))
            {
                Directory.CreateDirectory(ebookCoversPath);
                Log.Information("Created eBook covers directory: {Path}", ebookCoversPath);
            }

            // ─── Create assets/mvthumbs/ directory ─────────────────
            var mvThumbsPath = Path.Combine(exeDir, "assets", "mvthumbs");
            if (!Directory.Exists(mvThumbsPath))
            {
                Directory.CreateDirectory(mvThumbsPath);
                Log.Information("Created music video thumbnails directory: {Path}", mvThumbsPath);
            }

            // ─── Create assets/videothumbs/ directory ────────────────
            var videoThumbsPath = Path.Combine(exeDir, "assets", "videothumbs");
            if (!Directory.Exists(videoThumbsPath))
            {
                Directory.CreateDirectory(videoThumbsPath);
                Log.Information("Created video thumbnails directory: {Path}", videoThumbsPath);
            }

            // ─── Create assets/videometa/ directory (metadata images) ──
            var videoMetaPath = Path.Combine(exeDir, "assets", "videometa");
            if (!Directory.Exists(videoMetaPath))
            {
                Directory.CreateDirectory(videoMetaPath);
                Log.Information("Created video metadata images directory: {Path}", videoMetaPath);
            }

            // ─── Create assets/lang/ directory (i18n language files) ──
            var langPath = Path.Combine(exeDir, "assets", "lang");
            if (!Directory.Exists(langPath))
            {
                Directory.CreateDirectory(langPath);
                Log.Information("Created language files directory: {Path}", langPath);
            }

            // ─── Create users/ directory for per-user databases ────
            var usersDbFolder = Path.Combine(exeDir, "users");
            if (!Directory.Exists(usersDbFolder))
            {
                Directory.CreateDirectory(usersDbFolder);
                Log.Information("Created user databases directory: {Path}", usersDbFolder);
            }

            // ─── Create cache/remux/ directory ──────────────────────
            var remuxCachePath = Path.Combine(exeDir, "cache", "remux");
            if (!Directory.Exists(remuxCachePath))
            {
                Directory.CreateDirectory(remuxCachePath);
                Log.Information("Created remux cache directory: {Path}", remuxCachePath);
            }

            // ─── Create assets/tvlogos/ directory ──────────────────
            var tvLogosPath = Path.Combine(exeDir, "assets", "tvlogos");
            if (!Directory.Exists(tvLogosPath))
            {
                Directory.CreateDirectory(tvLogosPath);
                Log.Information("Created TV logos directory: {Path}", tvLogosPath);
            }

            // ─── Create assets/actors/ directory ──────────────────────
            var actorsPath = Path.Combine(exeDir, "assets", "actors");
            if (!Directory.Exists(actorsPath))
            {
                Directory.CreateDirectory(actorsPath);
                Log.Information("Created actors photos directory: {Path}", actorsPath);
            }

            // ─── Create assets/radiologos/ directory ─────────────────
            var radioLogosPath = Path.Combine(exeDir, "assets", "radiologos");
            if (!Directory.Exists(radioLogosPath))
            {
                Directory.CreateDirectory(radioLogosPath);
                Log.Information("Created radio logos directory: {Path}", radioLogosPath);
            }

            // ─── Create assets/podcastart/ directory ──────────────────
            var podcastArtPath = Path.Combine(exeDir, "assets", "podcastart");
            if (!Directory.Exists(podcastArtPath))
            {
                Directory.CreateDirectory(podcastArtPath);
                Log.Information("Created podcast artwork directory: {Path}", podcastArtPath);
            }

            // ─── Ensure tools/ffmpeg/bin/ directory exists ──────────
            var toolsDir = Path.Combine(exeDir, "tools", "ffmpeg", "bin");
            if (!Directory.Exists(toolsDir))
            {
                Directory.CreateDirectory(toolsDir);
                Log.Information("Created tools directory (place ffmpeg here): {Path}", toolsDir);
            }

            // ─── Middleware Pipeline ─────────────────────────────────

            // IP Whitelist - must be first (blocks before any processing)
            app.UseMiddleware<IPWhitelistMiddleware>();

            app.UseCors();
            app.UseDefaultFiles();  // Serves index.html for /
            app.UseStaticFiles();   // Serves wwwroot files (CSS, JS, images)

            // Serve album art from assets/albumart/ as /albumart/{filename}
            var albumArtDir = Path.Combine(exeDir, "assets", "albumart");
            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(albumArtDir),
                RequestPath = "/albumart"
            });

            // Serve picture thumbnails from assets/thumbs/ as /picthumb/{filename}
            var thumbsDir = Path.Combine(exeDir, "assets", "thumbs");
            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(thumbsDir),
                RequestPath = "/picthumb"
            });

            // Serve eBook covers from assets/ebookcovers/ as /ebookcover/{filename}
            var ebookCoversDir = Path.Combine(exeDir, "assets", "ebookcovers");
            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(ebookCoversDir),
                RequestPath = "/ebookcover"
            });

            // Serve music video thumbnails from assets/mvthumbs/ as /mvthumb/{filename}
            var mvThumbsDir = Path.Combine(exeDir, "assets", "mvthumbs");
            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(mvThumbsDir),
                RequestPath = "/mvthumb"
            });

            // Serve video thumbnails from assets/videothumbs/ as /videothumb/{filename}
            var videoThumbsDir = Path.Combine(exeDir, "assets", "videothumbs");
            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(videoThumbsDir),
                RequestPath = "/videothumb"
            });

            // Serve video metadata images from assets/videometa/ as /videometa/{filename}
            var videoMetaDir = Path.Combine(exeDir, "assets", "videometa");
            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(videoMetaDir),
                RequestPath = "/videometa"
            });

            // Serve language files from assets/lang/ as /lang/{code}.json
            var langDir = Path.Combine(exeDir, "assets", "lang");
            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(langDir),
                RequestPath = "/lang"
            });

            // Serve TV logos from assets/tvlogos/ as /tvlogo/{filename}
            var tvLogosDir = Path.Combine(exeDir, "assets", "tvlogos");
            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(tvLogosDir),
                RequestPath = "/tvlogo"
            });

            // Serve radio logos from assets/radiologos/ as /radiologo/{filename}
            var radioLogosDir = Path.Combine(exeDir, "assets", "radiologos");
            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(radioLogosDir),
                RequestPath = "/radiologo"
            });

            // Serve actor photos from assets/actors/ as /actorphoto/{filename}
            var actorsDir = Path.Combine(exeDir, "assets", "actors");
            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(actorsDir),
                RequestPath = "/actorphoto"
            });

            // Serve podcast artwork from assets/podcastart/ as /podcastart/{filename}
            var podcastArtDir = Path.Combine(exeDir, "assets", "podcastart");
            Directory.CreateDirectory(podcastArtDir);
            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(podcastArtDir),
                RequestPath = "/podcastart"
            });

            app.UseAuthentication();

            // Auto-login middleware: when SecurityByPin is disabled, auto-authenticate as admin
            if (!config.Security.SecurityByPin)
            {
                app.Use(async (context, next) =>
                {
                    if (context.User.Identity?.IsAuthenticated != true)
                    {
                        // Auto-login as admin
                        using var scope = context.RequestServices.CreateScope();
                        var usersDb = scope.ServiceProvider.GetRequiredService<UsersDbContext>();
                        var admin = await usersDb.Users
                            .FirstOrDefaultAsync(u => u.Role == "admin" && u.IsActive);

                        if (admin != null)
                        {
                            var claims = new List<Claim>
                            {
                                new(ClaimTypes.NameIdentifier, admin.Id.ToString()),
                                new(ClaimTypes.Name, admin.Username),
                                new(ClaimTypes.Role, admin.Role),
                                new("DisplayName", admin.DisplayName ?? admin.Username)
                            };
                            var identity = new ClaimsIdentity(claims, "AutoLogin");
                            context.User = new ClaimsPrincipal(identity);
                        }
                    }
                    await next();
                });
            }

            app.UseAuthorization();

            app.MapControllers();   // Maps API controller routes

            // ─── Startup Banner ──────────────────────────────────────
            Log.Information("═══════════════════════════════════════════════════════════");
            Log.Information("  NexusM is running!");
            Log.Information("  Version:  1.0.0 (.NET {Framework})", Environment.Version);
            Log.Information("  Platform: {OS}", Environment.OSVersion);
            Log.Information("  Started:  {Time}", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            Log.Information("═══════════════════════════════════════════════════════════");
            Log.Information("  Web UI:   http://localhost:{Port}", config.Server.ServerPort);
            Log.Information("  API:      http://localhost:{Port}/api/stats", config.Server.ServerPort);
            Log.Information("  Config:   {Path}", configService.ConfigFilePath);
            Log.Information("  Log File: {Path}", logFilePath);
            Log.Information("═══════════════════════════════════════════════════════════");
            Log.Information("  Security:");
            var ipWhitelist = config.Security.GetIPWhitelistList();
            Log.Information("    PIN Auth:     {Status}", config.Security.SecurityByPin ? "Enabled" : "Disabled (auto-login as admin)");
            Log.Information("    IP Whitelist: {Status}", ipWhitelist.Count > 0
                ? $"Enabled ({ipWhitelist.Count} IPs: {string.Join(", ", ipWhitelist)})"
                : "Disabled (all IPs allowed)");
            Log.Information("═══════════════════════════════════════════════════════════");
            Log.Information("  Databases:");
            Log.Information("    Music:    {Path}", config.Database.DatabasePath);
            Log.Information("    Pictures: {Path}", config.Database.PicturesDatabasePath);
            Log.Information("    eBooks:   {Path}", config.Database.EBooksDatabasePath);
            Log.Information("    MusicVid: {Path}", config.Database.MusicVideosDatabasePath);
            Log.Information("    Videos:   {Path}", config.Database.VideosDatabasePath);
            Log.Information("    Users:    {Path}", config.Database.UsersDatabasePath);
            Log.Information("    User DBs: {Path}", usersDbFolder);
            Log.Information("    Actors:   {Path}", config.Database.ActorsDatabasePath);
            Log.Information("═══════════════════════════════════════════════════════════");
            var ffmpeg = app.Services.GetRequiredService<FFmpegService>();
            Log.Information("  FFmpeg:   {Status}", ffmpeg.IsAvailable ? ffmpeg.FfmpegPath : "NOT INSTALLED - download from Settings page");
            Log.Information("═══════════════════════════════════════════════════════════");

            // ─── GPU Detection & Hardware Encoder Testing ────────────
            var gpuService = app.Services.GetRequiredService<GpuDetectionService>();
            await gpuService.InitialiseAsync();

            Log.Information("  Transcoding:");
            Log.Information("    Preferred: {Encoder}", config.Transcoding.PreferredEncoder);
            if (gpuService.GpuInfo != null)
            {
                foreach (var gpu in gpuService.GpuInfo.DetectedGPUs)
                    Log.Information("    GPU:       {Name} ({Vendor}, {VRAM}GB, {Encoder})",
                        gpu.Name, gpu.Vendor, gpu.VramGB, gpu.EncoderType.ToUpperInvariant());
            }
            if (gpuService.Capabilities != null)
            {
                var encoder = gpuService.GetOptimalEncoder();
                Log.Information("    Active:    {Name} ({Type})", encoder.Name, encoder.Type);
                Log.Information("    Codec:     {Encoder}, Preset: {Preset}", encoder.Encoder, encoder.Preset);
            }

            // ─── Initialise Transcoding Service ──────────────────────
            var transcodingService = app.Services.GetRequiredService<TranscodingService>();
            transcodingService.Initialise();
            transcodingService.CleanupHLSCache();
            Log.Information("    CPU Limit: {Limit}", config.Transcoding.FFmpegCPULimit > 0
                ? $"{config.Transcoding.FFmpegCPULimit}%"
                : "Unlimited");
            Log.Information("    Remux:     Priority={Priority}, Threads={Threads}",
                config.Transcoding.RemuxPriority,
                config.Transcoding.RemuxThreads > 0 ? config.Transcoding.RemuxThreads.ToString() : "auto");
            Log.Information("    HLS Cache: {Status}", config.Transcoding.HLSCacheEnabled
                ? $"Enabled (max {config.Transcoding.HLSCacheMaxSizeGB}GB, {config.Transcoding.HLSCacheRetentionDays} days)"
                : "Disabled");
            Log.Information("═══════════════════════════════════════════════════════════");
            Log.Information("  Media Shares:");
            var musicFolders = config.Library.GetMusicFolderList();
            if (musicFolders.Count > 0)
                musicFolders.ForEach(f => Log.Information("    Music:        {Path} [{Status}]", f, Directory.Exists(f) ? "ONLINE" : "OFFLINE"));
            else
                Log.Warning("    Music:        Not configured");
            var moviesFolders = config.Library.GetMoviesTVFolderList();
            if (moviesFolders.Count > 0)
                moviesFolders.ForEach(f => Log.Information("    Videos:       {Path} [{Status}]", f, Directory.Exists(f) ? "ONLINE" : "OFFLINE"));
            var pictureFolders = config.Library.GetPicturesFolderList();
            if (pictureFolders.Count > 0)
                pictureFolders.ForEach(f => Log.Information("    Pictures:     {Path} [{Status}]", f, Directory.Exists(f) ? "ONLINE" : "OFFLINE"));
            var ebookFolders = config.Library.GetEBooksFolderList();
            if (ebookFolders.Count > 0)
                ebookFolders.ForEach(f => Log.Information("    eBooks:       {Path} [{Status}]", f, Directory.Exists(f) ? "ONLINE" : "OFFLINE"));
            var mvFolders = config.Library.GetMusicVideosFolderList();
            if (mvFolders.Count > 0)
                mvFolders.ForEach(f => Log.Information("    Music Videos: {Path} [{Status}]", f, Directory.Exists(f) ? "ONLINE" : "OFFLINE"));
            Log.Information("═══════════════════════════════════════════════════════════");
            Log.Information("  Press Ctrl+C to stop the server");
            Log.Information("═══════════════════════════════════════════════════════════");

            // ─── Auto-mount network shares ─────────────────────────────
            var shareService = app.Services.GetRequiredService<ShareCredentialService>();
            _ = shareService.MountAllEnabledAsync();

            // ─── Auto-scan on startup if configured ──────────────────
            if (config.Library.AutoScanOnStartup && config.Library.GetMusicFolderList().Count > 0)
            {
                Log.Information("Auto-scan enabled. Starting library scan...");
                var scanner = app.Services.GetRequiredService<LibraryScannerService>();
                _ = scanner.StartScanAsync();
            }

            // ─── Open browser if configured ──────────────────────────
            if (config.Server.OpenBrowser)
            {
                var url = $"http://localhost:{config.Server.ServerPort}";
                try
                {
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                    Log.Information("  Browser opened: {Url}", url);
                }
                catch (Exception ex)
                {
                    Log.Warning("  Could not open browser: {Message}", ex.Message);
                }
            }

            // ─── Graceful shutdown: stop active transcodes ─────────
            var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
            lifetime.ApplicationStopping.Register(() =>
            {
                Log.Information("Shutting down - stopping active transcodes...");
                transcodingService.StopAllTranscodes();
            });

            // ─── System tray icon (Windows only) ────────────────────
            if (OperatingSystem.IsWindows())
            {
                // Sync "Run on Startup" registry with config
                StartupRegistryHelper.SetRunOnStartup(config.Server.RunOnStartup);

                var trayIcon = new TrayIconService(config.Server.ServerPort, logFilePath, configService.ConfigFilePath, configService, lifetime);
                lifetime.ApplicationStopping.Register(trayIcon.Dispose);
                Log.Information("System tray icon started");
            }
            else if (OperatingSystem.IsLinux())
            {
                // Sync "Run on Startup" systemd service with config
                StartupRegistryHelper.SetRunOnStartup(config.Server.RunOnStartup);
            }

            // ─── Run ────────────────────────────────────────────────
            await app.RunAsync();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Application terminated unexpectedly");
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    // P/Invoke for allocating a console window on Windows (app is WinExe subsystem)
    [DllImport("kernel32.dll")]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static extern bool AllocConsole();

    /// <summary>
    /// Finds and terminates any process listening on the configured port before Kestrel binds.
    /// Port-based (not name-based) so it catches old builds, renamed executables, etc.
    /// </summary>
    private static void KillStaleInstances(int port)
    {
        var currentPid = Environment.ProcessId;

        try
        {
            var stalePids = GetListeningPidsOnPort(port)
                .Where(pid => pid != currentPid && pid != 0)
                .Distinct()
                .ToList();

            if (stalePids.Count == 0) return;

            Log.Information("Found {Count} stale process(es) holding port {Port} - cleaning up...", stalePids.Count, port);

            foreach (var pid in stalePids)
            {
                try
                {
                    using var proc = System.Diagnostics.Process.GetProcessById(pid);
                    Log.Information("  Terminating PID {Pid} ({Name})...", pid, proc.ProcessName);
                    proc.Kill(entireProcessTree: true);
                    proc.WaitForExit(3000);
                    Log.Information("  PID {Pid} terminated.", pid);
                }
                catch (Exception ex)
                {
                    Log.Warning("  Could not terminate PID {Pid}: {Error}", pid, ex.Message);
                }
            }

            // Brief pause to let the OS release the port before Kestrel binds
            System.Threading.Thread.Sleep(500);
        }
        catch (Exception ex)
        {
            Log.Warning("Stale instance cleanup failed (non-critical): {Error}", ex.Message);
        }
    }

    /// <summary>
    /// Returns PIDs of all processes with a TCP LISTENING socket on the given port.
    /// Uses netstat -ano on Windows, ss -tlnp on Linux/macOS.
    /// </summary>
    private static List<int> GetListeningPidsOnPort(int port)
    {
        var pids = new List<int>();
        try
        {
            if (OperatingSystem.IsWindows())
            {
                // Windows: netstat -ano
                // Format: "  TCP    0.0.0.0:8182    0.0.0.0:0    LISTENING    1234"
                var result = RunProcess("netstat", "-ano");
                if (result.exitCode != 0) return pids;
                var suffix = $":{port}";
                foreach (var line in result.output.Split('\n'))
                {
                    var parts = line.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 5) continue;
                    if (!parts[0].Equals("TCP", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!parts[3].Equals("LISTENING", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!parts[1].EndsWith(suffix)) continue;
                    if (int.TryParse(parts[4], out var pid))
                        pids.Add(pid);
                }
            }
            else
            {
                // Linux/macOS: ss -tlnp sport = :<port>
                // Format: "tcp LISTEN 0 128 0.0.0.0:8182 0.0.0.0:* users:(("NexusM",pid=1234,fd=10))"
                var result = RunProcess("ss", $"-tlnp sport = :{port}");
                if (result.exitCode != 0) return pids;
                foreach (var line in result.output.Split('\n'))
                {
                    var m = System.Text.RegularExpressions.Regex.Match(line, @"pid=(\d+)");
                    if (m.Success && int.TryParse(m.Groups[1].Value, out var pid))
                        pids.Add(pid);
                }
            }
        }
        catch { /* ignore */ }
        return pids;
    }

    /// <summary>
    /// Ensures a Windows Firewall inbound rule exists for the configured server port.
    /// Removes any stale NexusM rules on other ports and creates/updates as needed.
    /// </summary>
    private static void EnsureFirewallRule(int port)
    {
        if (!OperatingSystem.IsWindows()) return;

        const string ruleName = "NexusM Media Server";
        try
        {
            // Check if a rule with the correct port already exists
            var checkResult = RunProcess("netsh", $"advfirewall firewall show rule name=\"{ruleName}\" verbose");
            if (checkResult.exitCode == 0 && checkResult.output.Contains($"{port}"))
            {
                Log.Debug("Firewall rule '{RuleName}' already exists for port {Port}", ruleName, port);
                return;
            }

            // Delete any existing NexusM rule (may have old port)
            if (checkResult.exitCode == 0)
            {
                RunProcess("netsh", $"advfirewall firewall delete rule name=\"{ruleName}\"");
                Log.Information("Removed old firewall rule '{RuleName}'", ruleName);
            }

            // Create new rule for the current port
            var addResult = RunProcess("netsh",
                $"advfirewall firewall add rule name=\"{ruleName}\" dir=in action=allow protocol=TCP localport={port} profile=any enable=yes");

            if (addResult.exitCode == 0)
                Log.Information("Created firewall rule '{RuleName}' for TCP port {Port}", ruleName, port);
            else
                Log.Warning("Could not create firewall rule (may need admin privileges): {Output}", addResult.output);
        }
        catch (Exception ex)
        {
            Log.Warning("Firewall rule check failed (non-critical): {Error}", ex.Message);
        }
    }

    private static (int exitCode, string output) RunProcess(string fileName, string arguments)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = System.Diagnostics.Process.Start(psi)!;
            var output = proc.StandardOutput.ReadToEnd() + proc.StandardError.ReadToEnd();
            proc.WaitForExit(5000);
            return (proc.ExitCode, output);
        }
        catch (Exception ex)
        {
            return (-1, ex.Message);
        }
    }
}
