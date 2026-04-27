using uMediaOps.Models;
using NPoco;
using Umbraco.Cms.Infrastructure.Scoping;

namespace uMediaOps.Repositories;

/// <summary>
/// Repository for file hash database operations
/// </summary>
public interface IFileHashRepository
{
    Task<FileHash?> GetByMediaIdAsync(int mediaId);
    Task<IEnumerable<FileHash>> GetByHashAsync(string hash);
    Task SaveAsync(FileHash fileHash);
    Task<IEnumerable<IGrouping<string, FileHash>>> GetDuplicateGroupsAsync();
    Task DeleteByMediaIdAsync(int mediaId);
    Task<int> DeleteOrphanedAsync(IEnumerable<int> validMediaIds);
}

/// <summary>
/// Implementation of file hash repository using IScopeProvider
/// </summary>
public class FileHashRepository : IFileHashRepository
{
    private readonly IScopeProvider _scopeProvider;

    public FileHashRepository(IScopeProvider scopeProvider)
    {
        _scopeProvider = scopeProvider;
    }

    public async Task<FileHash?> GetByMediaIdAsync(int mediaId)
    {
        using var scope = _scopeProvider.CreateScope();
        var sql = scope.SqlContext.Sql()
            .Select<FileHash>()
            .From<FileHash>()
            .Where<FileHash>(x => x.MediaId == mediaId);

        var result = await scope.Database.FirstOrDefaultAsync<FileHash>(sql);
        scope.Complete();
        return result;
    }

    public async Task<IEnumerable<FileHash>> GetByHashAsync(string hash)
    {
        using var scope = _scopeProvider.CreateScope();
        var sql = scope.SqlContext.Sql()
            .Select<FileHash>()
            .From<FileHash>()
            .Where<FileHash>(x => x.Hash == hash);

        var results = await scope.Database.FetchAsync<FileHash>(sql);
        scope.Complete();
        return results;
    }

    public async Task SaveAsync(FileHash fileHash)
    {
        using var scope = _scopeProvider.CreateScope();

        var existingSql = scope.SqlContext.Sql()
            .Select<FileHash>()
            .From<FileHash>()
            .Where<FileHash>(x => x.MediaId == fileHash.MediaId);

        var existing = await scope.Database.FirstOrDefaultAsync<FileHash>(existingSql);

        if (existing != null)
        {
            fileHash.Id = existing.Id;
            await scope.Database.UpdateAsync(fileHash);
        }
        else
        {
            await scope.Database.InsertAsync(fileHash);
        }

        scope.Complete();
    }

    public async Task<IEnumerable<IGrouping<string, FileHash>>> GetDuplicateGroupsAsync()
    {
        using var scope = _scopeProvider.CreateScope();

        // Get only hashes that appear more than once using raw SQL
        var duplicateHashes = await scope.Database.FetchAsync<string>(
            "SELECT Hash FROM uMediaOps_FileHashes GROUP BY Hash HAVING COUNT(*) > 1");

        if (duplicateHashes.Count == 0)
        {
            scope.Complete();
            return Enumerable.Empty<IGrouping<string, FileHash>>();
        }

        // Fetch only the file hashes that belong to duplicate groups
        var sql = scope.SqlContext.Sql()
            .Select<FileHash>()
            .From<FileHash>()
            .WhereIn<FileHash>(x => x.Hash, duplicateHashes);

        var allHashes = await scope.Database.FetchAsync<FileHash>(sql);
        scope.Complete();

        return allHashes.GroupBy(h => h.Hash);
    }

    public async Task DeleteByMediaIdAsync(int mediaId)
    {
        using var scope = _scopeProvider.CreateScope();
        var sql = scope.SqlContext.Sql()
            .Delete()
            .From<FileHash>()
            .Where<FileHash>(x => x.MediaId == mediaId);

        await scope.Database.ExecuteAsync(sql);
        scope.Complete();
    }

    public async Task<int> DeleteOrphanedAsync(IEnumerable<int> validMediaIds)
    {
        using var scope = _scopeProvider.CreateScope();
        var validSet = validMediaIds.ToHashSet();

        // Get all media IDs in the hash table
        var allHashMediaIds = await scope.Database.FetchAsync<int>(
            "SELECT MediaId FROM uMediaOps_FileHashes");

        var orphanedIds = allHashMediaIds.Where(id => !validSet.Contains(id)).ToList();
        if (orphanedIds.Count == 0)
        {
            scope.Complete();
            return 0;
        }

        // Delete in batches to avoid SQL parameter limits
        var deleted = 0;
        foreach (var batch in orphanedIds.Chunk(500))
        {
            var placeholders = string.Join(",", batch.Select((_, i) => $"@{i}"));
            var sql = $"DELETE FROM uMediaOps_FileHashes WHERE MediaId IN ({placeholders})";
            deleted += await scope.Database.ExecuteAsync(sql, batch.Cast<object>().ToArray());
        }

        scope.Complete();
        return deleted;
    }
}
