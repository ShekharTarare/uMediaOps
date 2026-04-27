using uMediaOps.Models;
using Microsoft.Extensions.Logging;
using NPoco;
using Umbraco.Cms.Infrastructure.Scoping;

namespace uMediaOps.Repositories;

public interface IUnusedMediaScanResultRepository
{
    Task SaveAsync(UnusedMediaScanResult result);
    Task<UnusedMediaScanResult?> GetByIdAsync(Guid scanId);
    Task<UnusedMediaScanResult?> GetLatestAsync();
    Task<int> DeleteOldResultsAsync(DateTime olderThan);
}

public class UnusedMediaScanResultRepository : IUnusedMediaScanResultRepository
{
    private readonly IScopeProvider _scopeProvider;
    private readonly ILogger<UnusedMediaScanResultRepository> _logger;

    public UnusedMediaScanResultRepository(
        IScopeProvider scopeProvider,
        ILogger<UnusedMediaScanResultRepository> logger)
    {
        _scopeProvider = scopeProvider;
        _logger = logger;
    }

    public async Task SaveAsync(UnusedMediaScanResult result)
    {
        using var scope = _scopeProvider.CreateScope();
        var database = scope.Database;

        try
        {
            // Delete all previous scan results and their items — only keep the latest
            await database.ExecuteAsync("DELETE FROM uMediaOps_UnusedMediaItems");
            await database.ExecuteAsync("DELETE FROM uMediaOps_UnusedMediaScans");

            // Insert new scan result
            await database.InsertAsync(result);

            // Insert all unused media items
            foreach (var item in result.UnusedItems)
            {
                item.ScanId = result.Id;
                await database.InsertAsync(item);
            }

            scope.Complete();
            _logger.LogDebug("Saved scan result {ScanId} with {Count} unused items", result.Id, result.UnusedItems.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save scan result {ScanId}", result.Id);
            throw;
        }
    }

    public async Task<UnusedMediaScanResult?> GetByIdAsync(Guid scanId)
    {
        using var scope = _scopeProvider.CreateScope();
        var database = scope.Database;

        try
        {
            // Get the scan result
            var sql = scope.SqlContext.Sql()
                .Select<UnusedMediaScanResult>()
                .From<UnusedMediaScanResult>()
                .Where<UnusedMediaScanResult>(x => x.Id == scanId);

            var result = await database.FirstOrDefaultAsync<UnusedMediaScanResult>(sql);

            if (result != null)
            {
                // Load the unused media items
                var itemsSql = scope.SqlContext.Sql()
                    .Select<UnusedMediaItem>()
                    .From<UnusedMediaItem>()
                    .Where<UnusedMediaItem>(x => x.ScanId == scanId);

                result.UnusedItems = (await database.FetchAsync<UnusedMediaItem>(itemsSql)).ToList();
            }

            scope.Complete();
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get scan result {ScanId}", scanId);
            throw;
        }
    }

    public async Task<UnusedMediaScanResult?> GetLatestAsync()
    {
        using var scope = _scopeProvider.CreateScope();
        var database = scope.Database;

        try
        {
            // Get the most recent scan result
            var sql = scope.SqlContext.Sql()
                .Select<UnusedMediaScanResult>()
                .From<UnusedMediaScanResult>()
                .OrderByDescending<UnusedMediaScanResult>(x => x.ScannedAt);

            var result = await database.FirstOrDefaultAsync<UnusedMediaScanResult>(sql);

            if (result != null)
            {
                // Load the unused media items
                var itemsSql = scope.SqlContext.Sql()
                    .Select<UnusedMediaItem>()
                    .From<UnusedMediaItem>()
                    .Where<UnusedMediaItem>(x => x.ScanId == result.Id);

                result.UnusedItems = (await database.FetchAsync<UnusedMediaItem>(itemsSql)).ToList();
            }

            scope.Complete();
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get latest scan result");
            throw;
        }
    }

    public async Task<int> DeleteOldResultsAsync(DateTime olderThan)
    {
        using var scope = _scopeProvider.CreateScope();
        var database = scope.Database;

        try
        {
            // First delete orphaned items for scans we're about to remove
            var scansSql = scope.SqlContext.Sql()
                .Select<UnusedMediaScanResult>(x => x.Id)
                .From<UnusedMediaScanResult>()
                .Where<UnusedMediaScanResult>(x => x.ScannedAt < olderThan);

            var scanIds = await database.FetchAsync<Guid>(scansSql);

            foreach (var scanId in scanIds)
            {
                await database.ExecuteAsync(
                    "DELETE FROM uMediaOps_UnusedMediaItems WHERE ScanId = @0", scanId);
            }

            // Then delete the scan results
            var deleteSql = scope.SqlContext.Sql()
                .Delete()
                .From<UnusedMediaScanResult>()
                .Where<UnusedMediaScanResult>(x => x.ScannedAt < olderThan);

            var count = await database.ExecuteAsync(deleteSql);
            scope.Complete();

            _logger.LogDebug("Deleted {Count} old scan results and their items older than {Date}", count, olderThan);
            return count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete old scan results");
            throw;
        }
    }
}
