using Microsoft.Extensions.Logging;
using Umbraco.Cms.Infrastructure.Migrations;

namespace uMediaOps.Migrations;

/// <summary>
/// Creates the uMediaOps_References table for tracking media references
/// across content, templates, and code files.
/// </summary>
public class CreateReferencesTable : AsyncMigrationBase
{
    public CreateReferencesTable(IMigrationContext context) : base(context) { }

    protected override Task MigrateAsync()
    {
        if (TableExists("uMediaOps_References"))
            return Task.CompletedTask;

        Create.Table<Models.MediaReference>().Do();

        Logger.LogInformation("Created uMediaOps_References table");
        return Task.CompletedTask;
    }
}
