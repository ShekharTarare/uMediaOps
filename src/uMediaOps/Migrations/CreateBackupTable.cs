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

        Create.Table("uMediaOps_Backups")
            .WithColumn("Id").AsInt32().PrimaryKey("PK_uMediaOps_Backups").Identity()
            .WithColumn("BackupId").AsString(100).NotNullable()
            .WithColumn("BackupType").AsString(50).NotNullable().WithDefaultValue("Full")
            .WithColumn("StartedAt").AsDateTime().NotNullable()
            .WithColumn("CompletedAt").AsDateTime().Nullable()
            .WithColumn("Status").AsString(50).NotNullable().WithDefaultValue("InProgress")
            .WithColumn("FileCount").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("TotalSize").AsInt64().NotNullable().WithDefaultValue(0)
            .WithColumn("CompressedSize").AsInt64().NotNullable().WithDefaultValue(0)
            .WithColumn("StorageProvider").AsString(100).NotNullable().WithDefaultValue("Local")
            .WithColumn("StoragePath").AsString(1000).NotNullable()
            .WithColumn("ManifestPath").AsString(1000).Nullable()
            .WithColumn("Checksum").AsString(64).Nullable()
            .WithColumn("BaseBackupId").AsString(100).Nullable()
            .WithColumn("CreatedBy").AsString(255).NotNullable()
            .WithColumn("ErrorMessage").AsCustom("NVARCHAR(MAX)").Nullable()
            .WithColumn("ExpiresAt").AsDateTime().Nullable()
            .Do();

        Create.Index("IX_uMediaOps_Backups_BackupId")
            .OnTable("uMediaOps_Backups")
            .OnColumn("BackupId").Ascending()
            .WithOptions().Unique()
            .Do();

        Create.Index("IX_uMediaOps_Backups_StartedAt")
            .OnTable("uMediaOps_Backups")
            .OnColumn("StartedAt").Descending()
            .Do();

        Create.Index("IX_uMediaOps_Backups_Status")
            .OnTable("uMediaOps_Backups")
            .OnColumn("Status").Ascending()
            .Do();

        Logger.LogInformation("Created uMediaOps_Backups table");
        return Task.CompletedTask;
    }
}
