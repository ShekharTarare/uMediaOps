using uMediaOps.Models;
using Microsoft.Extensions.Logging;
using NPoco;
using Umbraco.Cms.Infrastructure.Scoping;

namespace uMediaOps.Repositories;

public interface IAuditLogRepository
{
    Task SaveAsync(AuditLogEntry entry);
    Task<IEnumerable<AuditLogEntry>> GetRecentAsync(int count = 100);
    Task<IEnumerable<AuditLogEntry>> GetByMediaIdAsync(int mediaId);
    Task<IEnumerable<AuditLogEntry>> GetByUserIdAsync(int userId);
    Task<int> DeleteOldEntriesAsync(DateTime olderThan);
}

public class AuditLogRepository : IAuditLogRepository
{
    private readonly IScopeProvider _scopeProvider;
    private readonly ILogger<AuditLogRepository> _logger;

    public AuditLogRepository(
        IScopeProvider scopeProvider,
        ILogger<AuditLogRepository> logger)
    {
        _scopeProvider = scopeProvider;
        _logger = logger;
    }

    public async Task SaveAsync(AuditLogEntry entry)
    {
        using var scope = _scopeProvider.CreateScope();
        var database = scope.Database;

        entry.Timestamp = DateTime.UtcNow;
        await database.InsertAsync(entry);

        scope.Complete();
    }

    public async Task<IEnumerable<AuditLogEntry>> GetRecentAsync(int count = 100)
    {
        // Clamp count to prevent abuse
        if (count < 1) count = 1;
        if (count > 1000) count = 1000;

        using var scope = _scopeProvider.CreateScope();
        var database = scope.Database;

        var sql = scope.SqlContext.Sql()
            .Select<AuditLogEntry>()
            .From<AuditLogEntry>()
            .OrderByDescending<AuditLogEntry>(x => x.Timestamp);

        var entries = await database.FetchAsync<AuditLogEntry>(1, count, sql);
        scope.Complete();

        return entries;
    }

    public async Task<IEnumerable<AuditLogEntry>> GetByMediaIdAsync(int mediaId)
    {
        using var scope = _scopeProvider.CreateScope();
        var database = scope.Database;

        var sql = scope.SqlContext.Sql()
            .Select<AuditLogEntry>()
            .From<AuditLogEntry>()
            .Where<AuditLogEntry>(x => x.MediaId == mediaId)
            .OrderByDescending<AuditLogEntry>(x => x.Timestamp);

        var entries = await database.FetchAsync<AuditLogEntry>(sql);
        scope.Complete();

        return entries;
    }

    public async Task<IEnumerable<AuditLogEntry>> GetByUserIdAsync(int userId)
    {
        using var scope = _scopeProvider.CreateScope();
        var database = scope.Database;

        var sql = scope.SqlContext.Sql()
            .Select<AuditLogEntry>()
            .From<AuditLogEntry>()
            .Where<AuditLogEntry>(x => x.UserId == userId)
            .OrderByDescending<AuditLogEntry>(x => x.Timestamp);

        var entries = await database.FetchAsync<AuditLogEntry>(sql);
        scope.Complete();

        return entries;
    }

    public async Task<int> DeleteOldEntriesAsync(DateTime olderThan)
    {
        using var scope = _scopeProvider.CreateScope();
        var database = scope.Database;

        var sql = scope.SqlContext.Sql()
            .Delete()
            .From<AuditLogEntry>()
            .Where<AuditLogEntry>(x => x.Timestamp < olderThan);

        var count = await database.ExecuteAsync(sql);
        scope.Complete();

        return count;
    }
}
