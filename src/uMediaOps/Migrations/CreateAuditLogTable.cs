using Microsoft.Extensions.Logging;
using Umbraco.Cms.Infrastructure.Migrations;

namespace uMediaOps.Migrations;

/// <summary>
/// Creates the uMediaOps_AuditLog table for tracking all media operations.
/// </summary>
public class CreateAuditLogTable : AsyncMigrationBase
{
    public CreateAuditLogTable(IMigrationContext context) : base(context) { }

    protected override Task MigrateAsync()
    {
        if (TableExists("uMediaOps_AuditLog"))
            return Task.CompletedTask;

        Create.Table<Models.AuditLogEntry>().Do();

        Logger.LogInformation("Created uMediaOps_AuditLog table");
        return Task.CompletedTask;
    }
}
