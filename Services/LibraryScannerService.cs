using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using NexusM.Data;
using NexusM.Models;

namespace NexusM.Services;

/// <summary>
/// Scans configured music folders and indexes tracks into the SQLite database.
/// Uses TagLibSharp for reading ID3/FLAC/Vorbis/APE tags.
/// </summary>
public class LibraryScannerService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ConfigService _configService;
    private readonly ILogger<LibraryScannerService> _logger;

    private int _scanningFlag; // 0 = idle, 1 = scanning (atomic)
    private ScanProgress _currentProgress = new();

    public bool IsScanning => _scanningFlag == 1;
    public ScanProgress CurrentProgress => _currentProgress;

    public LibraryScannerService(
        IServiceProvider serviceProvider,
        ConfigService configService,
        ILogger<LibraryScannerService> logger)
    {
        _serviceProvider = serviceProvider;
        _configService = configService;
        _logger = logger;
    }

    /// <summary>
    /// Start a library scan in the background.
    /// </summary>
    public Task StartScanAsync()
    {
        if (Interlocked.CompareExchange(ref _scanningFlag, 1, 0) != 0)
        {
            _logger.LogWarning("Scan already in progress, ignoring request.");
            return Task.CompletedTask;
        }

        try
        {
            return Task.Run(async () => await ScanLibraryAsync());
        }
        catch
        {
            Interlocked.Exchange(ref _scanningFlag, 0);
            throw;
        }
    }

    private async Task ScanLibraryAsync()
    {
        _currentProgress = new ScanProgress { Status = "scanning", StartTime = DateTime.UtcNow };

        var folders = _configService.Config.Library.GetMusicFolderList();
        var extensions = _configService.Config.Library.GetAudioExtensionList()
            .Select(e => e.ToLowerInvariant()).ToHashSet();

        if (folders.Count == 0)
        {
            _logger.LogWarning("No music folders configured. Edit NexusM.conf [Library] MusicFolders.");
            _currentProgress.Status = "completed";
            _currentProgress.Message = "No music folders configured";
            Interlocked.Exchange(ref _scanningFlag, 0);
            return;
        }

        _logger.LogInformation("Starting library scan. Folders: {Count}, Extensions: {Ext}",
            folders.Count, string.Join(", ", extensions));

        try
        {
            // Collect all audio files
            var audioFiles = new List<string>();
            foreach (var folder in folders)
            {
                if (!Directory.Exists(folder))
                {
                    _logger.LogWarning("Music folder not found: {Folder}", folder);
                    continue;
                }

                try
                {
                    var files = Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories)
                        .Where(f => extensions.Contains(Path.GetExtension(f).ToLowerInvariant()));
                    audioFiles.AddRange(files);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error enumerating folder: {Folder}", folder);
                }
            }

            // Normalize paths and deduplicate to prevent double inserts
            var uniqueFiles = audioFiles
                .Select(f => Path.GetFullPath(f))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            audioFiles = uniqueFiles;

            _currentProgress.TotalFiles = audioFiles.Count;
            _logger.LogInformation("Found {Count} audio files to process", audioFiles.Count);

            // Process files with configured parallelism
            var maxThreads = Math.Max(1, _configService.Config.Library.ScanThreads);
            var semaphore = new SemaphoreSlim(maxThreads);

            var tasks = audioFiles.Select(async filePath =>
            {
                await semaphore.WaitAsync();
                try
                {
                    await ProcessFileAsync(filePath);
                    Interlocked.Increment(ref _currentProgress._processedFiles);
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(tasks);

            _currentProgress.Status = "completed";
            _currentProgress.Message = $"Scan complete. {_currentProgress.NewTracks} new, {_currentProgress.UpdatedTracks} updated, {_currentProgress.ErrorCount} errors.";
            _logger.LogInformation(_currentProgress.Message);
        }
        catch (Exception ex)
        {
            _currentProgress.Status = "failed";
            _currentProgress.Message = ex.Message;
            _logger.LogError(ex, "Library scan failed");
        }
        finally
        {
            Interlocked.Exchange(ref _scanningFlag, 0);
        }
    }

    // Track paths currently being processed to prevent parallel duplicate inserts
    private readonly ConcurrentDictionary<string, byte> _processingPaths = new(StringComparer.OrdinalIgnoreCase);

    private async Task ProcessFileAsync(string filePath)
    {
        try
        {
            filePath = Path.GetFullPath(filePath);

            if (!_processingPaths.TryAdd(filePath, 0))
                return;

            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MusicDbContext>();

            // Check if file already exists and hasn't changed
            var fileInfo = new FileInfo(filePath);
            var normalizedPath = filePath.ToLowerInvariant();
            var existing = await db.Tracks.FirstOrDefaultAsync(t => t.FilePath.ToLower() == normalizedPath);

            if (existing != null && existing.FileModified == fileInfo.LastWriteTimeUtc)
            {
                // File unchanged, skip
                return;
            }

            // Read metadata using TagLib
            TagLib.File? tagFile = null;
            try
            {
                tagFile = TagLib.File.Create(filePath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Cannot read tags from {File}: {Error}", filePath, ex.Message);
                Interlocked.Increment(ref _currentProgress._errorCount);
                return;
            }

            var tag = tagFile.Tag;
            var props = tagFile.Properties;

            // Determine or create Album record
            var albumName = string.IsNullOrWhiteSpace(tag.Album) ? "Unknown Album" : tag.Album;
            var artistName = string.IsNullOrWhiteSpace(tag.FirstPerformer) ? "Unknown Artist" : tag.FirstPerformer;
            var albumArtist = string.IsNullOrWhiteSpace(tag.FirstAlbumArtist) ? artistName : tag.FirstAlbumArtist;

            // Find or create album
            var album = await db.Albums.FirstOrDefaultAsync(a => a.Name == albumName && a.Artist == albumArtist);
            if (album == null)
            {
                album = new Album
                {
                    Name = albumName,
                    Artist = albumArtist,
                    Year = tag.Year > 0 ? (int?)tag.Year : null,
                    Genre = tag.FirstGenre ?? "",
                    DateAdded = DateTime.UtcNow
                };
                db.Albums.Add(album);
                await db.SaveChangesAsync();
            }

            // Find or create artist
            var artist = await db.Artists.FirstOrDefaultAsync(a => a.Name == artistName);
            if (artist == null)
            {
                artist = new Artist { Name = artistName };
                db.Artists.Add(artist);
                await db.SaveChangesAsync();
            }

            // Determine MIME type
            var mimeType = Path.GetExtension(filePath).ToLowerInvariant() switch
            {
                ".mp3" => "audio/mpeg",
                ".flac" => "audio/flac",
                ".wav" => "audio/wav",
                ".m4a" or ".aac" => "audio/mp4",
                ".ogg" or ".opus" => "audio/ogg",
                ".wma" => "audio/x-ms-wma",
                ".aiff" => "audio/aiff",
                ".ape" => "audio/ape",
                _ => "audio/unknown"
            };

            var hasArt = tag.Pictures?.Length > 0;

            if (existing != null)
            {
                // Update existing track
                existing.Title = string.IsNullOrWhiteSpace(tag.Title) ? Path.GetFileNameWithoutExtension(filePath) : tag.Title;
                existing.Artist = artistName;
                existing.AlbumArtist = albumArtist;
                existing.Album = albumName;
                existing.Year = tag.Year > 0 ? (int?)tag.Year : null;
                existing.TrackNumber = tag.Track > 0 ? (int?)tag.Track : null;
                existing.DiscNumber = tag.Disc > 0 ? (int?)tag.Disc : null;
                existing.Genre = tag.FirstGenre ?? "";
                existing.Composer = tag.FirstComposer ?? "";
                existing.Duration = props.Duration.TotalSeconds;
                existing.Bitrate = props.AudioBitrate;
                existing.SampleRate = props.AudioSampleRate;
                existing.Channels = props.AudioChannels;
                existing.Codec = props.Codecs?.FirstOrDefault()?.Description ?? "";
                existing.FileSize = fileInfo.Length;
                existing.MimeType = mimeType;
                existing.HasAlbumArt = hasArt;
                existing.FileModified = fileInfo.LastWriteTimeUtc;
                existing.LastScanned = DateTime.UtcNow;
                existing.AlbumId = album.Id;

                // Extract album art to disk
                if (hasArt)
                {
                    var artFilename = ExtractAlbumArt(tag, existing.Id);
                    if (artFilename != null) existing.AlbumArtCached = artFilename;
                }

                db.Tracks.Update(existing);
                Interlocked.Increment(ref _currentProgress._updatedTracks);
            }
            else
            {
                // Create new track
                var track = new Track
                {
                    FilePath = filePath,
                    FileName = Path.GetFileName(filePath),
                    Title = string.IsNullOrWhiteSpace(tag.Title) ? Path.GetFileNameWithoutExtension(filePath) : tag.Title,
                    Artist = artistName,
                    AlbumArtist = albumArtist,
                    Album = albumName,
                    Year = tag.Year > 0 ? (int?)tag.Year : null,
                    TrackNumber = tag.Track > 0 ? (int?)tag.Track : null,
                    DiscNumber = tag.Disc > 0 ? (int?)tag.Disc : null,
                    Genre = tag.FirstGenre ?? "",
                    Composer = tag.FirstComposer ?? "",
                    Duration = props.Duration.TotalSeconds,
                    Bitrate = props.AudioBitrate,
                    SampleRate = props.AudioSampleRate,
                    Channels = props.AudioChannels,
                    Codec = props.Codecs?.FirstOrDefault()?.Description ?? "",
                    FileSize = fileInfo.Length,
                    MimeType = mimeType,
                    HasAlbumArt = hasArt,
                    FileModified = fileInfo.LastWriteTimeUtc,
                    LastScanned = DateTime.UtcNow,
                    DateAdded = DateTime.UtcNow,
                    AlbumId = album.Id
                };

                db.Tracks.Add(track);
                try
                {
                    await db.SaveChangesAsync(); // Save to get the track Id
                }
                catch (DbUpdateException)
                {
                    _logger.LogDebug("Skipping duplicate track: {File}", filePath);
                    tagFile.Dispose();
                    return;
                }

                // Extract album art to disk (need Id first)
                if (hasArt)
                {
                    var artFilename = ExtractAlbumArt(tag, track.Id);
                    if (artFilename != null)
                    {
                        track.AlbumArtCached = artFilename;
                        db.Tracks.Update(track);
                    }
                }

                Interlocked.Increment(ref _currentProgress._newTracks);
            }

            await db.SaveChangesAsync();
            tagFile.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing file: {File}", filePath);
            Interlocked.Increment(ref _currentProgress._errorCount);
        }
        finally
        {
            _processingPaths.TryRemove(filePath, out _);
        }
    }

    /// <summary>
    /// Extracts album art from tag data and saves to assets/albumart/ folder.
    /// Returns the filename (e.g., "albumart_123.jpg") or null on failure.
    /// </summary>
    private string? ExtractAlbumArt(TagLib.Tag tag, int trackId)
    {
        try
        {
            var picture = tag.Pictures?.FirstOrDefault();
            if (picture == null || picture.Data == null || picture.Data.Count == 0)
                return null;

            var albumArtDir = Path.Combine(AppContext.BaseDirectory, "assets", "albumart");
            if (!Directory.Exists(albumArtDir))
                Directory.CreateDirectory(albumArtDir);

            var filename = $"albumart_{trackId}.jpg";
            var fullPath = Path.Combine(albumArtDir, filename);

            // Skip if already cached and file exists
            if (System.IO.File.Exists(fullPath))
                return filename;

            System.IO.File.WriteAllBytes(fullPath, picture.Data.Data);
            return filename;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to extract album art for track {TrackId}: {Error}", trackId, ex.Message);
            return null;
        }
    }
}

/// <summary>
/// Tracks progress of an ongoing library scan.
/// </summary>
public class ScanProgress
{
    public string Status { get; set; } = "idle"; // idle, scanning, completed, failed
    public string Message { get; set; } = "";
    public DateTime? StartTime { get; set; }
    public int TotalFiles { get; set; }

    // Thread-safe counters
    internal int _processedFiles;
    internal int _newTracks;
    internal int _updatedTracks;
    internal int _errorCount;

    public int ProcessedFiles => _processedFiles;
    public int NewTracks => _newTracks;
    public int UpdatedTracks => _updatedTracks;
    public int ErrorCount => _errorCount;

    public double PercentComplete => TotalFiles > 0 ? Math.Round((double)_processedFiles / TotalFiles * 100, 1) : 0;
}
