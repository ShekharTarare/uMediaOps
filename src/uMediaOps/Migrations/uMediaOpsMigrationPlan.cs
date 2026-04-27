using Umbraco.Cms.Core.Packaging;

namespace uMediaOps.Migrations;

/// <summary>
/// Migration plan for uMediaOps database schema.
/// Each step runs once and is tracked in the umbracoKeyValue table.
/// </summary>
public class uMediaOpsMigrationPlan : PackageMigrationPlan
{
    public uMediaOpsMigrationPlan() : base("uMediaOps") { }

    protected override void DefinePlan()
    {
        From(string.Empty)
            .To<CreateFileHashesTable>("create-file-hashes-table")
            .To<CreateUnusedMediaTables>("create-unused-media-tables")
            .To<CreateAnalyticsTable>("create-analytics-table")
            .To<CreateAuditLogTable>("create-audit-log-table")
            .To<CreateBackupTable>("create-backup-table")
            .To<CreateReferencesTable>("create-references-table");
    }
}
