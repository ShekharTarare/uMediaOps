using uMediaOps.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Api.Management.Controllers;
using Umbraco.Cms.Api.Management.Routing;
using Umbraco.Cms.Core.Security;

namespace uMediaOps.Controllers;

[VersionedApiBackOfficeRoute("umediaops/auditlog")]
[ApiExplorerSettings(GroupName = "uMediaOps")]
[Authorize(Policy = "BackOfficeAccess")]
public class AuditLogController : ManagementApiControllerBase
{
    private readonly IAuditLogService _auditLogService;
    private readonly IBackOfficeSecurityAccessor _backOfficeSecurityAccessor;
    private readonly ILogger<AuditLogController> _logger;

    public AuditLogController(
        IAuditLogService auditLogService,
        IBackOfficeSecurityAccessor backOfficeSecurityAccessor,
        ILogger<AuditLogController> logger)
    {
        _auditLogService = auditLogService;
        _backOfficeSecurityAccessor = backOfficeSecurityAccessor;
        _logger = logger;
    }

    /// <summary>
    /// Get recent audit log entries
    /// </summary>
    [HttpGet("recent")]
    [ProducesResponseType(typeof(List<AuditLogEntryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRecent([FromQuery] int count = 100, [FromQuery] string? action = null)
    {
        try
        {
            // Get current user for logging
            var currentUser = _backOfficeSecurityAccessor.BackOfficeSecurity?.CurrentUser;
            _logger.LogInformation("GetRecent called by user: {UserName} (ID: {UserId})", 
                currentUser?.Name ?? currentUser?.Username ?? currentUser?.Email ?? "Anonymous", 
                currentUser?.Id ?? -1);

            var entries = await _auditLogService.GetRecentAsync(count);

            // Filter by action if specified
            if (!string.IsNullOrWhiteSpace(action))
            {
                entries = entries.Where(e => e.Action == action).ToList();
            }

            var response = entries.Select(e => new AuditLogEntryDto
            {
                Id = e.Id,
                Action = e.Action,
                MediaId = e.MediaId,
                MediaName = e.MediaName,
                UserId = e.UserId,
                UserName = e.UserName,
                Timestamp = e.Timestamp,
                Details = e.Details,
                Success = e.Success
            }).ToList();

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting recent audit log entries");
            return StatusCode(StatusCodes.Status500InternalServerError, "Failed to get audit log entries");
        }
    }

    /// <summary>
    /// Get audit log entries for a specific media item
    /// </summary>
    [HttpGet("media/{mediaId:int}")]
    [ProducesResponseType(typeof(List<AuditLogEntryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetByMediaId(int mediaId)
    {
        try
        {
            var entries = await _auditLogService.GetByMediaIdAsync(mediaId);

            var response = entries.Select(e => new AuditLogEntryDto
            {
                Id = e.Id,
                Action = e.Action,
                MediaId = e.MediaId,
                MediaName = e.MediaName,
                UserId = e.UserId,
                UserName = e.UserName,
                Timestamp = e.Timestamp,
                Details = e.Details,
                Success = e.Success
            }).ToList();

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting audit log entries for media {MediaId}", mediaId);
            return StatusCode(StatusCodes.Status500InternalServerError, "Failed to get audit log entries");
        }
    }

    /// <summary>
    /// Get audit log entries for a specific user
    /// </summary>
    [HttpGet("user/{userId:int}")]
    [ProducesResponseType(typeof(List<AuditLogEntryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetByUserId(int userId)
    {
        try
        {
            var entries = await _auditLogService.GetByUserIdAsync(userId);

            var response = entries.Select(e => new AuditLogEntryDto
            {
                Id = e.Id,
                Action = e.Action,
                MediaId = e.MediaId,
                MediaName = e.MediaName,
                UserId = e.UserId,
                UserName = e.UserName,
                Timestamp = e.Timestamp,
                Details = e.Details,
                Success = e.Success
            }).ToList();

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting audit log entries for user {UserId}", userId);
            return StatusCode(StatusCodes.Status500InternalServerError, "Failed to get audit log entries");
        }
    }
}

// DTOs
public class AuditLogEntryDto
{
    public int Id { get; set; }
    public string Action { get; set; } = string.Empty;
    public int? MediaId { get; set; }
    public string? MediaName { get; set; }
    public int? UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string? Details { get; set; }
    public bool Success { get; set; }
    // ErrorMessage intentionally excluded — may contain internal exception details from historical data
}
