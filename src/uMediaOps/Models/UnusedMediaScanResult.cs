using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace uMediaOps.Models;

/// <summary>
/// Stores the results of an unused media scan
/// </summary>
[TableName("uMediaOps_UnusedMediaScans")]
[PrimaryKey("Id", AutoIncrement = false)]
[ExplicitColumns]
public class UnusedMediaScanResult
{
    /// <summary>
    /// Primary key - unique identifier for this scan
    /// </summary>
    [Column]
    [PrimaryKeyColumn(AutoIncrement = false)]
    public Guid Id { get; set; }

    /// <summary>
    /// Timestamp when the scan was performed
    /// </summary>
    [Column]
    public DateTime ScannedAt { get; set; }

    /// <summary>
    /// User ID or name who initiated the scan
    /// </summary>
    [Column]
    [Length(255)]
    public string ScannedBy { get; set; } = string.Empty;

    /// <summary>
    /// Total number of media items scanned
    /// </summary>
    [Column]
    public int TotalScanned { get; set; }

    /// <summary>
    /// Number of unused media items found
    /// </summary>
    [Column]
    public int UnusedCount { get; set; }

    /// <summary>
    /// Total storage space wasted by unused media (in bytes)
    /// </summary>
    [Column]
    public long TotalStorageWasted { get; set; }

    /// <summary>
    /// Indicates if the scan completed successfully
    /// </summary>
    [Column]
    public bool IsComplete { get; set; }

    /// <summary>
    /// Number of content items scanned for references
    /// </summary>
    [Column]
    public int ContentItemsScanned { get; set; }

    /// <summary>
    /// Number of templates scanned for references (0 if template scanning disabled)
    /// </summary>
    [Column]
    public int TemplatesScanned { get; set; }

    /// <summary>
    /// Number of partial views scanned for references (0 if template scanning disabled)
    /// </summary>
    [Column]
    public int PartialViewsScanned { get; set; }

    /// <summary>
    /// Whether template scanning was enabled for this scan
    /// </summary>
    [Column]
    public bool IncludedTemplates { get; set; }

    /// <summary>
    /// The scan profile used for this scan (Quick, Deep, Complete)
    /// </summary>
    [Column("Profile")]
    public ScanProfile Profile { get; set; } = ScanProfile.Quick;

    /// <summary>
    /// Duration of the scan in seconds
    /// </summary>
    [Column]
    public int DurationSeconds { get; set; }

    /// <summary>
    /// Number of unused items that have references in code files (Views, JS, CSS, etc.)
    /// </summary>
    [Column]
    public int ItemsWithCodeReferences { get; set; }

    /// <summary>
    /// Detailed breakdown of file types scanned
    /// Not stored in database - computed from individual statistics columns
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

    /// <summary>
    /// Number of view files scanned (.cshtml files in Views folder)
    /// </summary>
    [Column]
    public int ViewFilesScanned { get; set; }

    /// <summary>
    /// Number of block components scanned
    /// </summary>
    [Column]
    public int BlockComponentsScanned { get; set; }

    /// <summary>
    /// Number of layouts scanned
    /// </summary>
    [Column]
    public int LayoutsScanned { get; set; }

    /// <summary>
    /// Number of JavaScript files scanned
    /// </summary>
    [Column]
    public int JavaScriptFilesScanned { get; set; }

    /// <summary>
    /// Number of CSS files scanned
    /// </summary>
    [Column]
    public int CssFilesScanned { get; set; }

    /// <summary>
    /// Number of TypeScript files scanned
    /// </summary>
    [Column]
    public int TypeScriptFilesScanned { get; set; }

    /// <summary>
    /// Number of SCSS/LESS files scanned
    /// </summary>
    [Column]
    public int ScssFilesScanned { get; set; }

    /// <summary>
    /// Number of configuration files scanned
    /// </summary>
    [Column]
    public int ConfigFilesScanned { get; set; }

    /// <summary>
    /// Number of files scanned in wwwroot directory
    /// </summary>
    [Column]
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
    /// Collection of unused media items found in this scan
    /// Not stored in database - loaded separately
    /// </summary>
    [Ignore]
    public List<UnusedMediaItem> UnusedItems { get; set; } = new();
}
