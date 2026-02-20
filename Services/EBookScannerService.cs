using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using NexusM.Data;
using NexusM.Models;
using PdfSharpCore.Pdf.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;
using VersOne.Epub;

namespace NexusM.Services;

/// <summary>
/// Scans configured eBooks folders and indexes PDF/EPUB files into the SQLite eBooks database.
/// Uses PdfSharpCore for PDF metadata and VersOne.Epub for EPUB metadata.
/// Generates cover images: EPUB cover extraction + PDF first-page rendering via PDFtoImage.
/// </summary>
public class EBookScannerService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ConfigService _configService;
    private readonly ILogger<EBookScannerService> _logger;

    private int _scanningFlag; // 0 = idle, 1 = scanning (atomic)
    private EBookScanProgress _currentProgress = new();

    // PDFtoImage (PDFium) is NOT thread-safe; serialize all PDF cover rendering
    private static readonly SemaphoreSlim _pdfRenderLock = new(1, 1);

    public bool IsScanning => _scanningFlag == 1;
    public EBookScanProgress CurrentProgress => _currentProgress;

    public EBookScannerService(
        IServiceProvider serviceProvider,
        ConfigService configService,
        ILogger<EBookScannerService> logger)
    {
        _serviceProvider = serviceProvider;
        _configService = configService;
        _logger = logger;
    }

    /// <summary>
    /// Start an eBooks library scan in the background.
    /// </summary>
    public Task StartScanAsync()
    {
        if (Interlocked.CompareExchange(ref _scanningFlag, 1, 0) != 0)
        {
            _logger.LogWarning("eBooks scan already in progress, ignoring request.");
            return Task.CompletedTask;
        }

        try
        {
            return Task.Run(async () => await ScanEBooksAsync());
        }
        catch
        {
            Interlocked.Exchange(ref _scanningFlag, 0);
            throw;
        }
    }

    private async Task ScanEBooksAsync()
    {
        _currentProgress = new EBookScanProgress { Status = "scanning", StartTime = DateTime.UtcNow };

        var folders = _configService.Config.Library.GetEBooksFolderList();
        var extensions = _configService.Config.Library.GetEBookExtensionList()
            .Select(e => e.ToLowerInvariant()).ToHashSet();

        if (folders.Count == 0)
        {
            _logger.LogWarning("No eBooks folders configured. Edit NexusM.conf [Library] EBooksFolders.");
            _currentProgress.Status = "completed";
            _currentProgress.Message = "No eBooks folders configured";
            Interlocked.Exchange(ref _scanningFlag, 0);
            return;
        }

        _logger.LogInformation("Starting eBooks scan. Folders: {Count}, Extensions: {Ext}",
            folders.Count, string.Join(", ", extensions));

        try
        {
            // Collect all eBook files
            var ebookFiles = new List<string>();
            foreach (var folder in folders)
            {
                if (!Directory.Exists(folder))
                {
                    _logger.LogWarning("eBooks folder not found: {Folder}", folder);
                    continue;
                }

                try
                {
                    var files = Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories)
                        .Where(f => extensions.Contains(Path.GetExtension(f).ToLowerInvariant()));
                    ebookFiles.AddRange(files);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error enumerating folder: {Folder}", folder);
                }
            }

            // Normalize paths and deduplicate to prevent double inserts
            var uniqueFiles = ebookFiles
                .Select(f => Path.GetFullPath(f))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            ebookFiles = uniqueFiles;

            _currentProgress.TotalFiles = ebookFiles.Count;
            _logger.LogInformation("Found {Count} eBook files to process", ebookFiles.Count);

            // Process files with configured parallelism
            var maxThreads = Math.Max(1, _configService.Config.Library.ScanThreads);
            var semaphore = new SemaphoreSlim(maxThreads);

            var tasks = ebookFiles.Select(async filePath =>
            {
                await semaphore.WaitAsync();
                try
                {
                    await ProcessEBookFileAsync(filePath, folders);
                    Interlocked.Increment(ref _currentProgress._processedFiles);
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(tasks);

            _currentProgress.Status = "completed";
            _currentProgress.Message = $"Scan complete. {_currentProgress.NewBooks} new, {_currentProgress.UpdatedBooks} updated, {_currentProgress.ErrorCount} errors.";
            _logger.LogInformation(_currentProgress.Message);
        }
        catch (Exception ex)
        {
            _currentProgress.Status = "failed";
            _currentProgress.Message = ex.Message;
            _logger.LogError(ex, "eBooks scan failed");
        }
        finally
        {
            Interlocked.Exchange(ref _scanningFlag, 0);
        }
    }

    // Track paths currently being processed to prevent parallel duplicate inserts
    private readonly ConcurrentDictionary<string, byte> _processingPaths = new(StringComparer.OrdinalIgnoreCase);

    private async Task ProcessEBookFileAsync(string filePath, List<string> rootFolders)
    {
        try
        {
            filePath = Path.GetFullPath(filePath);

            if (!_processingPaths.TryAdd(filePath, 0))
                return;

            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<EBooksDbContext>();

            var fileInfo = new FileInfo(filePath);
            var normalizedPath = filePath.ToLowerInvariant();
            var existing = await db.EBooks.FirstOrDefaultAsync(e => e.FilePath.ToLower() == normalizedPath);

            if (existing != null && existing.LastModified == fileInfo.LastWriteTimeUtc)
            {
                // File unchanged — but generate cover if missing
                if (string.IsNullOrEmpty(existing.CoverImage))
                {
                    var coverFile = await GenerateEBookCoverAsync(filePath, existing.Id, existing.Format);
                    if (coverFile != null)
                    {
                        existing.CoverImage = coverFile;
                        db.EBooks.Update(existing);
                        await db.SaveChangesAsync();
                    }
                }
                return;
            }

            // Read metadata based on format
            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            var format = ext.TrimStart('.').ToUpperInvariant();
            string title = Path.GetFileNameWithoutExtension(filePath);
            string author = "";
            int pageCount = 0;
            string? publisher = null, language = null, isbn = null, description = null, subject = null;

            if (ext == ".pdf")
            {
                ReadPdfMetadata(filePath, ref title, ref author, ref pageCount,
                    ref publisher, ref language, ref subject);
            }
            else if (ext == ".epub")
            {
                ReadEpubMetadata(filePath, ref title, ref author,
                    ref publisher, ref language, ref isbn, ref description, ref subject);
            }

            // Determine category from first-level subfolder
            var category = DetermineCategory(filePath, rootFolders);

            if (existing != null)
            {
                // Update existing eBook
                existing.FileName = Path.GetFileName(filePath);
                existing.Title = title;
                existing.Author = author;
                existing.Format = format;
                existing.FileSize = fileInfo.Length;
                existing.PageCount = pageCount;
                existing.Category = category;
                existing.LastModified = fileInfo.LastWriteTimeUtc;
                existing.Publisher = publisher;
                existing.Language = language;
                existing.ISBN = isbn;
                existing.Description = description;
                existing.Subject = subject;

                // Regenerate cover if missing
                if (string.IsNullOrEmpty(existing.CoverImage))
                {
                    var coverFile = await GenerateEBookCoverAsync(filePath, existing.Id, format);
                    if (coverFile != null) existing.CoverImage = coverFile;
                }

                db.EBooks.Update(existing);
                await db.SaveChangesAsync();
                Interlocked.Increment(ref _currentProgress._updatedBooks);
            }
            else
            {
                // Create new eBook — save first to get the Id for cover filename
                var ebook = new EBook
                {
                    FilePath = filePath,
                    FileName = Path.GetFileName(filePath),
                    Title = title,
                    Author = author,
                    Format = format,
                    FileSize = fileInfo.Length,
                    PageCount = pageCount,
                    Category = category,
                    DateAdded = DateTime.UtcNow,
                    LastModified = fileInfo.LastWriteTimeUtc,
                    Publisher = publisher,
                    Language = language,
                    ISBN = isbn,
                    Description = description,
                    Subject = subject
                };

                db.EBooks.Add(ebook);
                try
                {
                    await db.SaveChangesAsync(); // Save to get the Id
                }
                catch (DbUpdateException)
                {
                    _logger.LogDebug("Skipping duplicate eBook: {File}", filePath);
                    return;
                }

                // Generate cover image using the assigned Id
                var coverFile = await GenerateEBookCoverAsync(filePath, ebook.Id, format);
                if (coverFile != null)
                {
                    ebook.CoverImage = coverFile;
                    db.EBooks.Update(ebook);
                    await db.SaveChangesAsync();
                }

                Interlocked.Increment(ref _currentProgress._newBooks);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing eBook: {File}", filePath);
            Interlocked.Increment(ref _currentProgress._errorCount);
        }
        finally
        {
            _processingPaths.TryRemove(filePath, out _);
        }
    }

    /// <summary>
    /// Generates a cover image for an eBook and saves to assets/ebookcovers/.
    /// Returns the filename or null on failure.
    /// </summary>
    private async Task<string?> GenerateEBookCoverAsync(string filePath, int ebookId, string format)
    {
        try
        {
            var coverDir = Path.Combine(AppContext.BaseDirectory, "assets", "ebookcovers");
            if (!Directory.Exists(coverDir))
                Directory.CreateDirectory(coverDir);

            if (format == "EPUB")
            {
                var coverFilename = $"epub_cover_{ebookId}.jpg";
                var coverPath = Path.Combine(coverDir, coverFilename);

                if (File.Exists(coverPath))
                    return coverFilename;

                return ExtractEpubCover(filePath, coverPath, coverFilename);
            }
            else if (format == "PDF")
            {
                var coverFilename = $"pdf_preview_{ebookId}.jpg";
                var coverPath = Path.Combine(coverDir, coverFilename);

                if (File.Exists(coverPath))
                    return coverFilename;

                // Skip very large PDFs (>200MB) to avoid memory issues
                var fi = new FileInfo(filePath);
                if (fi.Length > 200 * 1024 * 1024)
                {
                    _logger.LogDebug("Skipping PDF cover for large file: {File} ({Size}MB)", filePath, fi.Length / 1024 / 1024);
                    return null;
                }

                return await RenderPdfCover(filePath, coverPath, coverFilename);
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to generate cover for eBook {Id}: {Error}", ebookId, ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Extracts the embedded cover image from an EPUB file using VersOne.Epub.
    /// </summary>
    private string? ExtractEpubCover(string filePath, string coverPath, string coverFilename)
    {
        try
        {
            using var bookRef = EpubReader.OpenBook(filePath);
            byte[]? coverData = bookRef.ReadCover();

            if (coverData == null || coverData.Length == 0)
            {
                _logger.LogDebug("No cover image found in EPUB: {File}", filePath);
                return null;
            }

            // Load cover image, resize to max 400x600 (book proportions), save as JPEG
            using var image = Image.Load(coverData);
            image.Mutate(x => x.Resize(new ResizeOptions
            {
                Size = new Size(400, 600),
                Mode = ResizeMode.Max
            }));

            var encoder = new JpegEncoder { Quality = 90 };
            image.Save(coverPath, encoder);

            return coverFilename;
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Failed to extract EPUB cover from {File}: {Error}", filePath, ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Renders the first page of a PDF file as a JPEG cover image using PDFtoImage.
    /// </summary>
    private async Task<string?> RenderPdfCover(string filePath, string coverPath, string coverFilename)
    {
        try
        {
            // Serialize PDF rendering — PDFtoImage (PDFium) is not thread-safe
            await _pdfRenderLock.WaitAsync();
            try
            {
                using var inputStream = File.OpenRead(filePath);
                var renderOptions = new PDFtoImage.RenderOptions { Dpi = 150 };

                // Render first page to a temporary PNG in memory
                using var tempStream = new MemoryStream();
#pragma warning disable CA1416 // PDFtoImage uses platform-specific PDFium, validated at runtime
                PDFtoImage.Conversion.SavePng(tempStream, inputStream, page: 0, leaveOpen: false, password: null, options: renderOptions);
#pragma warning restore CA1416
                tempStream.Position = 0;

                // Load with ImageSharp, resize to max 400x600, save as JPEG
                using var image = Image.Load(tempStream);
                image.Mutate(x => x.Resize(new ResizeOptions
                {
                    Size = new Size(400, 600),
                    Mode = ResizeMode.Max
                }));

                var encoder = new JpegEncoder { Quality = 85 };
                image.Save(coverPath, encoder);

                return coverFilename;
            }
            finally
            {
                _pdfRenderLock.Release();
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Failed to render PDF cover from {File}: {Error}", filePath, ex.Message);
            return null;
        }
    }

    private void ReadPdfMetadata(string filePath, ref string title, ref string author,
        ref int pageCount, ref string? publisher, ref string? language, ref string? subject)
    {
        try
        {
            using var document = PdfReader.Open(filePath, PdfDocumentOpenMode.InformationOnly);
            pageCount = document.PageCount;

            if (!string.IsNullOrWhiteSpace(document.Info?.Title))
                title = document.Info.Title.Trim();
            if (!string.IsNullOrWhiteSpace(document.Info?.Author))
                author = document.Info.Author.Trim();
            if (!string.IsNullOrWhiteSpace(document.Info?.Subject))
                subject = document.Info.Subject.Trim();
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Could not read PDF metadata from {File}: {Error}", filePath, ex.Message);
        }
    }

    private void ReadEpubMetadata(string filePath, ref string title, ref string author,
        ref string? publisher, ref string? language, ref string? isbn,
        ref string? description, ref string? subject)
    {
        try
        {
            using var bookRef = EpubReader.OpenBook(filePath);

            if (!string.IsNullOrWhiteSpace(bookRef.Title))
                title = bookRef.Title.Trim();
            if (bookRef.AuthorList != null && bookRef.AuthorList.Count > 0)
                author = string.Join(", ", bookRef.AuthorList);

            if (!string.IsNullOrWhiteSpace(bookRef.Description))
                description = bookRef.Description.Trim();
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Could not read EPUB metadata from {File}: {Error}", filePath, ex.Message);
        }
    }

    /// <summary>
    /// Determines the category (first-level subfolder name) for an eBook file.
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
}
