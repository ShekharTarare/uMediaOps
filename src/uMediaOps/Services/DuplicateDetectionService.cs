using uMediaOps.Models;
using uMediaOps.Repositories;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.IO;

namespace uMediaOps.Services;

/// <summary>
/// Result of a media library scan
/// </summary>
public class ScanResult
{
    public int TotalScanned { get; set; }
    public int DuplicateGroupsFound { get; set; }
    public int TotalDuplicates { get; set; }
    public long StorageWasted { get; set; }
    public DateTime ScannedAt { get; set; }
    public List<string> Errors { get; set; } = new();
}

/// <summary>
/// Duplicate group information
/// </summary>
public class DuplicateGroup
{
    public string Hash { get; set; } = string.Empty;
    public int Count { get; set; }
    public long TotalSize { get; set; }
    public List<DuplicateItem> Items { get; set; } = new();
}

/// <summary>
/// Individual duplicate item information
/// </summary>
public class DuplicateItem
{
    public int MediaId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string FileUrl { get; set; } = string.Empty;
    public string FileType { get; set; } = string.Empty;
    public string Extension { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public DateTime UploadDate { get; set; }
    public bool IsOriginal { get; set; }
    public bool IsManuallySelected { get; set; }
    public string UploaderName { get; set; } = string.Empty;
}

/// <summary>
/// Service for duplicate detection and management
/// </summary>
public interface IDuplicateDetectionService
{
    Task<ScanResult> ScanMediaLibraryAsync(IProgress<ScanProgress>? progress = null);
    Task<IEnumerable<DuplicateGroup>> GetDuplicateGroupsAsync(string? fileTypeFilter = null);
    Task<DuplicateGroup?> GetDuplicateGroupByHashAsync(string hash);
}

/// <summary>
/// Implementation of duplicate detection service
/// </summary>
public class DuplicateDetectionService : IDuplicateDetectionService
{
    private readonly IFileHashService _fileHashService;
    private readonly IFileHashRepository _repository;
    private readonly IMediaService _mediaService;
    private readonly MediaFileManager _mediaFileManager;
    private readonly IUserService _userService;
    private readonly ILogger<DuplicateDetectionService> _logger;

    public DuplicateDetectionService(
        IFileHashService fileHashService,
        IFileHashRepository repository,
        IMediaService mediaService,
        MediaFileManager mediaFileManager,
        IUserService userService,
        ILogger<DuplicateDetectionService> logger)
    {
        _fileHashService = fileHashService;
        _repository = repository;
        _mediaService = mediaService;
        _mediaFileManager = mediaFileManager;
        _userService = userService;
        _logger = logger;
    }

