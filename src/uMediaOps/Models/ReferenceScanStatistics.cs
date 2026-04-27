namespace uMediaOps.Models;

/// <summary>
/// Statistics about what was scanned when checking for media references
/// </summary>
public class ReferenceScanStatistics
{
    /// <summary>
    /// Number of content items scanned
    /// </summary>
    public int ContentItemsScanned { get; set; }

    /// <summary>
    /// Number of templates scanned (0 if template scanning disabled)
    /// </summary>
    public int TemplatesScanned { get; set; }

    /// <summary>
    /// Number of partial views scanned (0 if template scanning disabled)
    /// </summary>
    public int PartialViewsScanned { get; set; }

    /// <summary>
    /// Number of view files scanned (.cshtml files in Views folder)
    /// </summary>
    public int ViewFilesScanned { get; set; }

    /// <summary>
    /// Number of JavaScript files scanned
    /// </summary>
    public int JavaScriptFilesScanned { get; set; }

    /// <summary>
    /// Number of CSS files scanned
    /// </summary>
    public int CssFilesScanned { get; set; }

    /// <summary>
    /// Number of TypeScript files scanned
    /// </summary>
    public int TypeScriptFilesScanned { get; set; }

    /// <summary>
    /// Number of SCSS/LESS files scanned
    /// </summary>
    public int ScssFilesScanned { get; set; }

    /// <summary>
    /// Number of files scanned in wwwroot directory
    /// </summary>
    public int WwwrootFilesScanned { get; set; }

    /// <summary>
    /// Number of configuration files scanned
    /// </summary>
    public int ConfigFilesScanned { get; set; }

    /// <summary>
    /// Total items scanned (all file types)
    /// </summary>
    public int TotalItemsScanned => 
        ContentItemsScanned + 
        TemplatesScanned + 
        PartialViewsScanned +
        ViewFilesScanned +
        JavaScriptFilesScanned +
        CssFilesScanned +
        TypeScriptFilesScanned +
        ScssFilesScanned +
        WwwrootFilesScanned +
        ConfigFilesScanned;
}
