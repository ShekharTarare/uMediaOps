using uMediaOps.Models;
using uMediaOps.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Api.Management.Controllers;
using Umbraco.Cms.Api.Management.Routing;
using Umbraco.Cms.Core;

namespace uMediaOps.Controllers;

[VersionedApiBackOfficeRoute("umediaops/references")]
[ApiExplorerSettings(GroupName = "uMediaOps")]
[Authorize(Policy = "BackOfficeAccess")]
public class ReferencesController : ManagementApiControllerBase
{
    private readonly IReferenceTrackingService _referenceTrackingService;
    private readonly ILogger<ReferencesController> _logger;

    public ReferencesController(
        IReferenceTrackingService referenceTrackingService,
        ILogger<ReferencesController> logger)
    {
        _referenceTrackingService = referenceTrackingService;
        _logger = logger;
    }

    /// <summary>
    /// Get all references for a media item
    /// </summary>
    [HttpGet("{mediaId:int}")]
    [ProducesResponseType(typeof(ReferenceResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetReferences(int mediaId)
    {
        try
        {
            var references = await _referenceTrackingService.GetReferencesAsync(mediaId, ScanProfile.Quick);
            var referencesList = references.ToList();

            var breakdown = referencesList
                .GroupBy(r => r.ContentType)
                .ToDictionary(g => g.Key, g => g.Count());

            var response = new ReferenceResponse
            {
                MediaId = mediaId,
                References = referencesList.Select(r => new ReferenceDto
                {
                    ContentId = r.ContentId,
                    ContentName = r.ContentName,
                    ContentType = r.ContentType,
                    PropertyAlias = r.PropertyAlias,
                    Url = r.Url,
                    LastChecked = r.LastChecked
                }).ToList(),
                Statistics = new ReferenceStatisticsDto
                {
                    TotalReferences = referencesList.Count,
                    ByContentType = breakdown,
                    IsSafeToDelete = referencesList.Count == 0
                }
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting references for media {MediaId}", mediaId);
            return StatusCode(StatusCodes.Status500InternalServerError, "Failed to get references");
        }
    }

}

// DTOs
public class ReferenceResponse
{
    public int MediaId { get; set; }
    public List<ReferenceDto> References { get; set; } = new();
    public ReferenceStatisticsDto Statistics { get; set; } = new();
}

public class ReferenceDto
{
    public int ContentId { get; set; }
    public string ContentName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public string PropertyAlias { get; set; } = string.Empty;
    public string? Url { get; set; }
    public DateTime LastChecked { get; set; }
}

public class ReferenceStatisticsDto
{
    public int TotalReferences { get; set; }
    public Dictionary<string, int> ByContentType { get; set; } = new();
    public bool IsSafeToDelete { get; set; }
}
