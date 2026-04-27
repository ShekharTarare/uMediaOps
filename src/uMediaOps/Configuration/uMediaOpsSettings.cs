namespace uMediaOps.Configuration;

/// <summary>
/// Configuration settings for uMediaOps.
/// Add to appsettings.json under "uMediaOps" section.
/// </summary>
public class uMediaOpsSettings
{
    public const string SectionName = "uMediaOps";

    /// <summary>
    /// Directory for storing backup ZIP files.
    /// Supports ~/ for paths relative to the content root.
    /// Default: ~/App_Data/uMediaOps/Backups
    /// </summary>
    public string BackupDirectory { get; set; } = "~/App_Data/uMediaOps/Backups";

    /// <summary>
    /// Number of days to retain backups before they're eligible for cleanup.
    /// Set to 0 to keep backups indefinitely.
    /// Default: 30
    /// </summary>
    public int BackupRetentionDays { get; set; } = 30;

    /// <summary>
    /// Validate configuration settings.
    /// </summary>
    public void Validate()
    {
        if (BackupRetentionDays < 0)
        {
            throw new InvalidOperationException("BackupRetentionDays must be 0 or greater");
        }
    }
}
