using uMediaOps.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Api.Management.Controllers;
using Umbraco.Cms.Api.Management.Routing;

namespace uMediaOps.Controllers;

/// <summary>
/// API controller for analytics operations
/// </summary>
[VersionedApiBackOfficeRoute("umediaops/analytics")]
[ApiExplorerSettings(GroupName = "uMediaOps - Analytics")]
[Authorize(Policy = "BackOfficeAccess")]
public class AnalyticsController : ManagementApiControllerBase
{
    private readonly IAnalyticsService _analyticsService;
    private readonly ILogger<AnalyticsController> _logger;

    public AnalyticsController(
        IAnalyticsService analyticsService,
        ILogger<AnalyticsController> logger)
    {
        _analyticsService = analyticsService;
        _logger = logger;
    }

    /// <summary>
    /// Get storage savings history
    /// </summary>
    [HttpGet("savings")]
    public async Task<IActionResult> GetSavings()
    {
        try
        {
            var savings = await _analyticsService.GetStorageSavingsHistoryAsync();
            return Ok(savings);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting savings");
            throw;
        }
    }

    /// <summary>
    /// Get file type breakdown statistics
    /// </summary>
    [HttpGet("statistics")]
    public async Task<IActionResult> GetStatistics()
    {
        try
        {
            var breakdown = await _analyticsService.GetFileTypeBreakdownAsync();
            return Ok(breakdown);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting statistics");
            throw;
        }
    }
}