    public async Task<ScanResult> ScanMediaLibraryAsync(IProgress<ScanProgress>? progress = null)
    {
        var result = new ScanResult();

        try
        {
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

            result.TotalScanned = mediaList.Count;
            
            _logger.LogInformation("Scanning {Count} media items for duplicates (excluding trashed and folders)", mediaList.Count);

            for (int i = 0; i < mediaList.Count; i++)
            {
                var media = mediaList[i];

                try
                {
                    // Only process media with files
                    if (media.HasProperty("umbracoFile") && media.GetValue("umbracoFile") != null)
                    {
                        var fileValue = media.GetValue("umbracoFile");
                        string? filePath = null;

                        // Handle both string and JSON formats
                        if (fileValue is string strValue)
                        {
                            // Try to parse as JSON first (Umbraco 17 format)
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
                            _logger.LogDebug("Processing media {MediaId} with file path: {FilePath}", media.Id, filePath);
                            
                            // Get file stream using MediaFileManager
                            var fileSystem = _mediaFileManager.FileSystem;
                            if (fileSystem.FileExists(filePath))
                            {
                                using var stream = fileSystem.OpenFile(filePath);
                                var fileSize = fileSystem.GetSize(filePath);

                                _logger.LogDebug("File size for media {MediaId}: {FileSize} bytes", media.Id, fileSize);

                                // Compute or retrieve hash
                                await _fileHashService.GetOrComputeHashAsync(media.Id, stream, fileSize);
                            }
                            else
                            {
                                _logger.LogWarning("File not found for media {MediaId}: {FilePath}", media.Id, filePath);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error processing media {MediaId}", media.Id);
                    result.Errors.Add($"Failed to process media {media.Id}");
                }

                // Report progress
                progress?.Report(new ScanProgress
                {
                    Processed = i + 1,
                    Total = mediaList.Count
                });
            }

            // Clean up orphaned hashes for deleted media
            var scannedMediaIds = mediaList.Select(m => m.Id);
            var orphanedCount = await _repository.DeleteOrphanedAsync(scannedMediaIds);
            if (orphanedCount > 0)
            {
                _logger.LogInformation("Cleaned up {Count} orphaned file hash records", orphanedCount);
            }

            // Get duplicate statistics — use the pre-loaded media lookup
            var duplicateGroups = await _repository.GetDuplicateGroupsAsync();
            var validGroupCount = 0;
            var totalDuplicates = 0;
            long storageWasted = 0;
            
            // Batch-load media for trashed check using single query
            var allDupMediaIds = duplicateGroups.SelectMany(g => g.Select(h => h.MediaId)).Distinct().ToList();
            var validMedia = _mediaService.GetByIds(allDupMediaIds);
            var validMediaIds = new HashSet<int>(validMedia.Where(m => !m.Trashed).Select(m => m.Id));

            foreach (var group in duplicateGroups)
            {
                var validCount = group.Count(h => validMediaIds.Contains(h.MediaId));
                
                if (validCount > 1)
                {
                    validGroupCount++;
                    totalDuplicates += validCount - 1;
                    storageWasted += group.First().FileSize * (validCount - 1);
                }
            }

            result.DuplicateGroupsFound = validGroupCount;
            result.TotalDuplicates = totalDuplicates;
            result.StorageWasted = storageWasted;
            result.ScannedAt = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during media library scan");
            result.Errors.Add("An error occurred during the scan");
        }

        return result;
    }

    public async Task<IEnumerable<DuplicateGroup>> GetDuplicateGroupsAsync(string? fileTypeFilter = null)
    {
        try
        {
            var duplicateGroups = await _repository.GetDuplicateGroupsAsync();
            var groupsList = duplicateGroups.ToList();
            var result = new List<DuplicateGroup>();

            // Batch-load all media items in a single call instead of N individual GetById calls
            var allMediaIds = groupsList.SelectMany(g => g.Select(h => h.MediaId)).Distinct().ToList();
            var allMedia = _mediaService.GetByIds(allMediaIds);
            var mediaLookup = allMedia
                .Where(m => !m.Trashed)
                .ToDictionary(m => m.Id);

            // Cache user lookups to avoid repeated calls
            var userCache = new Dictionary<int, string>();

            foreach (var group in groupsList)
            {
                var items = new List<DuplicateItem>();
                
                // Use pre-loaded media lookup
                var mediaItems = new List<(FileHash hash, Umbraco.Cms.Core.Models.IMedia media)>();
                
                foreach (var fileHash in group)
                {
                    if (mediaLookup.TryGetValue(fileHash.MediaId, out var media))
                    {
                        mediaItems.Add((fileHash, media));
                    }
                }
                
                // Sort by actual upload date (oldest first)
                var groupList = mediaItems.OrderBy(m => m.media.CreateDate).ToList();

                // Check if there's a manually selected original
                var manualOriginal = groupList.FirstOrDefault(m => m.hash.IsManuallySelectedOriginal);
                int? manualOriginalMediaId = null;
                
                if (manualOriginal.hash != null)
                {
                    manualOriginalMediaId = manualOriginal.media.Id;
                }

                for (int i = 0; i < groupList.Count; i++)
                {
                    var (fileHash, media) = groupList[i];

                    // Get file URL and type info
                    var fileUrl = string.Empty;
                    var fileType = media.ContentType.Alias;
                    var extension = string.Empty;

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
                                    {
                                        fileUrl = srcElement.GetString() ?? string.Empty;
                                    }
                                }
                                catch
                                {
                                    fileUrl = strValue;
                                }
                            }
                            else
                            {
                                fileUrl = strValue;
                            }

                            // Extract extension
                            if (!string.IsNullOrEmpty(fileUrl))
                            {
                                extension = System.IO.Path.GetExtension(fileUrl).TrimStart('.');
                            }
                        }
                    }

                    // Get the actual file path from the URL or use a friendly path
                    var displayPath = fileUrl;
                    if (!string.IsNullOrEmpty(fileUrl))
                    {
                        // Remove query parameters and make it more readable
                        displayPath = fileUrl.Split('?')[0];
                        // Remove leading slash if present
                        if (displayPath.StartsWith("/"))
                        {
                            displayPath = displayPath.Substring(1);
                        }
                    }
                    else
                    {
                        displayPath = $"Media/{media.Name}";
                    }

                    // Get uploader name from cache or user service
                    var uploaderName = "System";
                    
                    if (media.CreatorId != 0)
                    {
                        if (userCache.TryGetValue(media.CreatorId, out var cachedName))
                        {
                            uploaderName = cachedName;
                        }
                        else
                        {
                            try
                            {
                                var user = _userService.GetUserById(media.CreatorId);
                                if (user != null)
                                {
                                    uploaderName = !string.IsNullOrWhiteSpace(user.Name) ? user.Name :
                                                 !string.IsNullOrWhiteSpace(user.Username) ? user.Username :
                                                 !string.IsNullOrWhiteSpace(user.Email) ? user.Email :
                                                 $"User {media.CreatorId}";
                                }
                                else
                                {
                                    uploaderName = $"User {media.CreatorId}";
                                }
                                userCache[media.CreatorId] = uploaderName;
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Error getting user for CreatorId {CreatorId}", media.CreatorId);
                                uploaderName = $"User {media.CreatorId}";
                                userCache[media.CreatorId] = uploaderName;
                            }
                        }
                    }

                    var item = new DuplicateItem
                    {
                        MediaId = media.Id,
                        Name = media.Name ?? string.Empty,
                        Path = displayPath,
                        FileUrl = fileUrl,
                        FileType = fileType,
                        Extension = extension,
                        FileSize = fileHash.FileSize,
                        UploadDate = media.CreateDate,
                        IsOriginal = manualOriginalMediaId.HasValue 
                            ? media.Id == manualOriginalMediaId.Value  // Use manual selection if set
                            : i == 0,  // Otherwise, first (oldest) is original
                        IsManuallySelected = fileHash.IsManuallySelectedOriginal,
                        UploaderName = uploaderName
                    };

                    _logger.LogDebug("Created DuplicateItem: MediaId={MediaId}, FileUrl={FileUrl}, FileType={FileType}, Extension={Extension}", 
                        item.MediaId, item.FileUrl, item.FileType, item.Extension);

                    // Apply file type filter if specified
                    if (string.IsNullOrEmpty(fileTypeFilter) || 
                        media.ContentType.Alias.Contains(fileTypeFilter, StringComparison.OrdinalIgnoreCase))
                    {
                        items.Add(item);
                    }
                }

                if (items.Count > 1) // Only include if still has duplicates after filtering
                {
                    result.Add(new DuplicateGroup
                    {
                        Hash = group.Key,
                        Count = items.Count,
                        TotalSize = items.Sum(i => i.FileSize),
                        Items = items
                    });
                }
            }

            // Sort by storage impact (largest first)
            return result.OrderByDescending(g => g.TotalSize);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting duplicate groups");
            throw;
        }
    }

