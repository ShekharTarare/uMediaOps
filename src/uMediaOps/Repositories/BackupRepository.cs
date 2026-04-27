using uMediaOps.Models;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Infrastructure.Scoping;

namespace uMediaOps.Repositories;

public interface IBackupRepository
{
    Task<int> SaveAsync(Backup backup);
    Task UpdateAsync(Backup backup);
    Task DeleteAsync(int id);
    Task<Backup?> GetByIdAsync(int id);
    Task<Backup?> GetByBackupIdAsync(string backupId);
    Task<IEnumerable<Backup>> GetAllAsync();
    Task<IEnumerable<Backup>> GetExpiredAsync();
}

public class BackupRepository : IBackupRepository
{
    private readonly IScopeProvider _scopeProvider;
    private readonly ILogger<BackupRepository> _logger;

    public BackupRepository(
        IScopeProvider scopeProvider,
        ILogger<BackupRepository> logger)
    {
        _scopeProvider = scopeProvider;
        _logger = logger;
    }

    public async Task<int> SaveAsync(Backup backup)
    {
        using var scope = _scopeProvider.CreateScope();
        var database = scope.Database;

        backup.StartedAt = DateTime.UtcNow;
        await database.InsertAsync(backup);

        scope.Complete();
        return backup.Id;
    }

    public async Task UpdateAsync(Backup backup)
    {
        using var scope = _scopeProvider.CreateScope();
        var database = scope.Database;

        await database.UpdateAsync(backup);

        scope.Complete();
    }

    public async Task DeleteAsync(int id)
    {
        using var scope = _scopeProvider.CreateScope();
        var database = scope.Database;

        var sql = scope.SqlContext.Sql()
            .Delete()
            .From<Backup>()
            .Where<Backup>(x => x.Id == id);

        await database.ExecuteAsync(sql);

        scope.Complete();
    }

    public async Task<Backup?> GetByIdAsync(int id)
    {
        using var scope = _scopeProvider.CreateScope();
        var database = scope.Database;

        var sql = scope.SqlContext.Sql()
            .Select<Backup>()
            .From<Backup>()
            .Where<Backup>(x => x.Id == id);

        var backup = await database.FirstOrDefaultAsync<Backup>(sql);
        scope.Complete();

        return backup;
    }

    public async Task<Backup?> GetByBackupIdAsync(string backupId)
    {
        using var scope = _scopeProvider.CreateScope();
        var database = scope.Database;

        var sql = scope.SqlContext.Sql()
            .Select<Backup>()
            .From<Backup>()
            .Where<Backup>(x => x.BackupId == backupId);

        var backup = await database.FirstOrDefaultAsync<Backup>(sql);
        scope.Complete();

        return backup;
    }

    public async Task<IEnumerable<Backup>> GetAllAsync()
    {
        using var scope = _scopeProvider.CreateScope();
        var database = scope.Database;

        var sql = scope.SqlContext.Sql()
            .Select<Backup>()
            .From<Backup>()
            .OrderByDescending<Backup>(x => x.StartedAt);

        var backups = await database.FetchAsync<Backup>(sql);
        scope.Complete();

        return backups;
    }

    public async Task<IEnumerable<Backup>> GetExpiredAsync()
    {
        using var scope = _scopeProvider.CreateScope();
        var database = scope.Database;

        var now = DateTime.UtcNow;
        var sql = scope.SqlContext.Sql()
            .Select<Backup>()
            .From<Backup>()
            .Where<Backup>(x => x.ExpiresAt != null && x.ExpiresAt < now)
            .OrderBy<Backup>(x => x.ExpiresAt);

        var backups = await database.FetchAsync<Backup>(sql);
        scope.Complete();

        return backups;
    }
}
