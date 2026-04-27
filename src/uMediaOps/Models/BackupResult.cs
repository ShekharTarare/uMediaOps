namespace uMediaOps.Models;

/// <summary>
/// Result of a backup operation
/// </summary>
public class BackupResult
{
    public string BackupId { get; set; } = string.Empty;
    public string BackupType { get; set; } = string.Empty;
    public int FileCount { get; set; }
    public long TotalSize { get; set; }
    public long CompressedSize { get; set; }
    public string StorageProvider { get; set; } = string.Empty;
    public string StoragePath { get; set; } = string.Empty;
    public TimeSpan Duration { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
}
