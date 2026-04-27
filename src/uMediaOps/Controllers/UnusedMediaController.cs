using uMediaOps.Models;
using uMediaOps.Services;
using System.IO.Compression;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Api.Management.Controllers;
using Umbraco.Cms.Api.Management.Routing;
using Umbraco.Cms.Core.Security;
using Umbraco.Cms.Core.Services;

namespace uMediaOps.Controllers;

[VersionedApiBackOfficeRoute("umediaops/unused")]
[ApiExplorerSettings(GroupName = "uMediaOps - Unused Media")]
[Authorize(Policy = "BackOfficeAccess")]
public class UnusedMediaController : ManagementApiControllerBase
{
    private readonly IMediaService _mediaService;
    private readonly IBackOfficeSecurityAccessor _backOfficeSecurityAccessor;
    private readonly ILogger<UnusedMediaController> _logger;
    private readonly ICacheService _cacheService;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IUnusedMediaScanService _scanService;
    private readonly IAnalyticsService _analyticsService;

    public UnusedMediaController(
        IMediaService mediaService,
        IBackOfficeSecurityAccessor backOfficeSecurityAccessor,
        ILogger<UnusedMediaController> logger,
        ICacheService cacheService,
        IServiceScopeFactory scopeFactory,
        IUnusedMediaScanService scanService,
        IAnalyticsService analyticsService)
    {
        _mediaService = mediaService;
        _backOfficeSecurityAccessor = backOfficeSecurityAccessor;
        _logger = logger;
        _cacheService = cacheService;
        _scopeFactory = scopeFactory;
        _scanService = scanService;
        _analyticsService = analyticsService;
    }

