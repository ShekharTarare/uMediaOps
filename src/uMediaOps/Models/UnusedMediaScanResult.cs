using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace uMediaOps.Models;

/// <summary>
/// Stores the results of an unused media scan
/// </summary>
[TableName("uMediaOps_UnusedMediaScans")]
[PrimaryKey("Id", AutoIncrement = false)]
public class UnusedMediaScanResult
{
    [PrimaryKeyColumn(AutoIncrement = false)]
    public Guid Id { get; set; }

    public DateTime ScannedAt { get; set; }

    [Length(255)]
    public string ScannedBy { get; set; } = string.Empty;

    public int TotalScanned { get; set; }

    public int UnusedCount { get; set; }

    public long TotalStorageWasted { get; set; }

    public bool IsComplete { get; set; }

    public int ContentItemsScanned { get; set; }

    public int TemplatesScanned { get; set; }

    public int PartialViewsScanned { get; set; }

    public bool IncludedTemplates { get; set; }

    /// <summary>
    /// The scan profile used for this scan (Quick=0, Deep=1, Complete=2).
    /// </summary>
    [Column("Profile")]
    public int ProfileValue { get; set; }

    /// <summary>
    /// Typed access to the scan profile enum.
    /// </summary>
    [Ignore]
    public ScanProfile Profile
    {
        get => (ScanProfile)ProfileValue;
        set => ProfileValue = (int)value;
    }

    public int DurationSeconds { get; set; }

    public int ItemsWithCodeReferences { get; set; }

    /// <summary>
    /// Detailed breakdown of file types scanned.
    /// Not stored in database - computed from individual statistics columns.
    /// </summary>
    [Ignore]
    public FileTypeScanStatistics FileTypeBreakdown => new()
    {
        ContentItemsScanned = this.ContentItemsScanned,
        ViewFilesScanned = this.ViewFilesScanned,
        TemplatesScanned = this.TemplatesScanned,
        PartialViewsScanned = this.PartialViewsScanned,
        BlockComponentsScanned = this.BlockComponentsScanned,
        LayoutsScanned = this.LayoutsScanned,
        JavaScriptFilesScanned = this.JavaScriptFilesScanned,
        CssFilesScanned = this.CssFilesScanned,
        TypeScriptFilesScanned = this.TypeScriptFilesScanned,
        ScssFilesScanned = this.ScssFilesScanned,
        ConfigFilesScanned = this.ConfigFilesScanned,
        WwwrootFilesScanned = this.WwwrootFilesScanned
    };

    public int ViewFilesScanned { get; set; }

    public int BlockComponentsScanned { get; set; }

    public int LayoutsScanned { get; set; }

    public int JavaScriptFilesScanned { get; set; }

    public int CssFilesScanned { get; set; }

    public int TypeScriptFilesScanned { get; set; }

    public int ScssFilesScanned { get; set; }

    public int ConfigFilesScanned { get; set; }

    public int WwwrootFilesScanned { get; set; }

    /// <summary>
    /// Human-readable profile name
    /// </summary>
    [Ignore]
    public string ProfileName => Profile switch
    {
        ScanProfile.Quick => "Quick Scan",
        ScanProfile.Deep => "Deep Scan",
        ScanProfile.Complete => "Complete Scan",
        _ => "Unknown"
    };

    /// <summary>
    /// Collection of unused media items found in this scan.
    /// Not stored in database - loaded separately.
    /// </summary>
    [Ignore]
    public List<UnusedMediaItem> UnusedItems { get; set; } = new();
}
