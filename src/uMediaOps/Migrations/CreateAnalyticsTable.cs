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

        Create.Table("uMediaOps_Analytics")
            .WithColumn("Id").AsInt32().PrimaryKey("PK_uMediaOps_Analytics").Identity()
            .WithColumn("RecordedAt").AsDateTime().NotNullable()
            .WithColumn("EventType").AsString(50).NotNullable()
            .WithColumn("DuplicateCount").AsInt32().NotNullable()
            .WithColumn("StorageWasted").AsInt64().NotNullable()
            .WithColumn("StorageFreed").AsInt64().NotNullable()
            .WithColumn("Metadata").AsCustom("NVARCHAR(MAX)").Nullable()
            .Do();

        Create.Index("IX_uMediaOps_Analytics_RecordedAt")
            .OnTable("uMediaOps_Analytics")
            .OnColumn("RecordedAt").Descending()
            .Do();

        Create.Index("IX_uMediaOps_Analytics_EventType")
            .OnTable("uMediaOps_Analytics")
            .OnColumn("EventType").Ascending()
            .Do();

        Logger.LogInformation("Created uMediaOps_Analytics table");
        return Task.CompletedTask;
    }
}
