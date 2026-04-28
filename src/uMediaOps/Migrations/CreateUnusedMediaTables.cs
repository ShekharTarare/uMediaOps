using Microsoft.Extensions.Logging;
using Umbraco.Cms.Infrastructure.Migrations;

namespace uMediaOps.Migrations;

/// <summary>
/// Creates tables for the Unused Media Finder feature:
/// - uMediaOps_UnusedMediaScans: scan results with profile and file type statistics.
/// - uMediaOps_UnusedMediaItems: individual unused media items per scan.
/// </summary>
public class CreateUnusedMediaTables : AsyncMigrationBase
{
    public CreateUnusedMediaTables(IMigrationContext context) : base(context) { }

    protected override Task MigrateAsync()
    {
        if (!TableExists("uMediaOps_UnusedMediaScans"))
        {
            Create.Table<Models.UnusedMediaScanResult>().Do();
            Logger.LogInformation("Created uMediaOps_UnusedMediaScans table");
        }

        if (!TableExists("uMediaOps_UnusedMediaItems"))
        {
            Create.Table<Models.UnusedMediaItem>().Do();
            Logger.LogInformation("Created uMediaOps_UnusedMediaItems table");
        }

        return Task.CompletedTask;
    }
}
