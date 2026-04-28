using Microsoft.Extensions.Logging;
using Umbraco.Cms.Infrastructure.Migrations;

namespace uMediaOps.Migrations;

/// <summary>
/// Creates the uMediaOps_Backups table for backup/export functionality.
/// </summary>
public class CreateBackupTable : AsyncMigrationBase
{
    public CreateBackupTable(IMigrationContext context) : base(context) { }

    protected override Task MigrateAsync()
    {
        if (TableExists("uMediaOps_Backups"))
            return Task.CompletedTask;

        Create.Table<Models.Backup>().Do();

        Logger.LogInformation("Created uMediaOps_Backups table");
        return Task.CompletedTask;
    }
}
