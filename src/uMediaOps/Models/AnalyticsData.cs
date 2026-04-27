using NPoco;

namespace uMediaOps.Models;

/// <summary>
/// Database model for analytics records
/// </summary>
[TableName("uMediaOps_Analytics")]
[PrimaryKey("Id", AutoIncrement = true)]
public class AnalyticsData
{
    public int Id { get; set; }
    public DateTime RecordedAt { get; set; }
    public string EventType { get; set; } = string.Empty; // "Scan", "Deletion"
    public int DuplicateCount { get; set; }
    public long StorageWasted { get; set; }
    public long StorageFreed { get; set; }
    public string? Metadata { get; set; } // JSON for additional data
}

/// <summary>
/// File type breakdown statistics
/// </summary>
public class FileTypeBreakdown
{
    public List<FileTypeStatistic> Statistics { get; set; } = new();
}

/// <summary>
/// Statistics for a specific file type
/// </summary>
public class FileTypeStatistic
{
    public string FileType { get; set; } = string.Empty;
    public int Count { get; set; }
    public long TotalSize { get; set; }
    public double Percentage { get; set; }
}

/// <summary>
/// Storage savings history
/// </summary>
public class StorageSavingsHistory
{
    public List<SavingsDataPoint> DataPoints { get; set; } = new();
    public long TotalSaved { get; set; }
}

/// <summary>
/// Savings data point
/// </summary>
public class SavingsDataPoint
{
    public DateTime Date { get; set; }
    public long SpaceFreed { get; set; }
    public int FilesDeleted { get; set; }
}
