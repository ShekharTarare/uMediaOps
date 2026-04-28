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
            Create.Table("uMediaOps_UnusedMediaScans")
                .WithColumn("Id").AsString(36).PrimaryKey("PK_uMediaOps_UnusedMediaScans").NotNullable()
                .WithColumn("ScannedAt").AsDateTime().NotNullable()
                .WithColumn("ScannedBy").AsString(255).NotNullable()
                .WithColumn("TotalScanned").AsInt32().NotNullable()
                .WithColumn("UnusedCount").AsInt32().NotNullable()
                .WithColumn("TotalStorageWasted").AsInt64().NotNullable()
                .WithColumn("IsComplete").AsBoolean().NotNullable()
                .WithColumn("ContentItemsScanned").AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn("TemplatesScanned").AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn("PartialViewsScanned").AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn("IncludedTemplates").AsBoolean().NotNullable().WithDefaultValue(false)
                .WithColumn("Profile").AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn("DurationSeconds").AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn("ItemsWithCodeReferences").AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn("ViewFilesScanned").AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn("BlockComponentsScanned").AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn("LayoutsScanned").AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn("JavaScriptFilesScanned").AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn("CssFilesScanned").AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn("TypeScriptFilesScanned").AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn("ScssFilesScanned").AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn("ConfigFilesScanned").AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn("WwwrootFilesScanned").AsInt32().NotNullable().WithDefaultValue(0)
                .Do();

            Create.Index("IX_uMediaOps_UnusedMediaScans_ScannedAt")
                .OnTable("uMediaOps_UnusedMediaScans")
                .OnColumn("ScannedAt").Descending()
                .Do();

            Logger.LogInformation("Created uMediaOps_UnusedMediaScans table");
        }

        if (!TableExists("uMediaOps_UnusedMediaItems"))
        {
            Create.Table("uMediaOps_UnusedMediaItems")
                .WithColumn("Id").AsInt32().PrimaryKey("PK_uMediaOps_UnusedMediaItems").Identity()
                .WithColumn("ScanId").AsGuid().NotNullable()
                .WithColumn("MediaId").AsInt32().NotNullable()
                .WithColumn("FileName").AsString(500).NotNullable()
                .WithColumn("FilePath").AsString(1000).NotNullable()
                .WithColumn("FileSize").AsInt64().NotNullable()
                .WithColumn("UploadDate").AsDateTime().NotNullable()
                .WithColumn("FileType").AsString(100).NotNullable()
                .WithColumn("FolderPath").AsString(500).NotNullable()
                .WithColumn("HasWarning").AsBoolean().NotNullable().WithDefaultValue(false)
                .WithColumn("WarningMessage").AsString(1000).Nullable()
                .Do();

            Create.Index("IX_uMediaOps_UnusedMediaItems_ScanId")
                .OnTable("uMediaOps_UnusedMediaItems")
                .OnColumn("ScanId").Ascending()
                .Do();

            Create.Index("IX_uMediaOps_UnusedMediaItems_MediaId")
                .OnTable("uMediaOps_UnusedMediaItems")
                .OnColumn("MediaId").Ascending()
                .Do();

            Logger.LogInformation("Created uMediaOps_UnusedMediaItems table");
        }

        return Task.CompletedTask;
    }
}
