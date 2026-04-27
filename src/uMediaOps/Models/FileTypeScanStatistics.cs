namespace uMediaOps.Models;

/// <summary>
/// Statistics about what file types were scanned during an unused media scan.
/// Provides a detailed breakdown of scan coverage.
/// </summary>
public class FileTypeScanStatistics
{
    /// <summary>
    /// Number of content items scanned (documents with media pickers, rich text editors).
    /// </summary>
    public int ContentItemsScanned { get; set; }

    /// <summary>
    /// Number of view files scanned (.cshtml files in Views folder).
    /// Includes templates, partial views, block components, and layouts.
    /// </summary>
    public int ViewFilesScanned { get; set; }

    /// <summary>
    /// Number of templates scanned (subset of ViewFilesScanned).
    /// </summary>
    public int TemplatesScanned { get; set; }

    /// <summary>
    /// Number of partial views scanned (subset of ViewFilesScanned).
    /// </summary>
    public int PartialViewsScanned { get; set; }

    /// <summary>
    /// Number of block components scanned (subset of ViewFilesScanned).
    /// </summary>
    public int BlockComponentsScanned { get; set; }

    /// <summary>
    /// Number of layouts scanned (subset of ViewFilesScanned).
    /// </summary>
    public int LayoutsScanned { get; set; }

    /// <summary>
    /// Number of JavaScript files scanned (.js files).
    /// </summary>
    public int JavaScriptFilesScanned { get; set; }

    /// <summary>
    /// Number of CSS files scanned (.css files).
    /// </summary>
    public int CssFilesScanned { get; set; }

    /// <summary>
    /// Number of TypeScript files scanned (.ts, .tsx files).
    /// </summary>
    public int TypeScriptFilesScanned { get; set; }

    /// <summary>
    /// Number of SCSS/LESS files scanned (.scss, .less files).
    /// </summary>
    public int ScssFilesScanned { get; set; }

    /// <summary>
    /// Number of configuration files scanned (appsettings.json, web.config).
    /// </summary>
    public int ConfigFilesScanned { get; set; }

    /// <summary>
    /// Number of files scanned in wwwroot directory.
    /// </summary>
    public int WwwrootFilesScanned { get; set; }

    /// <summary>
    /// Total number of files scanned across all types.
    /// </summary>
    public int TotalFilesScanned =>
        ContentItemsScanned +
        ViewFilesScanned +
        JavaScriptFilesScanned +
        CssFilesScanned +
        TypeScriptFilesScanned +
        ScssFilesScanned +
        ConfigFilesScanned +
        WwwrootFilesScanned;
}
