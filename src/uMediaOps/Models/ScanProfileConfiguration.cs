namespace uMediaOps.Models;

/// <summary>
/// Configuration metadata for a scan profile, including what file types to scan
/// and descriptive information for the UI.
/// </summary>
public class ScanProfileConfiguration
{
    /// <summary>
    /// The scan profile this configuration applies to.
    /// </summary>
    public ScanProfile Profile { get; set; }

    /// <summary>
    /// Display name for the profile (e.g., "Quick Scan").
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Detailed description of what this profile scans.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Estimated duration for this scan profile (e.g., "5-10 seconds").
    /// </summary>
    public string EstimatedDuration { get; set; } = string.Empty;

    /// <summary>
    /// Recommended use case for this profile.
    /// </summary>
    public string UseCase { get; set; } = string.Empty;

    // Scan flags - what file types to include
    public bool ScanContent { get; set; }
    public bool ScanViews { get; set; }
    public bool ScanJavaScript { get; set; }
    public bool ScanCss { get; set; }
    public bool ScanWwwroot { get; set; }
    public bool ScanTypeScript { get; set; }
    public bool ScanScss { get; set; }
    public bool ScanConfig { get; set; }

    /// <summary>
    /// Gets the configuration for a specific scan profile.
    /// </summary>
    public static ScanProfileConfiguration GetConfiguration(ScanProfile profile)
    {
        return profile switch
        {
            ScanProfile.Quick => new ScanProfileConfiguration
            {
                Profile = ScanProfile.Quick,
                Name = "Quick Scan",
                Description = "Scans only content items (media pickers, rich text editors). Fastest option for quick checks.",
                EstimatedDuration = "5-10 seconds",
                UseCase = "Frequent scans and quick checks",
                ScanContent = true,
                ScanViews = false,
                ScanJavaScript = false,
                ScanCss = false,
                ScanWwwroot = false,
                ScanTypeScript = false,
                ScanScss = false,
                ScanConfig = false
            },
            ScanProfile.Deep => new ScanProfileConfiguration
            {
                Profile = ScanProfile.Deep,
                Name = "Deep Scan",
                Description = "Scans content items + all files in Views folder + JavaScript + CSS. More thorough than Quick Scan.",
                EstimatedDuration = "30-60 seconds",
                UseCase = "Regular maintenance and before cleanup operations",
                ScanContent = true,
                ScanViews = true,
                ScanJavaScript = true,
                ScanCss = true,
                ScanWwwroot = false,
                ScanTypeScript = false,
                ScanScss = false,
                ScanConfig = false
            },
            ScanProfile.Complete => new ScanProfileConfiguration
            {
                Profile = ScanProfile.Complete,
                Name = "Complete Scan",
                Description = "Scans everything: content, Views, JS, CSS, wwwroot, TypeScript, SCSS, and config files. Most thorough option.",
                EstimatedDuration = "2-5 minutes",
                UseCase = "Comprehensive audits and before major cleanup operations",
                ScanContent = true,
                ScanViews = true,
                ScanJavaScript = true,
                ScanCss = true,
                ScanWwwroot = true,
                ScanTypeScript = true,
                ScanScss = true,
                ScanConfig = true
            },
            _ => throw new ArgumentException($"Unknown scan profile: {profile}", nameof(profile))
        };
    }

    /// <summary>
    /// Gets all available scan profile configurations.
    /// </summary>
    public static IEnumerable<ScanProfileConfiguration> GetAllConfigurations()
    {
        return new[]
        {
            GetConfiguration(ScanProfile.Quick),
            GetConfiguration(ScanProfile.Deep),
            GetConfiguration(ScanProfile.Complete)
        };
    }
}
