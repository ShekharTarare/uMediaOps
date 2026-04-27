namespace uMediaOps.Models;

/// <summary>
/// Represents the progress of an ongoing scan operation
/// </summary>
public class ScanProgress
{
    /// <summary>
    /// Number of media items processed so far
    /// </summary>
    public int Processed { get; set; }

    /// <summary>
    /// Total number of media items to process
    /// </summary>
    public int Total { get; set; }

    /// <summary>
    /// Percentage of completion (0-100)
    /// </summary>
    public int Percentage { get; set; }

    /// <summary>
    /// Indicates if the scan has completed
    /// </summary>
    public bool IsComplete { get; set; }

    /// <summary>
    /// Current file being processed
    /// </summary>
    public string CurrentFile { get; set; } = string.Empty;
}
