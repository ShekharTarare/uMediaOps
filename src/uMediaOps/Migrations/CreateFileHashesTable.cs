using Microsoft.Extensions.Logging;
using Umbraco.Cms.Infrastructure.Migrations;

namespace uMediaOps.Migrations;

/// <summary>
/// Creates the uMediaOps_FileHashes table for duplicate detection via SHA256 hashing.
/// </summary>
public class CreateFileHashesTable : AsyncMigrationBase
{
    public CreateFileHashesTable(IMigrationContext context) : base(context) { }

    protected override Task MigrateAsync()
    {
        if (TableExists("uMediaOps_FileHashes"))
            return Task.CompletedTask;

        Create.Table<Models.FileHash>().Do();

        Logger.LogInformation("Created uMediaOps_FileHashes table");
        return Task.CompletedTask;
    }
}
