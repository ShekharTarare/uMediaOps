using Microsoft.Extensions.Logging;
using Umbraco.Cms.Infrastructure.Migrations;

namespace uMediaOps.Migrations;

/// <summary>
/// Creates the uMediaOps_Analytics table for tracking duplicate trends and storage savings.
/// </summary>
public class CreateAnalyticsTable : AsyncMigrationBase
{
    public CreateAnalyticsTable(IMigrationContext context) : base(context) { }

    protected override Task MigrateAsync()
    {
        if (TableExists("uMediaOps_Analytics"))
            return Task.CompletedTask;

        Create.Table<Models.AnalyticsData>().Do();

        Logger.LogInformation("Created uMediaOps_Analytics table");
        return Task.CompletedTask;
    }
}
