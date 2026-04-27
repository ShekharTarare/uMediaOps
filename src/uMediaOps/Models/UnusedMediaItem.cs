using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace uMediaOps.Models;

/// <summary>
/// Represents a single unused media item found during a scan
/// </summary>
[TableName("uMediaOps_UnusedMediaItems")]
[PrimaryKey("Id", AutoIncrement = true)]
public class UnusedMediaItem
{
    /// <summary>
    /// Primary key - auto-incrementing ID
    /// </summary>
    [PrimaryKeyColumn(AutoIncrement = true)]
    public int Id { get; set; }

    /// <summary>
    /// Foreign key to the scan that found this item
    /// </summary>
    [Index(IndexTypes.NonClustered, Name = "IX_uMediaOps_UnusedMediaItems_ScanId")]
    public Guid ScanId { get; set; }

    /// <summary>
    /// Umbraco media item ID
    /// </summary>
    [Index(IndexTypes.NonClustered, Name = "IX_uMediaOps_UnusedMediaItems_MediaId")]
    public int MediaId { get; set; }

    /// <summary>
    /// File name
    /// </summary>
    [Length(500)]
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// Full file path
    /// </summary>
    [Length(1000)]
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// File size in bytes
    /// </summary>
    public long FileSize { get; set; }

    /// <summary>
    /// Date when the file was uploaded
    /// </summary>
    public DateTime UploadDate { get; set; }

    /// <summary>
    /// File type/extension
    /// </summary>
    [Length(100)]
    public string FileType { get; set; } = string.Empty;

    /// <summary>
    /// Folder path in media library
    /// </summary>
    [Length(500)]
    public string FolderPath { get; set; } = string.Empty;

    /// <summary>
    /// Indicates if there's a warning about this item
    /// </summary>
    public bool HasWarning { get; set; }

    /// <summary>
    /// Warning message if HasWarning is true
    /// </summary>
    [Length(1000)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? WarningMessage { get; set; }
}
