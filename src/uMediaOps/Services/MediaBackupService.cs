using uMediaOps.Configuration;
using uMediaOps.Models;
using uMediaOps.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Umbraco.Cms.Core.IO;
using Umbraco.Cms.Core.Services;

namespace uMediaOps.Services;

/// <summary>
/// Service for creating and managing media library backups (export only)
/// </summary>
public interface IBackupService
{
    Task<BackupResult> CreateBackupAsync(string backupType, string storageProvider, string createdBy, IProgress<BackupProgress>? progress = null);
    Task<IEnumerable<Backup>> GetBackupsAsync();
    Task<bool> DeleteBackupAsync(string backupId);
    Task<int> CleanupExpiredBackupsAsync();
    Task<bool> VerifyBackupIntegrityAsync(string backupId);
}

/// <summary>
/// Implementation of backup service
/// </summary>
public class BackupService : IBackupService
{
    private readonly IBackupRepository _repository;
    private readonly IMediaService _mediaService;
    private readonly MediaFileManager _mediaFileManager;
    private readonly IAuditLogService _auditLogService;
    private readonly uMediaOpsSettings _settings;
    private readonly ILogger<BackupService> _logger;

    public BackupService(
        IBackupRepository repository,
        IMediaService mediaService,
        MediaFileManager mediaFileManager,
        IAuditLogService auditLogService,
        IOptions<uMediaOpsSettings> settings,
        ILogger<BackupService> logger)
    {
        _repository = repository;
        _mediaService = mediaService;
        _mediaFileManager = mediaFileManager;
        _auditLogService = auditLogService;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<BackupResult> CreateBackupAsync(
        string backupType,
        string storageProvider,
        string createdBy,
        IProgress<BackupProgress>? progress = null)
    {
        var backupId = $"backup_{DateTime.UtcNow:yyyyMMdd_HHmmss}";
        _logger.LogInformation("Creating {BackupType} backup with ID {BackupId}", backupType, backupId);

        var backup = new Backup
        {
            BackupId = backupId,
            BackupType = backupType,
            StorageProvider = storageProvider,
            CreatedBy = createdBy,
            StartedAt = DateTime.UtcNow,
            Status = "InProgress"
        };

        try
        {
            // Save initial backup record
            await _repository.SaveAsync(backup);

            // Load media in pages to avoid loading entire library into memory at once
            var pageSize = 1000;
            var pageIndex = 0;
            var mediaList = new List<Umbraco.Cms.Core.Models.IMedia>();
            long totalRecords;

            do
            {
                var page = _mediaService.GetPagedDescendants(-1, pageIndex, pageSize, out totalRecords);
                var filtered = page.Where(m => !m.Trashed && m.ContentType.Alias != "Folder");
                mediaList.AddRange(filtered);
                pageIndex++;
            } while ((long)pageIndex * pageSize < totalRecords);

            _logger.LogInformation("Backing up {Count} media items (excluding trashed and folders)", mediaList.Count);

            // Prepare backup directory
            var backupDir = GetBackupDirectory(storageProvider);
            if (!Directory.Exists(backupDir))
            {
                Directory.CreateDirectory(backupDir);
            }

            var zipPath = Path.Combine(backupDir, $"{backupId}.zip");
            var manifestPath = Path.Combine(backupDir, $"{backupId}_manifest.json");

            var manifest = new BackupManifest
            {
                BackupId = backupId,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = createdBy,
                Files = new List<BackupFileInfo>()
            };

            long totalSize = 0;
            int processedFiles = 0;

            // Create ZIP archive
            using (var zipArchive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                for (int i = 0; i < mediaList.Count; i++)
                {
                    var media = mediaList[i];

                    try
                    {
                        if (media.HasProperty("umbracoFile") && media.GetValue("umbracoFile") != null)
                        {
                            var fileValue = media.GetValue("umbracoFile");
                            string? filePath = ExtractFilePath(fileValue);

                            if (!string.IsNullOrEmpty(filePath))
                            {
                                var physicalPath = _mediaFileManager.FileSystem.GetFullPath(filePath);

                                if (File.Exists(physicalPath))
                                {
                                    var fileInfo = new FileInfo(physicalPath);
                                    totalSize += fileInfo.Length;

                                    // Add file to ZIP
                                    zipArchive.CreateEntryFromFile(physicalPath, filePath.TrimStart('/'), CompressionLevel.Optimal);

                                    // Calculate checksum
                                    var checksum = await CalculateFileChecksumAsync(physicalPath);

                                    // Add to manifest
                                    manifest.Files.Add(new BackupFileInfo
                                    {
                                        MediaId = media.Id,
                                        FilePath = filePath,
                                        FileSize = fileInfo.Length,
                                        Checksum = checksum,
                                        MediaName = media.Name ?? string.Empty
                                    });

                                    processedFiles++;

                                    // Report progress
                                    progress?.Report(new BackupProgress
                                    {
                                        ProcessedFiles = processedFiles,
                                        TotalFiles = mediaList.Count,
                                        CurrentFile = media.Name,
                                        PercentageComplete = (processedFiles / (double)mediaList.Count) * 100,
                                        ProcessedSize = totalSize,
                                        TotalSize = totalSize
                                    });
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to backup media {MediaId}: {MediaName}", media.Id, media.Name);
                    }
                }
            }



            // Save manifest
            var manifestJson = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(manifestPath, manifestJson);

            // Calculate backup checksum
            var backupChecksum = await CalculateFileChecksumAsync(zipPath);

            // Get compressed size
            var zipFileInfo = new FileInfo(zipPath);
            var compressedSize = zipFileInfo.Length;

            // Update backup record
            backup.CompletedAt = DateTime.UtcNow;
            backup.Status = "Completed";
            backup.FileCount = processedFiles;
            backup.TotalSize = totalSize;
            backup.CompressedSize = compressedSize;
            backup.StoragePath = zipPath;
            backup.ManifestPath = manifestPath;
            backup.Checksum = backupChecksum;
            backup.ExpiresAt = DateTime.UtcNow.AddDays(_settings.BackupRetentionDays);

            await _repository.UpdateAsync(backup);

            _logger.LogInformation("Backup {BackupId} completed successfully. Files: {FileCount}, Size: {TotalSize} bytes", 
                backupId, processedFiles, totalSize);

            return new BackupResult
            {
                BackupId = backupId,
                BackupType = backupType,
                StorageProvider = storageProvider,
                FileCount = processedFiles,
                TotalSize = totalSize,
                CompressedSize = compressedSize,
                Success = true
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create backup {BackupId}", backupId);

            backup.Status = "Failed";
            backup.CompletedAt = DateTime.UtcNow;
            backup.ErrorMessage = ex.Message; // Internal DB record — keep full details
            await _repository.UpdateAsync(backup);

            return new BackupResult
            {
                BackupId = backupId,
                Success = false,
                ErrorMessage = "Backup creation failed. Check the audit log for details."
            };
        }
    }


    public async Task<IEnumerable<Backup>> GetBackupsAsync()
    {
        return await _repository.GetAllAsync();
    }

    public async Task<bool> DeleteBackupAsync(string backupId)
    {
        _logger.LogInformation("Deleting backup {BackupId}", backupId);

        try
        {
            var backup = await _repository.GetByBackupIdAsync(backupId);
            if (backup == null)
            {
                _logger.LogWarning("Backup {BackupId} not found", backupId);
                return false;
            }

            // Delete physical files
            if (!string.IsNullOrEmpty(backup.StoragePath) && File.Exists(backup.StoragePath))
            {
                File.Delete(backup.StoragePath);
                _logger.LogInformation("Deleted backup file: {FilePath}", backup.StoragePath);
            }

            if (!string.IsNullOrEmpty(backup.ManifestPath) && File.Exists(backup.ManifestPath))
            {
                File.Delete(backup.ManifestPath);
                _logger.LogInformation("Deleted manifest file: {FilePath}", backup.ManifestPath);
            }

            // Delete database record
            await _repository.DeleteAsync(backup.Id);

            _logger.LogInformation("Backup {BackupId} deleted successfully", backupId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete backup {BackupId}", backupId);
            return false;
        }
    }

    public async Task<int> CleanupExpiredBackupsAsync()
    {
        _logger.LogInformation("Starting cleanup of expired backups");

        try
        {
            var expiredBackups = await _repository.GetExpiredAsync();
            int deletedCount = 0;

            foreach (var backup in expiredBackups)
            {
                try
                {
                    var success = await DeleteBackupAsync(backup.BackupId);
                    if (success)
                    {
                        deletedCount++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to delete expired backup {BackupId}", backup.BackupId);
                }
            }

            _logger.LogInformation("Cleanup completed. Deleted {Count} expired backups", deletedCount);
            return deletedCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cleanup expired backups");
            return 0;
        }
    }

    public async Task<bool> VerifyBackupIntegrityAsync(string backupId)
    {
        _logger.LogInformation("Verifying backup integrity for {BackupId}", backupId);

        try
        {
            var backup = await _repository.GetByBackupIdAsync(backupId);
            if (backup == null)
            {
                _logger.LogWarning("Backup {BackupId} not found", backupId);
                return false;
            }

            // Check if backup file exists
            if (string.IsNullOrEmpty(backup.StoragePath) || !File.Exists(backup.StoragePath))
            {
                _logger.LogWarning("Backup file not found: {FilePath}", backup.StoragePath);
                return false;
            }

            // Verify backup file checksum
            if (!string.IsNullOrEmpty(backup.Checksum))
            {
                var currentChecksum = await CalculateFileChecksumAsync(backup.StoragePath);
                if (currentChecksum != backup.Checksum)
                {
                    _logger.LogWarning("Backup checksum mismatch. Expected: {Expected}, Got: {Actual}", 
                        backup.Checksum, currentChecksum);
                    return false;
                }
            }

            // Verify ZIP integrity
            try
            {
                using var zipArchive = ZipFile.OpenRead(backup.StoragePath);
                var entryCount = zipArchive.Entries.Count;
                _logger.LogInformation("Backup ZIP contains {EntryCount} entries", entryCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Backup ZIP file is corrupted");
                return false;
            }

            // Verify manifest if exists
            if (!string.IsNullOrEmpty(backup.ManifestPath) && File.Exists(backup.ManifestPath))
            {
                try
                {
                    var manifestJson = await File.ReadAllTextAsync(backup.ManifestPath);
                    var manifest = JsonSerializer.Deserialize<BackupManifest>(manifestJson);
                    
                    if (manifest != null && manifest.Files.Count != backup.FileCount)
                    {
                        _logger.LogWarning("Manifest file count mismatch. Expected: {Expected}, Got: {Actual}", 
                            backup.FileCount, manifest.Files.Count);
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to parse manifest file");
                    return false;
                }
            }

            _logger.LogInformation("Backup {BackupId} integrity verified successfully", backupId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to verify backup {BackupId}", backupId);
            return false;
        }
    }

    private string GetBackupDirectory(string storageProvider)
    {
        if (storageProvider != "Local")
        {
            throw new NotImplementedException($"Storage provider {storageProvider} is not yet implemented. Only Local storage is currently supported.");
        }

        var backupDir = _settings.BackupDirectory;
        
        // Handle ~/App_Data path
        if (backupDir.StartsWith("~/"))
        {
            var contentRoot = Directory.GetCurrentDirectory();
            backupDir = Path.Combine(contentRoot, backupDir.Substring(2).Replace("/", Path.DirectorySeparatorChar.ToString()));
        }

        return backupDir;
    }

    private string? ExtractFilePath(object? fileValue)
    {
        if (fileValue == null) return null;

        if (fileValue is string strValue)
        {
            // Try to parse as JSON first (Umbraco 10+ format)
            if (strValue.StartsWith("{"))
            {
                try
                {
                    using var json = JsonDocument.Parse(strValue);
                    if (json.RootElement.TryGetProperty("src", out var srcElement))
                    {
                        return srcElement.GetString();
                    }
                }
                catch
                {
                    return strValue;
                }
            }
            else
            {
                return strValue;
            }
        }

        return null;
    }

    private async Task<string> CalculateFileChecksumAsync(string filePath)
    {
        using var sha256 = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        var hashBytes = await sha256.ComputeHashAsync(stream);
        return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
    }
}

/// <summary>
/// Backup manifest containing file list (export only)
/// </summary>
internal class BackupManifest
{
    public string BackupId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public List<BackupFileInfo> Files { get; set; } = new();
}

/// <summary>
/// Information about a file in the backup
/// </summary>
internal class BackupFileInfo
{
    public int MediaId { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string Checksum { get; set; } = string.Empty;
    public string MediaName { get; set; } = string.Empty;
}