    public async Task<DuplicateGroup?> GetDuplicateGroupByHashAsync(string hash)
    {
        try
        {
            // Query only the specific hash group from the repository instead of loading all groups
            var fileHashes = await _repository.GetByHashAsync(hash);
            var hashList = fileHashes.ToList();

            if (hashList.Count < 2)
                return null; // Not a duplicate group

            // Batch-load all media for this hash group in a single call
            var mediaIds = hashList.Select(fh => fh.MediaId).ToList();
            var allMedia = _mediaService.GetByIds(mediaIds);
            var mediaLookup = allMedia
                .Where(m => !m.Trashed)
                .ToDictionary(m => m.Id);

            if (mediaLookup.Count < 2)
                return null;

            var userCache = new Dictionary<int, string>();
            var items = new List<DuplicateItem>();
            var mediaItems = hashList
                .Where(fh => mediaLookup.ContainsKey(fh.MediaId))
                .Select(fh => (hash: fh, media: mediaLookup[fh.MediaId]))
                .OrderBy(m => m.media.CreateDate)
                .ToList();

            var manualOriginal = mediaItems.FirstOrDefault(m => m.hash.IsManuallySelectedOriginal);
            int? manualOriginalMediaId = manualOriginal.hash != null ? manualOriginal.media.Id : null;

            for (int i = 0; i < mediaItems.Count; i++)
            {
                var (fileHash, media) = mediaItems[i];
                var fileUrl = string.Empty;
                var extension = string.Empty;

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
                                    fileUrl = srcElement.GetString() ?? string.Empty;
                            }
                            catch { fileUrl = strValue; }
                        }
                        else
                        {
                            fileUrl = strValue;
                        }
                        if (!string.IsNullOrEmpty(fileUrl))
                            extension = System.IO.Path.GetExtension(fileUrl).TrimStart('.');
                    }
                }

                var displayPath = fileUrl;
                if (!string.IsNullOrEmpty(fileUrl))
                {
                    displayPath = fileUrl.Split('?')[0];
                    if (displayPath.StartsWith("/")) displayPath = displayPath.Substring(1);
                }
                else
                {
                    displayPath = $"Media/{media.Name}";
                }

                var uploaderName = "System";
                if (media.CreatorId != 0)
                {
                    if (!userCache.TryGetValue(media.CreatorId, out uploaderName!))
                    {
                        try
                        {
                            var user = _userService.GetUserById(media.CreatorId);
                            uploaderName = user?.Name ?? user?.Username ?? user?.Email ?? $"User {media.CreatorId}";
                        }
                        catch { uploaderName = $"User {media.CreatorId}"; }
                        userCache[media.CreatorId] = uploaderName;
                    }
                }

                items.Add(new DuplicateItem
                {
                    MediaId = media.Id,
                    Name = media.Name ?? string.Empty,
                    Path = displayPath,
                    FileUrl = fileUrl,
                    FileType = media.ContentType.Alias,
                    Extension = extension,
                    FileSize = fileHash.FileSize,
                    UploadDate = media.CreateDate,
                    IsOriginal = manualOriginalMediaId.HasValue ? media.Id == manualOriginalMediaId.Value : i == 0,
                    IsManuallySelected = fileHash.IsManuallySelectedOriginal,
                    UploaderName = uploaderName
                });
            }

            return new DuplicateGroup
            {
                Hash = hash,
                Count = items.Count,
                TotalSize = items.Sum(i => i.FileSize),
                Items = items
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting duplicate group by hash {Hash}", hash);
            throw;
        }
    }

}
