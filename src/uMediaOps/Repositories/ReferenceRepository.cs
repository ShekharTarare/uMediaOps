using uMediaOps.Models;
using Microsoft.Extensions.Logging;
using NPoco;
using Umbraco.Cms.Infrastructure.Scoping;

namespace uMediaOps.Repositories;

public interface IReferenceRepository
{
    Task<IEnumerable<MediaReference>> GetReferencesByMediaIdAsync(int mediaId);
    Task SaveReferenceAsync(MediaReference reference);
    Task DeleteReferencesByMediaIdAsync(int mediaId);
    Task<int> GetReferenceCountAsync(int mediaId);
    Task<Dictionary<string, int>> GetReferenceBreakdownAsync(int mediaId);
}

public class ReferenceRepository : IReferenceRepository
{
    private readonly IScopeProvider _scopeProvider;
    private readonly ILogger<ReferenceRepository> _logger;

    public ReferenceRepository(
        IScopeProvider scopeProvider,
        ILogger<ReferenceRepository> logger)
    {
        _scopeProvider = scopeProvider;
        _logger = logger;
    }

    public async Task<IEnumerable<MediaReference>> GetReferencesByMediaIdAsync(int mediaId)
    {
        using var scope = _scopeProvider.CreateScope();
        var database = scope.Database;

        var sql = scope.SqlContext.Sql()
            .Select<MediaReference>()
            .From<MediaReference>()
            .Where<MediaReference>(x => x.MediaId == mediaId)
            .OrderBy<MediaReference>(x => x.ContentName);

        var references = await database.FetchAsync<MediaReference>(sql);
        scope.Complete();

        return references;
    }

    public async Task SaveReferenceAsync(MediaReference reference)
    {
        using var scope = _scopeProvider.CreateScope();
        var database = scope.Database;

        reference.LastChecked = DateTime.UtcNow;
        await database.InsertAsync(reference);

        scope.Complete();
    }

    public async Task DeleteReferencesByMediaIdAsync(int mediaId)
    {
        using var scope = _scopeProvider.CreateScope();
        var database = scope.Database;

        var sql = scope.SqlContext.Sql()
            .Delete()
            .From<MediaReference>()
            .Where<MediaReference>(x => x.MediaId == mediaId);

        await database.ExecuteAsync(sql);
        scope.Complete();
    }

    public async Task<int> GetReferenceCountAsync(int mediaId)
    {
        using var scope = _scopeProvider.CreateScope();
        var database = scope.Database;

        var sql = scope.SqlContext.Sql()
            .Select("COUNT(*)")
            .From<MediaReference>()
            .Where<MediaReference>(x => x.MediaId == mediaId);

        var count = await database.ExecuteScalarAsync<int>(sql);
        scope.Complete();

        return count;
    }

    public async Task<Dictionary<string, int>> GetReferenceBreakdownAsync(int mediaId)
    {
        using var scope = _scopeProvider.CreateScope();
        var database = scope.Database;

        var sql = scope.SqlContext.Sql()
            .Select("ContentType, COUNT(*) as Count")
            .From<MediaReference>()
            .Where<MediaReference>(x => x.MediaId == mediaId)
            .GroupBy("ContentType");

        var results = await database.FetchAsync<dynamic>(sql);
        scope.Complete();

        var breakdown = new Dictionary<string, int>();
        foreach (var result in results)
        {
            breakdown[result.ContentType] = result.Count;
        }

        return breakdown;
    }
}
