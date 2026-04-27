namespace uMediaOps.Models;

/// <summary>
/// Defines the scan profile types for unused media detection.
/// Each profile represents a different level of thoroughness and scan duration.
/// </summary>
public enum ScanProfile
{
    /// <summary>
    /// Quick Scan: Scans only content items (media pickers, rich text editors).
    /// Fastest option, typically completes in 5-10 seconds.
    /// Recommended for frequent scans and quick checks.
    /// </summary>
    Quick = 0,

    /// <summary>
    /// Deep Scan: Scans content items + all files in Views folder + JavaScript + CSS.
    /// Moderate speed, typically completes in 30-60 seconds.
    /// Recommended for regular maintenance and before cleanup operations.
    /// </summary>
    Deep = 1,

    /// <summary>
    /// Complete Scan: Scans everything including wwwroot, TypeScript, SCSS, and config files.
    /// Most thorough but slowest, typically completes in 2-5 minutes.
    /// Recommended for comprehensive audits and before major cleanup operations.
    /// </summary>
    Complete = 2
}
