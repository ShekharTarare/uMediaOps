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

        Create.Table("uMediaOps_AuditLog")
            .WithColumn("Id").AsInt32().PrimaryKey("PK_uMediaOps_AuditLog").Identity()
            .WithColumn("Timestamp").AsDateTime().NotNullable()
            .WithColumn("Action").AsString(100).NotNullable()
            .WithColumn("MediaId").AsInt32().Nullable()
            .WithColumn("MediaName").AsString(500).Nullable()
            .WithColumn("UserId").AsInt32().Nullable()
            .WithColumn("UserName").AsString(255).NotNullable()
            .WithColumn("Details").AsCustom("NVARCHAR(MAX)").Nullable()
            .WithColumn("Success").AsBoolean().NotNullable()
            .WithColumn("ErrorMessage").AsCustom("NVARCHAR(MAX)").Nullable()
            .Do();

        Create.Index("IX_uMediaOps_AuditLog_Timestamp")
            .OnTable("uMediaOps_AuditLog")
            .OnColumn("Timestamp").Descending()
            .Do();

        Logger.LogInformation("Created uMediaOps_AuditLog table");
        return Task.CompletedTask;
    }
}
