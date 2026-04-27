using uMediaOps.Models;
using uMediaOps.Repositories;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using Umbraco.Cms.Core.IO;
using Umbraco.Cms.Core.Services;

namespace uMediaOps.Services;

public interface IUnusedMediaScanService
{
    Task<Guid> StartScanAsync(string userId, ScanProfile profile);
    Task<Models.ScanProgress?> GetProgressAsync(Guid scanId);
    Task<UnusedMediaScanResult?> GetResultsAsync(Guid scanId);
    Task<UnusedMediaScanResult?> GetLatestResultsAsync();
    Task CancelScanAsync(Guid scanId);
    Task<byte[]> CreateZipArchiveAsync(IEnumerable<int> mediaIds);
    Task WriteZipArchiveToStreamAsync(IEnumerable<int> mediaIds, Stream outputStream);
}

public class UnusedMediaScanService : IUnusedMediaScanService
{
    private readonly IMediaService _mediaService;
    private readonly IReferenceTrackingService _referenceTrackingService;
    private readonly IUnusedMediaScanResultRepository _repository;
    private readonly MediaFileManager _mediaFileManager;
    private readonly ILogger<UnusedMediaScanService> _logger;

    // Track active scans with their progress and cancellation tokens
    private readonly ConcurrentDictionary<Guid, Models.ScanProgress> _activeScans = new();
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _cancellationTokens = new();

    public UnusedMediaScanService(
        IMediaService mediaService,
        IReferenceTrackingService referenceTrackingService,
        IUnusedMediaScanResultRepository repository,
        MediaFileManager mediaFileManager,
        ILogger<UnusedMediaScanService> logger)
    {
        _mediaService = mediaService;
        _referenceTrackingService = referenceTrackingService;
        _repository = repository;
        _mediaFileManager = mediaFileManager;
        _logger = logger;
    }

    public async Task<Guid> StartScanAsync(string userId, ScanProfile profile)
    {
        var scanId = Guid.NewGuid();
        _logger.LogInformation("Starting unused media scan {ScanId} for user {UserId} (profile: {Profile})", scanId, userId, profile);

        // Initialize progress tracking
        var progress = new Models.ScanProgress
        {
            Processed = 0,
            Total = 0,
            Percentage = 0,
            IsComplete = false
        };
        _activeScans[scanId] = progress;

        // Create cancellation token
        var cts = new CancellationTokenSource();
        _cancellationTokens[scanId] = cts;

        // Start background scan task
        _ = Task.Run(async () => await ProcessScanAsync(scanId, userId, cts.Token, profile), cts.Token);

        return scanId;
    }

