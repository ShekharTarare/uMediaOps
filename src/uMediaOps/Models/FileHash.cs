using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace uMediaOps.Models;

/// <summary>
/// Stores computed file hashes for media items
/// </summary>
[TableName("uMediaOps_FileHashes")]
[PrimaryKey("Id")]
public class FileHash
{
    /// <summary>
    /// Primary key
    /// </summary>
    [PrimaryKeyColumn(AutoIncrement = true)]
    public int Id { get; set; }

    /// <summary>
    /// Umbraco media item ID
    /// </summary>
    public int MediaId { get; set; }

    /// <summary>
    /// SHA256 hash of file content
    /// </summary>
    public string Hash { get; set; } = string.Empty;

    /// <summary>
    /// File size in bytes at time of hash computation
    /// </summary>
    public long FileSize { get; set; }

    /// <summary>
    /// Timestamp when hash was computed
    /// </summary>
    public DateTime ComputedAt { get; set; }

    /// <summary>
    /// Indicates if this file was manually selected as the original in its duplicate group
    /// </summary>
    public bool IsManuallySelectedOriginal { get; set; }
}
