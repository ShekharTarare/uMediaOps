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

        Create.Table("uMediaOps_References")
            .WithColumn("Id").AsInt32().PrimaryKey().Identity()
            .WithColumn("MediaId").AsInt32().NotNullable()
            .WithColumn("ContentId").AsInt32().NotNullable()
            .WithColumn("ContentName").AsString(500).NotNullable()
            .WithColumn("ContentType").AsString(100).NotNullable()
            .WithColumn("PropertyAlias").AsString(100).NotNullable()
            .WithColumn("Url").AsString(1000).Nullable()
            .WithColumn("LastChecked").AsDateTime().NotNullable()
            .WithColumn("ReferenceType").AsString(50).NotNullable().WithDefaultValue("Content")
            .WithColumn("WarningMessage").AsString(500).Nullable()
            .WithColumn("RequiresManualUpdate").AsBoolean().NotNullable().WithDefaultValue(false)
            .WithColumn("RiskLevel").AsInt32().NotNullable().WithDefaultValue(0)
            .Do();

        Create.Index("IX_uMediaOps_References_MediaId")
            .OnTable("uMediaOps_References")
            .OnColumn("MediaId").Ascending()
            .Do();

        Logger.LogInformation("Created uMediaOps_References table");
        return Task.CompletedTask;
    }
}
