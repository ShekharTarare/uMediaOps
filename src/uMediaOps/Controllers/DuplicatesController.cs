using uMediaOps.Models;
using uMediaOps.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Api.Management.Controllers;
using Umbraco.Cms.Api.Management.Routing;
using Umbraco.Cms.Core.Security;
using Umbraco.Cms.Core.Services;

namespace uMediaOps.Controllers;

/// <summary>
/// Request model for deleting duplicates
/// </summary>
public class DeleteDuplicatesRequest
{
    [System.ComponentModel.DataAnnotations.Required]
    [System.ComponentModel.DataAnnotations.MinLength(1, ErrorMessage = "At least one media ID is required")]
    public List<int> MediaIds { get; set; } = new();
    public bool UseSoftDelete { get; set; } = true;
    public bool CheckReferences { get; set; } = true;
}

/// <summary>
/// Operation status for duplicate operations
/// </summary>
public enum DuplicateOperationStatus
{
    NotFound,
    InvalidRequest
}

/// <summary>
/// API controller for duplicate management operations
/// </summary>
[VersionedApiBackOfficeRoute("umediaops/duplicates")]
[ApiExplorerSettings(GroupName = "uMediaOps - Duplicates")]
[Authorize(Policy = "BackOfficeAccess")]
public class DuplicatesController : ManagementApiControllerBase
{
    private readonly IDuplicateDetectionService _duplicateDetectionService;
    private readonly IReferenceTrackingService _referenceTrackingService;
    private readonly IAuditLogService _auditLogService;
    private readonly IAnalyticsService _analyticsService;
    private readonly IBackOfficeSecurityAccessor _backOfficeSecurityAccessor;
    private readonly IMediaService _mediaService;
    private readonly ICacheService _cacheService;
    private readonly ILogger<DuplicatesController> _logger;

    public DuplicatesController(
        IDuplicateDetectionService duplicateDetectionService,
        IReferenceTrackingService referenceTrackingService,
        IAuditLogService auditLogService,
        IAnalyticsService analyticsService,
        IBackOfficeSecurityAccessor backOfficeSecurityAccessor,
        IMediaService mediaService,
        ICacheService cacheService,
        ILogger<DuplicatesController> logger)
    {
        _duplicateDetectionService = duplicateDetectionService;
        _referenceTrackingService = referenceTrackingService;
        _auditLogService = auditLogService;
        _analyticsService = analyticsService;
        _backOfficeSecurityAccessor = backOfficeSecurityAccessor;
        _mediaService = mediaService;
        _cacheService = cacheService;
        _logger = logger;
    }

