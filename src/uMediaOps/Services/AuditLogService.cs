using uMediaOps.Models;
using uMediaOps.Repositories;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace uMediaOps.Services;

public interface IAuditLogService
{
    Task LogActionAsync(string action, int? mediaId, string? mediaName, int? userId, string userName, object? details, bool success, string? errorMessage = null);
    Task<IEnumerable<AuditLogEntry>> GetRecentAsync(int count = 100);
    Task<IEnumerable<AuditLogEntry>> GetByMediaIdAsync(int mediaId);
    Task<IEnumerable<AuditLogEntry>> GetByUserIdAsync(int userId);
    Task<int> CleanupOldEntriesAsync(int daysToKeep = 90);
}

public class AuditLogService : IAuditLogService
{
    private readonly IAuditLogRepository _repository;
    private readonly ILogger<AuditLogService> _logger;

    public AuditLogService(
        IAuditLogRepository repository,
        ILogger<AuditLogService> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task LogActionAsync(
        string action,
        int? mediaId,
        string? mediaName,
        int? userId,
        string userName,
        object? details,
        bool success,
        string? errorMessage = null)
    {
        try
        {
            var entry = new AuditLogEntry
            {
                Action = action,
                MediaId = mediaId,
                MediaName = mediaName,
                UserId = userId,
                UserName = userName,
                Details = details != null ? JsonSerializer.Serialize(details) : null,
                Success = success,
                ErrorMessage = errorMessage
            };

            await _repository.SaveAsync(entry);
            _logger.LogDebug("Saved audit log entry: Action={Action}, MediaId={MediaId}", action, mediaId);
        }
        catch (Exception ex)
        {
            // Swallow audit log failures — they should never crash the main operation
            _logger.LogError(ex, "Failed to log audit entry for action {Action}, MediaId={MediaId}", action, mediaId);
        }
    }

    public async Task<IEnumerable<AuditLogEntry>> GetRecentAsync(int count = 100)
    {
        return await _repository.GetRecentAsync(count);
    }

    public async Task<IEnumerable<AuditLogEntry>> GetByMediaIdAsync(int mediaId)
    {
        return await _repository.GetByMediaIdAsync(mediaId);
    }

    public async Task<IEnumerable<AuditLogEntry>> GetByUserIdAsync(int userId)
    {
        return await _repository.GetByUserIdAsync(userId);
    }

    public async Task<int> CleanupOldEntriesAsync(int daysToKeep = 90)
    {
        var cutoffDate = DateTime.UtcNow.AddDays(-daysToKeep);
        var count = await _repository.DeleteOldEntriesAsync(cutoffDate);
        _logger.LogInformation("Cleaned up {Count} old audit log entries", count);
        return count;
    }
}
