namespace uMediaOps.Models;

/// <summary>
/// Progress information for backup operations
/// </summary>
public class BackupProgress
{
    public int TotalFiles { get; set; }
    public int ProcessedFiles { get; set; }
    public double PercentageComplete { get; set; }
    public string? CurrentFile { get; set; }
    public long TotalSize { get; set; }
    public long ProcessedSize { get; set; }
}
