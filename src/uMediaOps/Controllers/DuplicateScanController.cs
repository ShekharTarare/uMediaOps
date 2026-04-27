using uMediaOps.Models;
using uMediaOps.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Api.Management.Controllers;
using Umbraco.Cms.Api.Management.Routing;
using Umbraco.Cms.Core.Security;

namespace uMediaOps.Controllers;

/// <summary>
/// API controller for duplicate scan operations
/// </summary>
[VersionedApiBackOfficeRoute("umediaops/scan")]
[ApiExplorerSettings(GroupName = "uMediaOps - Duplicate Scan")]
[Authorize(Policy = "BackOfficeAccess")]
public class DuplicateScanController : ManagementApiControllerBase
{
    private readonly IDuplicateDetectionService _duplicateDetectionService;
    private readonly IBackOfficeSecurityAccessor _backOfficeSecurityAccessor;
    private readonly ICacheService _cacheService;
    private readonly ILogger<DuplicateScanController> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IAnalyticsService _analyticsService;

    public DuplicateScanController(
        IDuplicateDetectionService duplicateDetectionService,
        IBackOfficeSecurityAccessor backOfficeSecurityAccessor,
        ICacheService cacheService,
        ILogger<DuplicateScanController> logger,
        IServiceScopeFactory scopeFactory,
        IAnalyticsService analyticsService)
    {
        _duplicateDetectionService = duplicateDetectionService;
        _backOfficeSecurityAccessor = backOfficeSecurityAccessor;
        _cacheService = cacheService;
        _logger = logger;
        _scopeFactory = scopeFactory;
        _analyticsService = analyticsService;
    }

    /// <summary>
    /// Start a duplicate scan of the media library
    /// </summary>
    /// <returns>Scan initiation result</returns>
    [HttpPost("start")]
    public async Task<IActionResult> StartScan()
    {
        // Atomic check-and-set to prevent concurrent scan starts
        if (!_cacheService.TrySetIfAbsent("umediaops:duplicates:is-scanning", true))
        {
            return BadRequest(new { message = "A scan is already in progress" });
        }

        // Rate limit: prevent scan starts within 10 seconds of each other
        var lastScanStart = _cacheService.Get<DateTime?>("umediaops:duplicates:last-scan-start");
        if (lastScanStart.HasValue && (DateTime.UtcNow - lastScanStart.Value).TotalSeconds < 10)
        {
            _cacheService.Remove("umediaops:duplicates:is-scanning");
            return StatusCode(429, new { message = "Please wait before starting another scan" });
        }

        _cacheService.Set("umediaops:duplicates:last-scan-start", DateTime.UtcNow, TimeSpan.FromMinutes(1));
        _cacheService.Set(CacheKeys.DuplicateScanProgress, new ScanProgress { Processed = 0, Total = 0 });
        _cacheService.Remove(CacheKeys.DuplicateScanResult);

        // Get current user
        var currentUser = _backOfficeSecurityAccessor.BackOfficeSecurity?.CurrentUser;
        var userId = currentUser?.Id;
        var userName = currentUser?.Name ?? currentUser?.Username ?? currentUser?.Email ?? "Unknown";

        // Start scan in background using a new DI scope
        _ = Task.Run(async () =>
        {
            using var scope = _scopeFactory.CreateScope();
            var duplicateDetectionService = scope.ServiceProvider.GetRequiredService<IDuplicateDetectionService>();
            var auditLogService = scope.ServiceProvider.GetRequiredService<IAuditLogService>();
            var analyticsService = scope.ServiceProvider.GetRequiredService<IAnalyticsService>();

            try
            {
                var progress = new Progress<ScanProgress>(p =>
                {
                    _cacheService.Set(CacheKeys.DuplicateScanProgress, p);
                });

                var scanResult = await duplicateDetectionService.ScanMediaLibraryAsync(progress);
                _cacheService.Set(CacheKeys.DuplicateScanResult, scanResult);
                
                // Clear stale duplicate groups cache so the next request fetches fresh data
                _cacheService.RemoveByPattern("umediaops:duplicates:groups");
                
                // Record analytics
                await analyticsService.RecordScanResultAsync(scanResult);
                
                // Log successful scan
                await auditLogService.LogActionAsync(
                    "Scan_Completed",
                    null,
                    null,
                    userId,
                    userName,
                    new
                    {
                        TotalScanned = scanResult.TotalScanned,
                        DuplicateGroupsFound = scanResult.DuplicateGroupsFound,
                        TotalDuplicates = scanResult.TotalDuplicates,
                        StorageWasted = scanResult.StorageWasted
                    },
                    true
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Duplicate scan failed");
                // Log failed scan
                await auditLogService.LogActionAsync(
                    "Scan_Failed",
                    null,
                    null,
                    userId,
                    userName,
                    new { Error = ex.Message },
                    false,
                    ex.Message
                );
            }
            finally
            {
                _cacheService.Remove("umediaops:duplicates:is-scanning");
            }
        });

        return Ok(new { message = "Scan started", isScanning = true });
    }

    /// <summary>
    /// Get the current scan progress
    /// </summary>
    /// <returns>Current scan progress</returns>
    [HttpGet("progress")]
    public IActionResult GetProgress()
    {
        var isScanning = _cacheService.Get<bool>("umediaops:duplicates:is-scanning");
        var progress = _cacheService.Get<ScanProgress>(CacheKeys.DuplicateScanProgress) ?? new ScanProgress();
        
        return Ok(new
        {
            isScanning,
            progress
        });
    }

    /// <summary>
    /// Get the results of the last completed scan
    /// </summary>
    /// <returns>Scan results</returns>
    [HttpGet("results")]
    public async Task<IActionResult> GetResults()
    {
        // Try cache first (available during current session)
        var scanResult = _cacheService.Get<ScanResult>(CacheKeys.DuplicateScanResult);
        if (scanResult != null)
        {
            return Ok(scanResult);
        }

        // Fall back to latest analytics record from DB (persists across restarts)
        var latest = await _analyticsService.GetLatestScanResultAsync();
        if (latest != null)
        {
            return Ok(latest);
        }

        return NotFound(new { message = "No scan results available" });
    }

    /// <summary>
    /// Clear the cached scan results (useful after deleting files)
    /// </summary>
    /// <returns>Success message</returns>
    [HttpPost("clear")]
    public IActionResult ClearResults()
    {
        _cacheService.RemoveByPattern("umediaops:duplicates");
        return Ok(new { message = "Scan results cleared" });
    }
}