    /// <summary>
    /// Get all duplicate groups
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetDuplicateGroups(
        [FromQuery] string? fileTypeFilter = null,
        [FromQuery] string? search = null,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 50)
    {
        try
        {
            if (pageNumber < 1) pageNumber = 1;
            if (pageSize < 1 || pageSize > 500) pageSize = 50;

            var cacheKey = $"umediaops:duplicates:groups:{fileTypeFilter ?? "all"}";
            var groupsList = _cacheService.Get<List<DuplicateGroup>>(cacheKey);
            if (groupsList == null)
            {
                var allGroups = await _duplicateDetectionService.GetDuplicateGroupsAsync(fileTypeFilter);
                groupsList = allGroups.ToList();
                _cacheService.Set(cacheKey, groupsList, TimeSpan.FromMinutes(2));
            }

            IEnumerable<DuplicateGroup> filtered = groupsList;

            if (!string.IsNullOrEmpty(search))
            {
                filtered = filtered.Where(g =>
                    g.Items.Any(i => i.Name.Contains(search, StringComparison.OrdinalIgnoreCase))
                );
            }

            var filteredList = filtered.OrderByDescending(g => g.TotalSize).ToList();
            var totalDuplicateFiles = filteredList.Sum(g => g.Count - 1);
            var totalStorageWasted = filteredList.Sum(g => g.TotalSize - (g.Items.Any() ? g.Items.First().FileSize : 0));

            var totalItems = filteredList.Count;
            var totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);
            var skip = (pageNumber - 1) * pageSize;
            var pagedGroups = filteredList.Skip(skip).Take(pageSize);

            return Ok(new
            {
                items = pagedGroups,
                totalItems,
                totalDuplicateFiles,
                totalStorageWasted,
                totalPages,
                currentPage = pageNumber,
                pageSize,
                hasPreviousPage = pageNumber > 1,
                hasNextPage = pageNumber < totalPages
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving duplicate groups");
            throw;
        }
    }

    /// <summary>
    /// Get a specific duplicate group by hash
    /// </summary>
    [HttpGet("{hash}")]
    public async Task<IActionResult> GetDuplicateGroup(string hash)
    {
        try
        {
            var group = await _duplicateDetectionService.GetDuplicateGroupByHashAsync(hash);
            return group is not null
                ? Ok(group)
                : OperationStatusResult(
                    DuplicateOperationStatus.NotFound,
                    builder => NotFound(builder.WithTitle("Duplicate group not found")
                        .WithDetail($"No duplicate group found with hash: {hash}").Build()));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving duplicate group {Hash}", hash);
            throw;
        }
    }

    /// <summary>
    /// Delete duplicate files
    /// </summary>
    [HttpPost("delete")]
    public async Task<IActionResult> DeleteDuplicates([FromBody] DeleteDuplicatesRequest request)
    {
        if (request.MediaIds == null || request.MediaIds.Count == 0)
        {
            return OperationStatusResult(DuplicateOperationStatus.InvalidRequest,
                builder => BadRequest(builder.WithTitle("Invalid request").WithDetail("No media IDs provided").Build()));
        }

        var currentUser = _backOfficeSecurityAccessor.BackOfficeSecurity?.CurrentUser;
        var userId = currentUser?.Id;
        var userName = currentUser?.Name ?? currentUser?.Username ?? currentUser?.Email ?? "Unknown";

        var deletedCount = 0;
        var spaceFreed = 0L;
        var errors = new List<string>();
        var referencedFiles = new List<int>();

        try
        {
            if (request.CheckReferences)
            {
                foreach (var mediaId in request.MediaIds)
                {
                    var isSafe = await _referenceTrackingService.IsSafeToDeleteAsync(mediaId);
                    if (!isSafe)
                    {
                        var media = _mediaService.GetById(mediaId);
                        referencedFiles.Add(mediaId);
                        errors.Add($"File '{media?.Name}' (ID: {mediaId}) is referenced in content and cannot be deleted");
                        await _auditLogService.LogActionAsync("DeleteDuplicate_Blocked", mediaId, media?.Name,
                            userId, userName, new { Reason = "File is referenced in content" }, false, "File is referenced in content");
                    }
                }

                var safeToDelete = request.MediaIds.Except(referencedFiles).ToList();
                if (safeToDelete.Count == 0)
                {
                    return Ok(new { deletedCount = 0, spaceFreed = 0L, errors, referencedFiles,
                        message = "No files were deleted because all selected files are referenced in content" });
                }
                request.MediaIds = safeToDelete;
            }

            foreach (var mediaId in request.MediaIds)
            {
                try
                {
                    var media = _mediaService.GetById(mediaId);
                    if (media == null) { errors.Add($"Media with ID {mediaId} not found"); continue; }
                    if (media.Trashed) { errors.Add($"Media with ID {mediaId} is already in the recycle bin"); continue; }

                    var mediaName = media.Name ?? "Unknown";
                    var fileSize = 0L;
                    if (media.HasProperty("umbracoBytes") && media.GetValue("umbracoBytes") != null)
                    {
                        if (long.TryParse(media.GetValue("umbracoBytes")?.ToString(), out var bytes))
                            fileSize = bytes;
                    }

                    if (request.UseSoftDelete)
                    {
                        var result = _mediaService.MoveToRecycleBin(media);
                        if (result.Success)
                        {
                            await _auditLogService.LogActionAsync("DeleteDuplicate_SoftDelete", mediaId, mediaName,
                                userId, userName, new { FileSize = fileSize }, true);
                            _logger.LogInformation("Moved media {MediaId} to recycle bin", mediaId);
                        }
                        else { errors.Add($"Failed to move media {mediaId} to recycle bin"); }
                    }
                    else
                    {
                        _mediaService.Delete(media);
                        await _auditLogService.LogActionAsync("DeleteDuplicate_HardDelete", mediaId, mediaName,
                            userId, userName, new { FileSize = fileSize }, true);
                        _logger.LogInformation("Permanently deleted media {MediaId}", mediaId);
                    }

                    deletedCount++;
                    spaceFreed += fileSize;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error deleting media {MediaId}", mediaId);
                    errors.Add($"Error deleting media {mediaId}");
                    var media = _mediaService.GetById(mediaId);
                    await _auditLogService.LogActionAsync("DeleteDuplicate_Error", mediaId, media?.Name,
                        userId, userName, new { Error = ex.Message }, false, ex.Message);
                }
            }

            if (deletedCount > 0)
            {
                await _analyticsService.RecordDeletionAsync(deletedCount, spaceFreed,
                    System.Text.Json.JsonSerializer.Serialize(new { UseSoftDelete = request.UseSoftDelete, CheckedReferences = request.CheckReferences, DeletedBy = userName }));
                _cacheService.RemoveByPattern("umediaops:duplicates");
                _logger.LogInformation("Cleared scan cache after deleting {Count} files", deletedCount);
            }

            return Ok(new { deletedCount, spaceFreed, errors = errors.Count > 0 ? errors : null,
                referencedFiles = referencedFiles.Count > 0 ? referencedFiles : null, usedSoftDelete = request.UseSoftDelete });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in delete duplicates operation");
            await _auditLogService.LogActionAsync("DeleteDuplicates_Error", null, null, userId, userName,
                new { MediaIds = request.MediaIds, Error = ex.Message }, false, ex.Message);
            return StatusCode(500, new { message = "An error occurred while deleting duplicates." });
        }
    }
}
