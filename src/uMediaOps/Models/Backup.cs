using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace uMediaOps.Models;

/// <summary>
/// Stores backup metadata and configuration
/// </summary>
[TableName("uMediaOps_Backups")]
[PrimaryKey("Id", AutoIncrement = true)]
public class Backup
{
    [PrimaryKeyColumn(AutoIncrement = true)]
    public int Id { get; set; }

    [Column("BackupId")]
    [Length(100)]
    [Index(IndexTypes.UniqueNonClustered, Name = "IX_uMediaOps_Backups_BackupId")]
    public string BackupId { get; set; } = string.Empty;

    [Column("BackupType")]
    [Length(50)]
    public string BackupType { get; set; } = "Full";

    [Column("StartedAt")]
    [Index(IndexTypes.NonClustered, Name = "IX_uMediaOps_Backups_StartedAt")]
    public DateTime StartedAt { get; set; }

    [Column("CompletedAt")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public DateTime? CompletedAt { get; set; }

    [Column("Status")]
    [Length(50)]
    [Index(IndexTypes.NonClustered, Name = "IX_uMediaOps_Backups_Status")]
    public string Status { get; set; } = "InProgress";

    [Column("FileCount")]
    public int FileCount { get; set; }

    [Column("TotalSize")]
    public long TotalSize { get; set; }

    [Column("CompressedSize")]
    public long CompressedSize { get; set; }

    [Column("StorageProvider")]
    [Length(100)]
    public string StorageProvider { get; set; } = "Local";

    [Column("StoragePath")]
    [Length(1000)]
    public string StoragePath { get; set; } = string.Empty;

    [Column("ManifestPath")]
    [Length(1000)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? ManifestPath { get; set; }

    [Column("Checksum")]
    [Length(64)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? Checksum { get; set; }

    [Column("BaseBackupId")]
    [Length(100)]
    [NullSetting(NullSetting = NullSettings.Null)]
    [Index(IndexTypes.NonClustered, Name = "IX_uMediaOps_Backups_BaseBackupId")]
    public string? BaseBackupId { get; set; }

    [Column("CreatedBy")]
    [Length(255)]
    public string CreatedBy { get; set; } = string.Empty;

    [Column("ErrorMessage")]
    [SpecialDbType(SpecialDbTypes.NVARCHARMAX)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? ErrorMessage { get; set; }

    [Column("ExpiresAt")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public DateTime? ExpiresAt { get; set; }
}
