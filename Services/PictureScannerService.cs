using System.Collections.Concurrent;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using Microsoft.EntityFrameworkCore;
using NexusM.Data;
using NexusM.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace NexusM.Services;

/// <summary>
/// Scans configured pictures folders and indexes images into the SQLite pictures database.
/// Uses MetadataExtractor for EXIF reading and ImageSharp for thumbnail generation.
/// </summary>
public class PictureScannerService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ConfigService _configService;
    private readonly ILogger<PictureScannerService> _logger;

    private int _scanningFlag; // 0 = idle, 1 = scanning (atomic)
    private PictureScanProgress _currentProgress = new();

    public bool IsScanning => _scanningFlag == 1;
    public PictureScanProgress CurrentProgress => _currentProgress;

    public PictureScannerService(
        IServiceProvider serviceProvider,
        ConfigService configService,
        ILogger<PictureScannerService> logger)
    {
        _serviceProvider = serviceProvider;
        _configService = configService;
        _logger = logger;
    }

    /// <summary>
    /// Start a pictures library scan in the background.
    /// </summary>
    public Task StartScanAsync()
    {
        if (Interlocked.CompareExchange(ref _scanningFlag, 1, 0) != 0)
        {
            _logger.LogWarning("Pictures scan already in progress, ignoring request.");
            return Task.CompletedTask;
        }

        try
        {
            return Task.Run(async () => await ScanPicturesAsync());
        }
        catch
        {
            Interlocked.Exchange(ref _scanningFlag, 0);
            throw;
        }
    }

    private async Task ScanPicturesAsync()
    {
        _currentProgress = new PictureScanProgress { Status = "scanning", StartTime = DateTime.UtcNow };

        var folders = _configService.Config.Library.GetPicturesFolderList();
        var extensions = _configService.Config.Library.GetImageExtensionList()
            .Select(e => e.ToLowerInvariant()).ToHashSet();

        if (folders.Count == 0)
        {
            _logger.LogWarning("No pictures folders configured. Edit NexusM.conf [Library] PicturesFolders.");
            _currentProgress.Status = "completed";
            _currentProgress.Message = "No pictures folders configured";
            Interlocked.Exchange(ref _scanningFlag, 0);
            return;
        }

        _logger.LogInformation("Starting pictures scan. Folders: {Count}, Extensions: {Ext}",
            folders.Count, string.Join(", ", extensions));

        try
        {
            // Collect all image files
            var imageFiles = new List<string>();
            foreach (var folder in folders)
            {
                if (!System.IO.Directory.Exists(folder))
                {
                    _logger.LogWarning("Pictures folder not found: {Folder}", folder);
                    continue;
                }

                try
                {
                    var files = System.IO.Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories)
                        .Where(f => extensions.Contains(Path.GetExtension(f).ToLowerInvariant()));
                    imageFiles.AddRange(files);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error enumerating folder: {Folder}", folder);
                }
            }

            // Normalize paths and deduplicate to prevent double inserts
            var uniqueFiles = imageFiles
                .Select(f => Path.GetFullPath(f))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            imageFiles = uniqueFiles;

            _currentProgress.TotalFiles = imageFiles.Count;
            _logger.LogInformation("Found {Count} image files to process", imageFiles.Count);

            // Process files with configured parallelism
            var maxThreads = Math.Max(1, _configService.Config.Library.ScanThreads);
            var semaphore = new SemaphoreSlim(maxThreads);

            var tasks = imageFiles.Select(async filePath =>
            {
                await semaphore.WaitAsync();
                try
                {
                    await ProcessPictureFileAsync(filePath, folders);
                    Interlocked.Increment(ref _currentProgress._processedFiles);
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(tasks);

            _currentProgress.Status = "completed";
            _currentProgress.Message = $"Scan complete. {_currentProgress.NewPictures} new, {_currentProgress.UpdatedPictures} updated, {_currentProgress.ErrorCount} errors.";
            _logger.LogInformation(_currentProgress.Message);
        }
        catch (Exception ex)
        {
            _currentProgress.Status = "failed";
            _currentProgress.Message = ex.Message;
            _logger.LogError(ex, "Pictures scan failed");
        }
        finally
        {
            Interlocked.Exchange(ref _scanningFlag, 0);
        }
    }

    // Track paths currently being processed to prevent parallel duplicate inserts
    private readonly ConcurrentDictionary<string, byte> _processingPaths = new(StringComparer.OrdinalIgnoreCase);

    private async Task ProcessPictureFileAsync(string filePath, List<string> rootFolders)
    {
        try
        {
            filePath = Path.GetFullPath(filePath);

            if (!_processingPaths.TryAdd(filePath, 0))
                return;

            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PicturesDbContext>();

            var fileInfo = new FileInfo(filePath);
            var normalizedPath = filePath.ToLowerInvariant();
            var existing = await db.Pictures.FirstOrDefaultAsync(p => p.FilePath.ToLower() == normalizedPath);

            if (existing != null && existing.LastModified == fileInfo.LastWriteTimeUtc)
            {
                // File unchanged, skip
                return;
            }

            // Read EXIF metadata
            string? cameraMake = null, cameraModel = null, software = null;
            string? exposureTime = null, fNumber = null, focalLength = null, flash = null, lensModel = null;
            DateTime? dateTaken = null;
            int? isoSpeed = null, orientation = null;

            try
            {
                var directories = ImageMetadataReader.ReadMetadata(filePath);
                var ifd0 = directories.OfType<ExifIfd0Directory>().FirstOrDefault();
                var subIfd = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();

                if (ifd0 != null)
                {
                    cameraMake = ifd0.GetDescription(ExifDirectoryBase.TagMake)?.Trim();
                    cameraModel = ifd0.GetDescription(ExifDirectoryBase.TagModel)?.Trim();
                    software = ifd0.GetDescription(ExifDirectoryBase.TagSoftware)?.Trim();
                    if (ifd0.TryGetInt32(ExifDirectoryBase.TagOrientation, out var orient))
                        orientation = orient;
                }

                if (subIfd != null)
                {
                    if (subIfd.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out var dt))
                        dateTaken = dt;
                    if (subIfd.TryGetInt32(ExifDirectoryBase.TagIsoEquivalent, out var iso))
                        isoSpeed = iso;
                    exposureTime = subIfd.GetDescription(ExifDirectoryBase.TagExposureTime)?.Trim();
                    fNumber = subIfd.GetDescription(ExifDirectoryBase.TagFNumber)?.Trim();
                    focalLength = subIfd.GetDescription(ExifDirectoryBase.TagFocalLength)?.Trim();
                    flash = subIfd.GetDescription(ExifDirectoryBase.TagFlash)?.Trim();
                    lensModel = subIfd.GetDescription(ExifDirectoryBase.TagLensModel)?.Trim();
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug("Could not read EXIF from {File}: {Error}", filePath, ex.Message);
            }

            // Get image dimensions using ImageSharp
            int width = 0, height = 0;
            double? dpiX = null, dpiY = null;
            string format;

            try
            {
                var imageInfo = Image.Identify(filePath);
                if (imageInfo != null)
                {
                    width = imageInfo.Width;
                    height = imageInfo.Height;
                    dpiX = imageInfo.Metadata.HorizontalResolution > 0 ? imageInfo.Metadata.HorizontalResolution : null;
                    dpiY = imageInfo.Metadata.VerticalResolution > 0 ? imageInfo.Metadata.VerticalResolution : null;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug("Could not identify image {File}: {Error}", filePath, ex.Message);
            }

            format = Path.GetExtension(filePath).TrimStart('.').ToUpperInvariant();

            // Determine category from first-level subfolder
            var category = DetermineCategory(filePath, rootFolders);

            if (existing != null)
            {
                // Update existing picture
                existing.FileName = Path.GetFileName(filePath);
                existing.Width = width;
                existing.Height = height;
                existing.SizeBytes = fileInfo.Length;
                existing.Format = format;
                existing.DateTaken = dateTaken;
                existing.CameraMake = cameraMake;
                existing.CameraModel = cameraModel;
                existing.LastModified = fileInfo.LastWriteTimeUtc;
                existing.Category = category;
                existing.IsoSpeed = isoSpeed;
                existing.ExposureTime = exposureTime;
                existing.FNumber = fNumber;
                existing.FocalLength = focalLength;
                existing.Flash = flash;
                existing.Orientation = orientation;
                existing.DpiX = dpiX;
                existing.DpiY = dpiY;
                existing.LensModel = lensModel;
                existing.Software = software;

                // Regenerate thumbnail if missing
                if (string.IsNullOrEmpty(existing.ThumbnailPath))
                {
                    var thumbFile = GenerateThumbnail(filePath, existing.Id);
                    if (thumbFile != null) existing.ThumbnailPath = thumbFile;
                }

                db.Pictures.Update(existing);
                Interlocked.Increment(ref _currentProgress._updatedPictures);
            }
            else
            {
                // Create new picture
                var picture = new Picture
                {
                    FilePath = filePath,
                    FileName = Path.GetFileName(filePath),
                    Width = width,
                    Height = height,
                    SizeBytes = fileInfo.Length,
                    Format = format,
                    DateTaken = dateTaken,
                    CameraMake = cameraMake,
                    CameraModel = cameraModel,
                    DateAdded = DateTime.UtcNow,
                    LastModified = fileInfo.LastWriteTimeUtc,
                    Category = category,
                    IsoSpeed = isoSpeed,
                    ExposureTime = exposureTime,
                    FNumber = fNumber,
                    FocalLength = focalLength,
                    Flash = flash,
                    Orientation = orientation,
                    DpiX = dpiX,
                    DpiY = dpiY,
                    LensModel = lensModel,
                    Software = software
                };

                db.Pictures.Add(picture);
                try
                {
                    await db.SaveChangesAsync(); // Save to get the Id
                }
                catch (DbUpdateException)
                {
                    _logger.LogDebug("Skipping duplicate picture: {File}", filePath);
                    return;
                }

                // Generate thumbnail
                var thumbFile = GenerateThumbnail(filePath, picture.Id);
                if (thumbFile != null)
                {
                    picture.ThumbnailPath = thumbFile;
                    db.Pictures.Update(picture);
                }

                Interlocked.Increment(ref _currentProgress._newPictures);
            }

            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing picture: {File}", filePath);
            Interlocked.Increment(ref _currentProgress._errorCount);
        }
        finally
        {
            _processingPaths.TryRemove(filePath, out _);
        }
    }

    /// <summary>
    /// Determines the category (first-level subfolder name) for a picture file.
    /// </summary>
    private static string DetermineCategory(string filePath, List<string> rootFolders)
    {
        foreach (var root in rootFolders)
        {
            var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var normalizedFile = Path.GetFullPath(filePath);

            if (normalizedFile.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                normalizedFile.StartsWith(normalizedRoot + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                var relative = normalizedFile.Substring(normalizedRoot.Length + 1);
                var firstSep = relative.IndexOfAny(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar });
                if (firstSep > 0)
                    return relative.Substring(0, firstSep);
                return ""; // File directly in root
            }
        }
        return "";
    }

    /// <summary>
    /// Generates a JPEG thumbnail (max 400x300) and saves to assets/thumbs/.
    /// Returns the filename or null on failure.
    /// </summary>
    private string? GenerateThumbnail(string filePath, int pictureId)
    {
        try
        {
            var thumbDir = Path.Combine(AppContext.BaseDirectory, "assets", "thumbs");
            if (!System.IO.Directory.Exists(thumbDir))
                System.IO.Directory.CreateDirectory(thumbDir);

            var thumbFilename = $"picthumb_{pictureId}.jpg";
            var thumbPath = Path.Combine(thumbDir, thumbFilename);

            // Skip if already exists
            if (System.IO.File.Exists(thumbPath))
                return thumbFilename;

            // Skip very large files (>50MB) to avoid memory issues
            var fi = new FileInfo(filePath);
            if (fi.Length > 50 * 1024 * 1024)
            {
                _logger.LogDebug("Skipping thumbnail for large file: {File} ({Size}MB)", filePath, fi.Length / 1024 / 1024);
                return null;
            }

            using var image = Image.Load(filePath);

            image.Mutate(x => x.Resize(new ResizeOptions
            {
                Size = new Size(400, 300),
                Mode = ResizeMode.Max
            }));

            var encoder = new JpegEncoder { Quality = 85 };
            image.Save(thumbPath, encoder);

            return thumbFilename;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to generate thumbnail for picture {Id}: {Error}", pictureId, ex.Message);
            return null;
        }
    }
}