    /// <summary>
    /// Start a scan for unused media
    /// </summary>
    /// <param name="scanProfile">The scan profile to use (Quick, Deep, or Complete)</param>
    [HttpPost("start-scan")]
    public async Task<IActionResult> StartScan(
        [FromQuery] ScanProfile scanProfile = ScanProfile.Quick)
    {
        // Atomic check-and-set to prevent concurrent scan starts
        if (!_cacheService.TrySetIfAbsent("umediaops:unused:is-scanning", true))
        {
            return BadRequest(new { message = "A scan is already in progress" });
        }

        // Rate limit: prevent scan starts within 10 seconds of each other
        var lastScanStart = _cacheService.Get<DateTime?>("umediaops:unused:last-scan-start");
        if (lastScanStart.HasValue && (DateTime.UtcNow - lastScanStart.Value).TotalSeconds < 10)
        {
            _cacheService.Remove("umediaops:unused:is-scanning");
            return StatusCode(429, new { message = "Please wait before starting another scan" });
        }

        _cacheService.Set("umediaops:unused:last-scan-start", DateTime.UtcNow, TimeSpan.FromMinutes(1));
        _cacheService.Set(CacheKeys.UnusedMediaScanProgress, new Models.ScanProgress { Processed = 0, Total = 0, Percentage = 0, IsComplete = false });
        _cacheService.Remove(CacheKeys.UnusedMediaScanResult);

        // Get current user
        var currentUser = _backOfficeSecurityAccessor.BackOfficeSecurity?.CurrentUser;
        var userName = currentUser?.Name ?? currentUser?.Username ?? currentUser?.Email ?? "Unknown";

        // Start scan in background using a new DI scope
        _ = Task.Run(async () =>
        {
            using var scope = _scopeFactory.CreateScope();
            var scanService = scope.ServiceProvider.GetRequiredService<IUnusedMediaScanService>();

            try
            {
                _logger.LogInformation("Starting unused media scan for user {UserName} (profile: {Profile})", userName, scanProfile);
                
                // Start the actual scan with profile
                var scanId = await scanService.StartScanAsync(userName, scanProfile);
                _cacheService.Set("umediaops:unused:scan-id", scanId);
                
                // Poll for progress until complete
                var stillScanning = true;
                while (stillScanning)
                {
                    var progress = await scanService.GetProgressAsync(scanId);
                    if (progress != null)
                    {
                        _cacheService.Set(CacheKeys.UnusedMediaScanProgress, progress);
                        
                        if (progress.IsComplete)
                        {
                            // Get the final results
                            var scanResult = await scanService.GetResultsAsync(scanId);
                            if (scanResult != null)
                            {
                                // Update the scannedAt to current time to show accurate "time ago"
                                scanResult.ScannedAt = DateTime.UtcNow;
                                _cacheService.Set(CacheKeys.UnusedMediaScanResult, scanResult);
                            }
                            stillScanning = false;
                            
                            _logger.LogInformation("Unused media scan completed. Found {UnusedCount} unused items out of {TotalScanned} scanned", 
                                scanResult?.UnusedCount ?? 0, scanResult?.TotalScanned ?? 0);
                            break;
                        }
                    }
                    
                    await Task.Delay(500); // Poll every 500ms
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during unused media scan");
                _cacheService.Set(CacheKeys.UnusedMediaScanProgress, new Models.ScanProgress
                {
                    Processed = 0,
                    Total = 0,
                    Percentage = 0,
                    IsComplete = true
                });
            }
            finally
            {
                _cacheService.Remove("umediaops:unused:is-scanning");
            }
        });

        return Ok(new { message = "Scan started", isScanning = true });
    }

    /// <summary>
    /// Get the progress of a scan
    /// </summary>
    [HttpGet("scan/progress")]
    public async Task<IActionResult> GetProgress()
    {
        var isScanning = _cacheService.Get<bool>("umediaops:unused:is-scanning");
        var scanId = _cacheService.Get<Guid?>("umediaops:unused:scan-id");
        
        if (scanId.HasValue)
        {
            var progress = await _scanService.GetProgressAsync(scanId.Value);
            if (progress != null)
            {
                _cacheService.Set(CacheKeys.UnusedMediaScanProgress, progress);
                return Ok(new { isScanning, progress });
            }
        }

        var cachedProgress = _cacheService.Get<Models.ScanProgress>(CacheKeys.UnusedMediaScanProgress) ?? new Models.ScanProgress();
        return Ok(new { isScanning, progress = cachedProgress });
    }

    /// <summary>
    /// Cancel an ongoing scan
    /// </summary>
    [HttpPost("scan/cancel")]
    public async Task<IActionResult> CancelScan()
    {
        try
        {
            var scanId = _cacheService.Get<Guid?>("umediaops:unused:scan-id");
            if (!scanId.HasValue)
            {
                return BadRequest(new { message = "No active scan to cancel" });
            }

            await _scanService.CancelScanAsync(scanId.Value);
            
            // Clear scanning state
            _cacheService.Remove("umediaops:unused:is-scanning");
            _cacheService.Remove("umediaops:unused:scan-id");
            
            _logger.LogInformation("Scan {ScanId} cancelled by user", scanId.Value);
            
            return Ok(new { message = "Scan cancelled successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cancelling scan");
            return StatusCode(500, new { message = "Failed to cancel scan" });
        }
    }

    /// <summary>
    /// Get the results of a scan
    /// </summary>
    /// <param name="fileType">Optional file type filter</param>
    /// <param name="sortBy">Optional sort field (size, date, name)</param>
    /// <param name="pageNumber">Page number (default: 1)</param>
    /// <param name="pageSize">Page size (default: 50, max: 500)</param>
    [HttpGet("scan/results")]
    public async Task<IActionResult> GetResults(
        [FromQuery] string? fileType = null,
        [FromQuery] string? search = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 50)
    {
        try
        {
            // Validate pagination parameters
            if (pageNumber < 1) pageNumber = 1;
            if (pageSize < 1 || pageSize > 500) pageSize = 50;

            // First check if we have cached results
            UnusedMediaScanResult? scanResult = _cacheService.Get<UnusedMediaScanResult>(CacheKeys.UnusedMediaScanResult);

            // If no cached results, try to load the latest from database
            if (scanResult == null)
            {
                scanResult = await _scanService.GetLatestResultsAsync();
                if (scanResult != null)
                {
                    _cacheService.Set(CacheKeys.UnusedMediaScanResult, scanResult);
                }
            }

            if (scanResult == null)
            {
                return NotFound(new { message = "No scan results available" });
            }

            // Apply filters
            var items = scanResult.UnusedItems ?? new List<UnusedMediaItem>();
            
            // Apply file type filter
            if (!string.IsNullOrEmpty(fileType))
            {
                var filterLower = fileType.ToLower();
                items = items.Where(i => 
                    i.FileType.Contains(filterLower, StringComparison.OrdinalIgnoreCase) ||
                    GetFileExtension(i.FileName).Contains(filterLower, StringComparison.OrdinalIgnoreCase)
                ).ToList();
            }

            // Apply search filter (filename)
            if (!string.IsNullOrEmpty(search))
            {
                items = items.Where(i =>
                    i.FileName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    i.FolderPath.Contains(search, StringComparison.OrdinalIgnoreCase)
                ).ToList();
            }
            
            // Apply sorting
            if (!string.IsNullOrEmpty(sortBy))
            {
                items = sortBy.ToLower() switch
                {
                    "size" => items.OrderByDescending(i => i.FileSize).ToList(),
                    "date" => items.OrderByDescending(i => i.UploadDate).ToList(),
                    "name" => items.OrderBy(i => i.FileName).ToList(),
                    _ => items
                };
            }
            else
            {
                // Default sort by size (largest first)
                items = items.OrderByDescending(i => i.FileSize).ToList();
            }

            // Apply pagination
            var totalItems = items.Count;
            var totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);
            var skip = (pageNumber - 1) * pageSize;
            var pagedItems = items.Skip(skip).Take(pageSize).ToList();

            // Return paginated result
            return Ok(new
            {
                scanId = scanResult.Id,
                scannedAt = scanResult.ScannedAt,
                totalScanned = scanResult.TotalScanned,
                unusedCount = totalItems, // Use filtered count
                totalSize = items.Sum(i => i.FileSize), // Use filtered total
                items = pagedItems,
                totalItems,
                totalPages,
                currentPage = pageNumber,
                pageSize,
                hasPreviousPage = pageNumber > 1,
                hasNextPage = pageNumber < totalPages,
                // Scan statistics - all file types
                profile = scanResult.Profile,
                profileName = scanResult.ProfileName,
                durationSeconds = scanResult.DurationSeconds,
                itemsWithCodeReferences = scanResult.ItemsWithCodeReferences,
                includedTemplates = scanResult.IncludedTemplates,
                contentItemsScanned = scanResult.ContentItemsScanned,
                viewFilesScanned = scanResult.ViewFilesScanned,
                templatesScanned = scanResult.TemplatesScanned,
                partialViewsScanned = scanResult.PartialViewsScanned,
                blockComponentsScanned = scanResult.BlockComponentsScanned,
                layoutsScanned = scanResult.LayoutsScanned,
                javascriptFilesScanned = scanResult.JavaScriptFilesScanned,
                cssFilesScanned = scanResult.CssFilesScanned,
                typescriptFilesScanned = scanResult.TypeScriptFilesScanned,
                scssFilesScanned = scanResult.ScssFilesScanned,
                configFilesScanned = scanResult.ConfigFilesScanned,
                wwwrootFilesScanned = scanResult.WwwrootFilesScanned
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading scan results");
            return StatusCode(500, new { message = "Error loading results" });
        }
    }
    
    private string GetFileExtension(string fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return string.Empty;
        var parts = fileName.Split('.');
        if (parts.Length < 2) return string.Empty;
        return parts[^1];
    }

    /// <summary>
    /// Clear the cached scan results
    /// </summary>
    [HttpPost("clear-results")]
    public IActionResult ClearResults()
    {
        _cacheService.RemoveByPattern("umediaops:unused");
        return Ok(new { message = "Scan results cleared" });
    }

    /// <summary>
    /// Export unused media scan results as CSV
    /// </summary>
    [HttpGet("export")]
    public IActionResult ExportCsv()
    {
        var scanResult = _cacheService.Get<UnusedMediaScanResult>(CacheKeys.UnusedMediaScanResult);
        if (scanResult?.UnusedItems == null || !scanResult.UnusedItems.Any())
        {
            return NotFound(new { message = "No scan results to export" });
        }

        var csv = new System.Text.StringBuilder();
        csv.AppendLine("Media ID,File Name,File Type,File Size (Bytes),File Size,Upload Date,Folder Path,Has Warning,Warning Message");

        foreach (var item in scanResult.UnusedItems)
        {
            csv.AppendLine(string.Join(",",
                item.MediaId,
                EscapeCsv(item.FileName),
                EscapeCsv(item.FileType),
                item.FileSize,
                EscapeCsv(FormatBytes(item.FileSize)),
                EscapeCsv(item.UploadDate.ToString("yyyy-MM-dd HH:mm:ss")),
                EscapeCsv(item.FolderPath),
                item.HasWarning,
                EscapeCsv(item.WarningMessage ?? "")
            ));
        }

        var bytes = System.Text.Encoding.UTF8.GetPreamble().Concat(System.Text.Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
        return File(bytes, "text/csv", $"unused-media-{DateTime.UtcNow:yyyy-MM-dd}.csv");
    }

    /// <summary>
    /// Download selected unused media items as a ZIP archive
    /// </summary>
    [HttpPost("download")]
    public IActionResult DownloadZip([FromBody] DeleteMediaRequest request)
    {
        if (request?.MediaIds == null || !request.MediaIds.Any())
        {
            return BadRequest(new { message = "No media IDs provided" });
        }

        try
        {
            // Write to a temp file instead of MemoryStream to handle large media libraries
            var tempPath = Path.Combine(Path.GetTempPath(), $"umediaops-download-{Guid.NewGuid():N}.zip");

            try
            {
                using (var archive = ZipFile.Open(tempPath, ZipArchiveMode.Create))
                {
                    var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    foreach (var mediaId in request.MediaIds)
                    {
                        var media = _mediaService.GetById(mediaId);
                        if (media == null) continue;

                        var filePath = media.GetValue<string>("umbracoFile");
                        if (string.IsNullOrEmpty(filePath)) continue;

                        // Parse JSON file path if needed
                        if (filePath.StartsWith("{"))
                        {
                            try
                            {
                                using var doc = System.Text.Json.JsonDocument.Parse(filePath);
                                if (doc.RootElement.TryGetProperty("src", out var src))
                                    filePath = src.GetString();
                            }
                            catch { continue; }
                        }

                        if (string.IsNullOrEmpty(filePath)) continue;

                        var physicalPath = Path.Combine(
                            Directory.GetCurrentDirectory(), "wwwroot",
                            filePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));

                        if (!System.IO.File.Exists(physicalPath)) continue;

                        // Build unique entry name
                        var entryName = media.Name ?? $"media-{mediaId}";
                        var ext = Path.GetExtension(physicalPath);
                        if (!string.IsNullOrEmpty(ext) && !entryName.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                            entryName += ext;

                        // Deduplicate names
                        var baseName = entryName;
                        var counter = 1;
                        while (usedNames.Contains(entryName))
                        {
                            var nameWithoutExt = Path.GetFileNameWithoutExtension(baseName);
                            entryName = $"{nameWithoutExt} ({counter}){ext}";
                            counter++;
                        }
                        usedNames.Add(entryName);

                        archive.CreateEntryFromFile(physicalPath, entryName, CompressionLevel.Optimal);
                    }
                }

                // Stream the temp file to the client, then delete it
                var stream = new FileStream(tempPath, FileMode.Open, FileAccess.Read, FileShare.None, 81920, FileOptions.DeleteOnClose);
                return File(stream, "application/zip", $"unused-media-backup-{DateTime.UtcNow:yyyy-MM-dd}.zip");
            }
            catch
            {
                // Clean up temp file on error
                if (System.IO.File.Exists(tempPath))
                    System.IO.File.Delete(tempPath);
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating download ZIP");
            return StatusCode(500, new { message = "Failed to create download" });
        }
    }

    private static string EscapeCsv(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var safe = value;
        if (safe.Length > 0 && "=+-@\t\r".Contains(safe[0]))
            safe = "'" + safe;
        if (safe.Contains(',') || safe.Contains('"') || safe.Contains('\n'))
            return "\"" + safe.Replace("\"", "\"\"") + "\"";
        return safe;
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes == 0) return "0 Bytes";
        var k = 1024.0;
        string[] sizes = { "Bytes", "KB", "MB", "GB" };
        var i = (int)Math.Floor(Math.Log(bytes) / Math.Log(k));
        return Math.Round(bytes / Math.Pow(k, i), 2) + " " + sizes[i];
    }

    /// <summary>
    /// Delete selected unused media items (move to recycle bin)
    /// </summary>
    [HttpPost("delete")]
    public async Task<IActionResult> DeleteUnusedMedia([FromBody] DeleteMediaRequest request)
    {
        if (request?.MediaIds == null || !request.MediaIds.Any())
        {
            return BadRequest(new { message = "No media IDs provided" });
        }

        var currentUser = _backOfficeSecurityAccessor.BackOfficeSecurity?.CurrentUser;
        var userName = currentUser?.Name ?? currentUser?.Username ?? currentUser?.Email ?? "Unknown";

        int successCount = 0;
        int failureCount = 0;
        long spaceFreed = 0;
        var errors = new List<string>();

        foreach (var mediaId in request.MediaIds)
        {
            try
            {
                var media = _mediaService.GetById(mediaId);
                if (media != null)
                {
                    // Check if already trashed (another user may have deleted it)
                    if (media.Trashed)
                    {
                        failureCount++;
                        errors.Add($"Media {mediaId} is already in the recycle bin");
                        continue;
                    }

                    // Get file size before deletion for analytics
                    var fileSize = media.GetValue<long>("umbracoBytes");
                    
                    _mediaService.MoveToRecycleBin(media);
                    successCount++;
                    spaceFreed += fileSize;
                }
                else
                {
                    failureCount++;
                    errors.Add($"Media {mediaId} not found");
                }
            }
            catch (Exception ex)
            {
                failureCount++;
                errors.Add($"Failed to delete media {mediaId}");
                _logger.LogError(ex, "Failed to delete media {mediaId}", mediaId);
            }
        }

        // Record deletion in analytics for tracking
        if (successCount > 0)
        {
            await _analyticsService.RecordDeletionAsync(
                successCount,
                spaceFreed,
                $"UnusedMedia deleted by {userName}"
            );
        }

        // Update the cached scan results to remove deleted items
        var cachedResult = _cacheService.Get<UnusedMediaScanResult>(CacheKeys.UnusedMediaScanResult);
        if (cachedResult != null && cachedResult.UnusedItems != null && successCount > 0)
        {
            var deletedIds = request.MediaIds.ToHashSet();
            cachedResult.UnusedItems = cachedResult.UnusedItems
                .Where(item => !deletedIds.Contains(item.MediaId))
                .ToList();
            cachedResult.UnusedCount = cachedResult.UnusedItems.Count;
            
            // Recalculate total storage wasted
            cachedResult.TotalStorageWasted = cachedResult.UnusedItems.Sum(item => item.FileSize);
            
            // Update cache
            _cacheService.Set(CacheKeys.UnusedMediaScanResult, cachedResult);
        }

        return Ok(new
        {
            successCount,
            failureCount,
            spaceFreed,
            errors
        });
    }
}
