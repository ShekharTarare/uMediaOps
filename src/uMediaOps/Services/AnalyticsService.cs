using uMediaOps.Models;
using uMediaOps.Repositories;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using Umbraco.Cms.Core.Services;

namespace uMediaOps.Services;

/// <summary>
/// Service interface for analytics operations
/// </summary>
public interface IAnalyticsService
{
    Task<FileTypeBreakdown> GetFileTypeBreakdownAsync();
    Task<StorageSavingsHistory> GetStorageSavingsHistoryAsync();
    Task RecordScanResultAsync(ScanResult result);
    Task RecordDeletionAsync(int filesDeleted, long spaceFreed, string? metadata = null);
    Task<ScanResult?> GetLatestScanResultAsync();
}

/// <summary>
/// Service for analytics and reporting
/// </summary>
public class AnalyticsService : IAnalyticsService
{
    private readonly IAnalyticsRepository _analyticsRepository;
    private readonly IFileHashRepository _fileHashRepository;
    private readonly IMediaService _mediaService;
    private readonly ILogger<AnalyticsService> _logger;

    // Compiled regex for stripping Umbraco's duplicate naming suffix — e.g. " (1)", " (2)"
    private static readonly System.Text.RegularExpressions.Regex DuplicateSuffixRegex = new(
        @"\s*\(\d+\)",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    public AnalyticsService(
        IAnalyticsRepository analyticsRepository,
        IFileHashRepository fileHashRepository,
        IMediaService mediaService,
        ILogger<AnalyticsService> logger)
    {
        _analyticsRepository = analyticsRepository;
        _fileHashRepository = fileHashRepository;
        _mediaService = mediaService;
        _logger = logger;
    }

    /// <summary>
    /// Get file type breakdown statistics
    /// </summary>
    public async Task<FileTypeBreakdown> GetFileTypeBreakdownAsync()
    {
        try
        {
            var duplicateGroups = await _fileHashRepository.GetDuplicateGroupsAsync();
            var duplicateHashes = duplicateGroups.SelectMany(g => g).ToList();

            // Batch-load all media in a single call
            var mediaLookup = new Dictionary<int, Umbraco.Cms.Core.Models.IMedia>();
            var uniqueIds = duplicateHashes.Select(h => h.MediaId).Distinct().ToList();
            foreach (var media in _mediaService.GetByIds(uniqueIds))
            {
                mediaLookup[media.Id] = media;
            }

            var fileTypeStats = new Dictionary<string, (int count, long size)>();
            foreach (var hash in duplicateHashes)
            {
                if (mediaLookup.TryGetValue(hash.MediaId, out var media) && !string.IsNullOrEmpty(media.Name))
                {
                    var extension = GetFileExtension(media.Name);
                    
                    if (!fileTypeStats.TryGetValue(extension, out var current))
                    {
                        current = (0, 0);
                    }
                    fileTypeStats[extension] = (current.count + 1, current.size + hash.FileSize);
                }
            }

            var totalSize = fileTypeStats.Sum(x => x.Value.size);

            var statistics = fileTypeStats
                .Select(kvp => new FileTypeStatistic
                {
                    FileType = kvp.Key,
                    Count = kvp.Value.count,
                    TotalSize = kvp.Value.size,
                    Percentage = totalSize > 0 ? (double)kvp.Value.size / totalSize * 100 : 0
                })
                .OrderByDescending(x => x.TotalSize)
                .ToList();

            return new FileTypeBreakdown { Statistics = statistics };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting file type breakdown");
            return new FileTypeBreakdown();
        }
    }

    /// <summary>
    /// Get storage savings history
    /// </summary>
    public async Task<StorageSavingsHistory> GetStorageSavingsHistoryAsync()
    {
        try
        {
            // Only load last 12 months of data instead of all history
            var snapshots = await _analyticsRepository.GetSnapshotsAsync(DateTime.UtcNow.AddMonths(-12));

            var deletionEvents = snapshots
                .Where(s => s.EventType == "Deletion")
                .Select(s => new SavingsDataPoint
                {
                    Date = s.RecordedAt,
                    SpaceFreed = s.StorageFreed,
                    FilesDeleted = s.DuplicateCount // Reusing this field for files deleted
                })
                .OrderBy(x => x.Date)
                .ToList();

            var totalSaved = deletionEvents.Sum(x => x.SpaceFreed);

            return new StorageSavingsHistory
            {
                DataPoints = deletionEvents,
                TotalSaved = totalSaved
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting storage savings history");
            return new StorageSavingsHistory();
        }
    }

    /// <summary>
    /// Record a scan result for analytics
    /// </summary>
    public async Task RecordScanResultAsync(ScanResult result)
    {
        try
        {
            var record = new AnalyticsData
            {
                RecordedAt = result.ScannedAt,
                EventType = "Scan",
                DuplicateCount = result.TotalDuplicates,
                StorageWasted = result.StorageWasted,
                StorageFreed = 0,
                Metadata = JsonSerializer.Serialize(new
                {
                    result.TotalScanned,
                    result.DuplicateGroupsFound,
                    ErrorCount = result.Errors.Count
                })
            };

            await _analyticsRepository.SaveSnapshotAsync(record);
            _logger.LogInformation("Recorded scan result: {DuplicateCount} duplicates, {StorageWasted} bytes wasted", 
                result.TotalDuplicates, result.StorageWasted);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recording scan result");
        }
    }

    /// <summary>
    /// Record a deletion operation for analytics
    /// </summary>
    public async Task RecordDeletionAsync(int filesDeleted, long spaceFreed, string? metadata = null)
    {
        try
        {
            var record = new AnalyticsData
            {
                RecordedAt = DateTime.UtcNow,
                EventType = "Deletion",
                DuplicateCount = filesDeleted,
                StorageWasted = 0,
                StorageFreed = spaceFreed,
                Metadata = metadata
            };

            await _analyticsRepository.SaveSnapshotAsync(record);
            _logger.LogInformation("Recorded deletion: {FilesDeleted} files, {SpaceFreed} bytes freed", 
                filesDeleted, spaceFreed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recording deletion");
        }
    }

    public async Task<ScanResult?> GetLatestScanResultAsync()
    {
        try
        {
            // Use the repository's targeted method instead of loading all snapshots
            var latest = await _analyticsRepository.GetLatestSnapshotAsync();
            if (latest == null || latest.EventType != "Scan")
            {
                // If latest isn't a scan, search recent snapshots
                var recentSnapshots = await _analyticsRepository.GetSnapshotsAsync(DateTime.UtcNow.AddMonths(-12));
                latest = recentSnapshots.LastOrDefault(s => s.EventType == "Scan");
            }

            if (latest == null)
                return null;

            var result = new ScanResult
            {
                ScannedAt = latest.RecordedAt,
                TotalDuplicates = latest.DuplicateCount,
                StorageWasted = latest.StorageWasted,
            };

            // Parse metadata for TotalScanned and DuplicateGroupsFound
            if (!string.IsNullOrEmpty(latest.Metadata))
            {
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(latest.Metadata);
                    if (doc.RootElement.TryGetProperty("TotalScanned", out var ts))
                        result.TotalScanned = ts.GetInt32();
                    if (doc.RootElement.TryGetProperty("DuplicateGroupsFound", out var dg))
                        result.DuplicateGroupsFound = dg.GetInt32();
                }
                catch { /* ignore parse errors */ }
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting latest scan result from analytics");
            return null;
        }
    }

    private string GetFileExtension(string fileName)
    {
        // Remove Umbraco's duplicate naming suffix like " (1)", " (2)", etc.
        var cleanFileName = DuplicateSuffixRegex.Replace(fileName, "");
        
        var extension = Path.GetExtension(cleanFileName);
        return string.IsNullOrEmpty(extension) ? "unknown" : extension.TrimStart('.').ToLowerInvariant();
    }
}