    private async Task ProcessScanAsync(Guid scanId, string userId, CancellationToken cancellationToken, ScanProfile profile)
    {
        var startTime = DateTime.UtcNow;
        
        var scanResult = new UnusedMediaScanResult
        {
            Id = scanId,
            ScannedAt = startTime,
            ScannedBy = userId,
            IsComplete = false,
            Profile = profile
        };

        try
        {
            _logger.LogInformation("Processing scan {ScanId} with profile {Profile}", scanId, profile);

            // Load media in pages to avoid loading entire library into memory at once
            var pageSize = 1000;
            var pageIndex = 0;
            var mediaList = new List<Umbraco.Cms.Core.Models.IMedia>();
            long totalRecords;

            do
            {
                var page = _mediaService.GetPagedDescendants(-1, pageIndex, pageSize, out totalRecords);
                var filtered = page.Where(m => !m.Trashed && m.ContentType.Alias != "Folder");
                mediaList.AddRange(filtered);
                pageIndex++;
            } while ((long)pageIndex * pageSize < totalRecords);

            scanResult.TotalScanned = mediaList.Count;

            // Calculate total items to scan
            int totalItemsToScan = mediaList.Count;

            _logger.LogInformation("Scan {ScanId}: Using scan profile {Profile}", scanId, profile);

            // Update progress with total count
            if (_activeScans.TryGetValue(scanId, out var progress))
            {
                progress.Total = totalItemsToScan;
                progress.Percentage = 0;
            }

            _logger.LogInformation("Scan {ScanId}: Found {Count} media items to process (profile: {Profile})", 
                scanId, mediaList.Count, profile);

            var unusedItems = new List<UnusedMediaItem>();
            long totalStorageWasted = 0;
            
            // OPTIMIZED: Scan all files ONCE and get a set of referenced media IDs
            _logger.LogInformation("Scan {ScanId}: Starting optimized bulk file scan", scanId);
            var (referencedMediaIds, scanStats) = await _referenceTrackingService.GetAllReferencedMediaIdsAsync(
                mediaList, 
                profile, 
                cancellationToken);
            
            _logger.LogInformation("Scan {ScanId}: Bulk scan complete. Found {Count} referenced media items", 
                scanId, referencedMediaIds.Count);

            // Now check each media item against the referenced set (O(1) lookup)
            for (int i = 0; i < mediaList.Count; i++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogInformation("Scan {ScanId} cancelled", scanId);
                    return;
                }

                var media = mediaList[i];

                try
                {
                    // Check if this media ID is in the referenced set
                    if (!referencedMediaIds.Contains(media.Id))
                    {
                        // Media is unused - extract metadata
                        var unusedItem = await CreateUnusedMediaItemAsync(scanId, media);
                        if (unusedItem != null)
                        {
                            unusedItems.Add(unusedItem);
                            totalStorageWasted += unusedItem.FileSize;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error processing media {MediaId} in scan {ScanId}", media.Id, scanId);
                }

                // Update progress
                if (_activeScans.TryGetValue(scanId, out var currentProgress))
                {
                    currentProgress.Processed++;
                    currentProgress.Percentage = currentProgress.Total > 0
                        ? (currentProgress.Processed * 100) / currentProgress.Total
                        : 0;
                }
            }

            _logger.LogInformation("Scan {ScanId}: Processed all {Count} media items", scanId, mediaList.Count);

            // Finalize scan result
            scanResult.UnusedCount = unusedItems.Count;
            scanResult.TotalStorageWasted = totalStorageWasted;
            scanResult.UnusedItems = unusedItems;
            scanResult.IsComplete = true;
            scanResult.Profile = profile;
            scanResult.IncludedTemplates = profile != ScanProfile.Quick; // For backward compatibility
            
            // Store scan statistics
            if (scanStats != null)
            {
                scanResult.ContentItemsScanned = scanStats.ContentItemsScanned;
                scanResult.TemplatesScanned = scanStats.TemplatesScanned;
                scanResult.PartialViewsScanned = scanStats.PartialViewsScanned;
                scanResult.ViewFilesScanned = scanStats.ViewFilesScanned;
                scanResult.JavaScriptFilesScanned = scanStats.JavaScriptFilesScanned;
                scanResult.CssFilesScanned = scanStats.CssFilesScanned;
                scanResult.TypeScriptFilesScanned = scanStats.TypeScriptFilesScanned;
                scanResult.ScssFilesScanned = scanStats.ScssFilesScanned;
                scanResult.WwwrootFilesScanned = scanStats.WwwrootFilesScanned;
                scanResult.ConfigFilesScanned = scanStats.ConfigFilesScanned;
            }

            // Calculate scan duration
            var endTime = DateTime.UtcNow;
            scanResult.DurationSeconds = (int)(endTime - startTime).TotalSeconds;

            // Save to database
            await _repository.SaveAsync(scanResult);

            // Mark progress as complete
            if (_activeScans.TryGetValue(scanId, out var finalProgress))
            {
                finalProgress.IsComplete = true;
            }

            _logger.LogInformation("Scan {ScanId} completed in {Duration}s: Found {UnusedCount} unused items, {StorageWasted} bytes wasted",
                scanId, scanResult.DurationSeconds, unusedItems.Count, totalStorageWasted);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during scan {ScanId}", scanId);
            
            // Mark scan as incomplete but save what we have
            scanResult.IsComplete = false;
            try
            {
                await _repository.SaveAsync(scanResult);
            }
            catch (Exception saveEx)
            {
                _logger.LogError(saveEx, "Failed to save incomplete scan results for {ScanId}", scanId);
            }
        }
        finally
        {
            // Cleanup cancellation token
            if (_cancellationTokens.TryRemove(scanId, out var cts))
            {
                cts.Dispose();
            }
            
            // Keep progress for a while so clients can retrieve final status
            _ = Task.Delay(TimeSpan.FromMinutes(5)).ContinueWith(_ =>
            {
                _activeScans.TryRemove(scanId, out var _);
            });
        }
    }

    private async Task<UnusedMediaItem?> CreateUnusedMediaItemAsync(Guid scanId, Umbraco.Cms.Core.Models.IMedia media)
    {
        try
        {
            var item = new UnusedMediaItem
            {
                ScanId = scanId,
                MediaId = media.Id,
                FileName = media.Name ?? string.Empty,
                UploadDate = media.CreateDate,
                FileType = media.ContentType.Alias,
                HasWarning = false
            };

            // Extract file information
            if (media.HasProperty("umbracoFile") && media.GetValue("umbracoFile") != null)
            {
                var fileValue = media.GetValue("umbracoFile");
                string? filePath = null;

                // Handle both string and JSON formats
                if (fileValue is string strValue)
                {
                    if (strValue.StartsWith("{"))
                    {
                        try
                        {
                            using var json = System.Text.Json.JsonDocument.Parse(strValue);
                            if (json.RootElement.TryGetProperty("src", out var srcElement))
                            {
                                filePath = srcElement.GetString();
                            }
                        }
                        catch
                        {
                            filePath = strValue;
                        }
                    }
                    else
                    {
                        filePath = strValue;
                    }
                }

                if (!string.IsNullOrEmpty(filePath))
                {
                    item.FilePath = filePath;

                    // Get file size
                    var fileSystem = _mediaFileManager.FileSystem;
                    if (fileSystem.FileExists(filePath))
                    {
                        item.FileSize = fileSystem.GetSize(filePath);
                    }
                    else
                    {
                        _logger.LogWarning("File not found for media {MediaId}: {FilePath}", media.Id, filePath);
                        item.FileSize = 0;
                        item.HasWarning = true;
                        item.WarningMessage = "File not found on disk";
                    }

                    // Extract folder path
                    var lastSlash = filePath.LastIndexOf('/');
                    if (lastSlash > 0)
                    {
                        item.FolderPath = filePath.Substring(0, lastSlash);
                    }
                }
            }
            else
            {
                // Media item has no file
                item.FilePath = string.Empty;
                item.FolderPath = string.Empty;
                item.FileSize = 0;
                item.HasWarning = true;
                item.WarningMessage = "No file associated with this media item";
            }

            return item;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating unused media item for media {MediaId}", media.Id);
            return null;
        }
    }

    public async Task<Models.ScanProgress?> GetProgressAsync(Guid scanId)
    {
        // Check active scans first
        if (_activeScans.TryGetValue(scanId, out var progress))
        {
            return progress;
        }

        // Check if scan is complete in database
        var result = await _repository.GetByIdAsync(scanId);
        if (result != null)
        {
            return new Models.ScanProgress
            {
                Processed = result.TotalScanned,
                Total = result.TotalScanned,
                Percentage = 100,
                IsComplete = result.IsComplete
            };
        }

        return null;
    }

    public async Task<UnusedMediaScanResult?> GetResultsAsync(Guid scanId)
    {
        return await _repository.GetByIdAsync(scanId);
    }

    public async Task<UnusedMediaScanResult?> GetLatestResultsAsync()
    {
        return await _repository.GetLatestAsync();
    }

    public async Task CancelScanAsync(Guid scanId)
    {
        _logger.LogInformation("Cancelling scan {ScanId}", scanId);

        // Cancel the scan
        if (_cancellationTokens.TryRemove(scanId, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
        }

        // Remove from active scans
        _activeScans.TryRemove(scanId, out _);

        // Clean up partial results from database
        try
        {
            var result = await _repository.GetByIdAsync(scanId);
            if (result != null && !result.IsComplete)
            {
                await _repository.DeleteOldResultsAsync(DateTime.UtcNow.AddMinutes(1));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error cleaning up cancelled scan {ScanId}", scanId);
        }
    }

    public async Task<byte[]> CreateZipArchiveAsync(IEnumerable<int> mediaIds)
    {
        _logger.LogInformation("Creating ZIP archive for {Count} media items", mediaIds.Count());

        using var memoryStream = new MemoryStream();
        using (var archive = new System.IO.Compression.ZipArchive(memoryStream, System.IO.Compression.ZipArchiveMode.Create, true))
        {
            foreach (var mediaId in mediaIds)
            {
                try
                {
                    var media = _mediaService.GetById(mediaId);
                    if (media == null)
                    {
                        _logger.LogWarning("Media item {MediaId} not found", mediaId);
                        continue;
                    }

                    // Extract file path
                    string? filePath = null;
                    if (media.HasProperty("umbracoFile") && media.GetValue("umbracoFile") != null)
                    {
                        var fileValue = media.GetValue("umbracoFile");

                        // Handle both string and JSON formats
                        if (fileValue is string strValue)
                        {
                            if (strValue.StartsWith("{"))
                            {
                                try
                                {
                                    using var json = System.Text.Json.JsonDocument.Parse(strValue);
                                    if (json.RootElement.TryGetProperty("src", out var srcElement))
                                    {
                                        filePath = srcElement.GetString();
                                    }
                                }
                                catch
                                {
                                    filePath = strValue;
                                }
                            }
                            else
                            {
                                filePath = strValue;
                            }
                        }
                    }

                    if (string.IsNullOrEmpty(filePath))
                    {
                        _logger.LogWarning("File path not found for media {MediaId}", mediaId);
                        continue;
                    }

                    var fileSystem = _mediaFileManager.FileSystem;
                    if (!fileSystem.FileExists(filePath))
                    {
                        _logger.LogWarning("Physical file not found: {FilePath}", filePath);
                        continue;
                    }

                    // Create a safe entry name (preserve folder structure)
                    var entryName = filePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
                    
                    var entry = archive.CreateEntry(entryName);
                    using var entryStream = entry.Open();
                    using var fileStream = fileSystem.OpenFile(filePath);
                    await fileStream.CopyToAsync(entryStream);

                    _logger.LogDebug("Added {FileName} to ZIP archive", entryName);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error adding media {MediaId} to ZIP archive", mediaId);
                }
            }
        }

        memoryStream.Position = 0;
        return memoryStream.ToArray();
    }

    public async Task WriteZipArchiveToStreamAsync(IEnumerable<int> mediaIds, Stream outputStream)
    {
        _logger.LogInformation("Streaming ZIP archive for {Count} media items", mediaIds.Count());

        using var archive = new System.IO.Compression.ZipArchive(outputStream, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true);
        foreach (var mediaId in mediaIds)
        {
            try
            {
                var media = _mediaService.GetById(mediaId);
                if (media == null) continue;

                string? filePath = null;
                if (media.HasProperty("umbracoFile") && media.GetValue("umbracoFile") != null)
                {
                    var fileValue = media.GetValue("umbracoFile");
                    if (fileValue is string strValue)
                    {
                        if (strValue.StartsWith("{"))
                        {
                            try
                            {
                                using var json = System.Text.Json.JsonDocument.Parse(strValue);
                                if (json.RootElement.TryGetProperty("src", out var srcElement))
                                    filePath = srcElement.GetString();
                            }
                            catch { filePath = strValue; }
                        }
                        else
                        {
                            filePath = strValue;
                        }
                    }
                }

                if (string.IsNullOrEmpty(filePath)) continue;

                var fileSystem = _mediaFileManager.FileSystem;
                if (!fileSystem.FileExists(filePath)) continue;

                var entryName = filePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
                var entry = archive.CreateEntry(entryName);
                using var entryStream = entry.Open();
                using var fileStream = fileSystem.OpenFile(filePath);
                await fileStream.CopyToAsync(entryStream);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding media {MediaId} to ZIP stream", mediaId);
            }
        }
    }
}
