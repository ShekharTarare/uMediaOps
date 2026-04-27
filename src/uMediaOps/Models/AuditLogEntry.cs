using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace uMediaOps.Models;

[TableName("uMediaOps_AuditLog")]
[PrimaryKey("Id", AutoIncrement = true)]
public class AuditLogEntry
{
    [PrimaryKeyColumn(AutoIncrement = true)]
    public int Id { get; set; }

    [Column("Timestamp")]
    [Index(IndexTypes.NonClustered, Name = "IX_uMediaOps_AuditLog_Timestamp")]
    public DateTime Timestamp { get; set; }

    [Column("Action")]
    [Length(100)]
    public string Action { get; set; } = string.Empty;

    [Column("MediaId")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public int? MediaId { get; set; }

    [Column("MediaName")]
    [Length(500)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? MediaName { get; set; }

    [Column("UserId")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public int? UserId { get; set; }

    [Column("UserName")]
    [Length(255)]
    public string UserName { get; set; } = string.Empty;

    [Column("Details")]
    [SpecialDbType(SpecialDbTypes.NVARCHARMAX)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? Details { get; set; }

    [Column("Success")]
    public bool Success { get; set; }

    [Column("ErrorMessage")]
    [SpecialDbType(SpecialDbTypes.NVARCHARMAX)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? ErrorMessage { get; set; }
}
