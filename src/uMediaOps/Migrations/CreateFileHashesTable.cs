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

        Create.Table("uMediaOps_FileHashes")
            .WithColumn("Id").AsInt32().PrimaryKey().Identity()
            .WithColumn("MediaId").AsInt32().NotNullable()
            .WithColumn("Hash").AsString(128).NotNullable()
            .WithColumn("FileSize").AsInt64().NotNullable()
            .WithColumn("ComputedAt").AsDateTime().NotNullable()
            .WithColumn("IsManuallySelectedOriginal").AsBoolean().NotNullable().WithDefaultValue(false)
            .Do();

        Create.Index("IX_uMediaOps_FileHashes_MediaId")
            .OnTable("uMediaOps_FileHashes")
            .OnColumn("MediaId").Ascending()
            .Do();

        Create.Index("IX_uMediaOps_FileHashes_Hash")
            .OnTable("uMediaOps_FileHashes")
            .OnColumn("Hash").Ascending()
            .Do();

        Logger.LogInformation("Created uMediaOps_FileHashes table");
        return Task.CompletedTask;
    }
}
