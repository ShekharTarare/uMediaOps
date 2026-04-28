using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace uMediaOps.Models;

[TableName("uMediaOps_References")]
[PrimaryKey("Id", AutoIncrement = true)]
public class MediaReference
{
    [PrimaryKeyColumn(AutoIncrement = true)]
    public int Id { get; set; }

    [Column("MediaId")]
    [Index(IndexTypes.NonClustered, Name = "IX_uMediaOps_References_MediaId")]
    public int MediaId { get; set; }

    [Column("ContentId")]
    public int ContentId { get; set; }

    [Column("ContentName")]
    [Length(500)]
    public string ContentName { get; set; } = string.Empty;

    [Column("ContentType")]
    [Length(100)]
    public string ContentType { get; set; } = string.Empty;

    [Column("PropertyAlias")]
    [Length(100)]
    public string PropertyAlias { get; set; } = string.Empty;

    [Column("Url")]
    [Length(1000)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? Url { get; set; }

    [Column("LastChecked")]
    public DateTime LastChecked { get; set; }

    /// <summary>
    /// The type of reference: "Content", "Template", or "PartialView"
    /// </summary>
    [Column("ReferenceType")]
    [Length(50)]
    public string ReferenceType { get; set; } = "Content";

    /// <summary>
    /// Warning message for template references (optional)
    /// </summary>
    [Column("WarningMessage")]
    [Length(500)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? WarningMessage { get; set; }

    /// <summary>
    /// Indicates if this reference requires manual code updates
    /// </summary>
    [Column("RequiresManualUpdate")]
    public bool RequiresManualUpdate { get; set; } = false;

    /// <summary>
    /// Risk level for deleting the media item based on this reference.
    /// Stored as int in database.
    /// </summary>
    [Column("RiskLevel")]
    public int RiskLevelValue { get; set; }

    /// <summary>
    /// Typed access to the risk level enum.
    /// </summary>
    [Ignore]
    public RiskLevel RiskLevel
    {
        get => (RiskLevel)RiskLevelValue;
        set => RiskLevelValue = (int)value;
    }
}
