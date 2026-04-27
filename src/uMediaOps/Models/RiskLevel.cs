namespace uMediaOps.Models;

/// <summary>
/// Risk level for deleting a media item based on where it's referenced.
/// Helps users make informed decisions about which items are safe to delete.
/// </summary>
public enum RiskLevel
{
    /// <summary>
    /// Safe to delete: No references found in any scanned locations.
    /// Green indicator in UI.
    /// </summary>
    Safe = 0,

    /// <summary>
    /// Review recommended: References found in code files (Views, JS, CSS, etc.).
    /// These might be hardcoded references that won't break content but could affect functionality.
    /// Yellow indicator in UI.
    /// </summary>
    Review = 1,

    /// <summary>
    /// High risk: References found in content items (media pickers, rich text editors).
    /// Deleting will likely break content and should be avoided.
    /// Red indicator in UI. Deletion should be disabled for these items.
    /// </summary>
    HighRisk = 2
}
