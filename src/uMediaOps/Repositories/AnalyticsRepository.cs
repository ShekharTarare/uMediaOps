using uMediaOps.Models;
using NPoco;
using Umbraco.Cms.Infrastructure.Scoping;

namespace uMediaOps.Repositories;

/// <summary>
/// Repository interface for analytics data
/// </summary>
public interface IAnalyticsRepository
{
    Task SaveSnapshotAsync(AnalyticsData record);
    Task<List<AnalyticsData>> GetSnapshotsAsync(DateTime? startDate = null, DateTime? endDate = null);
    Task<AnalyticsData?> GetLatestSnapshotAsync();
}

/// <summary>
/// Repository for analytics data persistence
/// </summary>
public class AnalyticsRepository : IAnalyticsRepository
{
    private readonly IScopeProvider _scopeProvider;

    public AnalyticsRepository(IScopeProvider scopeProvider)
    {
        _scopeProvider = scopeProvider;
    }

    /// <summary>
    /// Save an analytics snapshot
    /// </summary>
    public async Task SaveSnapshotAsync(AnalyticsData record)
    {
        using var scope = _scopeProvider.CreateScope();
        var database = scope.Database;

        await database.InsertAsync(record);

        scope.Complete();
    }

    /// <summary>
    /// Get analytics snapshots within a date range
    /// </summary>
    public async Task<List<AnalyticsData>> GetSnapshotsAsync(DateTime? startDate = null, DateTime? endDate = null)
    {
        using var scope = _scopeProvider.CreateScope();
        var database = scope.Database;

        var sql = scope.SqlContext.Sql()
            .Select("*")
            .From<AnalyticsData>();

        if (startDate.HasValue)
        {
            sql = sql.Where<AnalyticsData>(x => x.RecordedAt >= startDate.Value);
        }

        if (endDate.HasValue)
        {
            sql = sql.Where<AnalyticsData>(x => x.RecordedAt <= endDate.Value);
        }

        sql = sql.OrderBy<AnalyticsData>(x => x.RecordedAt);

        var results = await database.FetchAsync<AnalyticsData>(sql);

        scope.Complete();
        return results;
    }

    /// <summary>
    /// Get the most recent analytics snapshot
    /// </summary>
    public async Task<AnalyticsData?> GetLatestSnapshotAsync()
    {
        using var scope = _scopeProvider.CreateScope();
        var database = scope.Database;

        var sql = scope.SqlContext.Sql()
            .Select<AnalyticsData>()
            .From<AnalyticsData>()
            .OrderByDescending<AnalyticsData>(x => x.RecordedAt);

        var result = await database.FirstOrDefaultAsync<AnalyticsData>(sql);

        scope.Complete();
        return result;
    }
}
