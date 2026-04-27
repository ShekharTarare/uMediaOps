using uMediaOps.Models;
using uMediaOps.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Api.Management.Controllers;
using Umbraco.Cms.Api.Management.Routing;
using Umbraco.Cms.Core.Security;

namespace uMediaOps.Controllers;

/// <summary>
/// API controller for backup and restore operations
/// </summary>
[VersionedApiBackOfficeRoute("umediaops/backup")]
[ApiExplorerSettings(GroupName = "uMediaOps - Backup")]
[Authorize(Policy = "BackOfficeAccess")]
public class BackupController : ManagementApiControllerBase
{
    private readonly IBackupService _backupService;
    private readonly IBackOfficeSecurityAccessor _backOfficeSecurityAccessor;
    private readonly uMediaOps.Configuration.uMediaOpsSettings _settings;
    private readonly ICacheService _cacheService;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IAuditLogService _auditLogService;

    public BackupController(
        IBackupService backupService,
        IBackOfficeSecurityAccessor backOfficeSecurityAccessor,
        Microsoft.Extensions.Options.IOptions<uMediaOps.Configuration.uMediaOpsSettings> settings,
        ICacheService cacheService,
        IServiceScopeFactory scopeFactory,
        IAuditLogService auditLogService)
    {
        _backupService = backupService;
        _backOfficeSecurityAccessor = backOfficeSecurityAccessor;
        _settings = settings.Value;
        _cacheService = cacheService;
        _scopeFactory = scopeFactory;
        _auditLogService = auditLogService;
    }

    /// <summary>
    /// Create a new backup (export only)
    /// </summary>
    /// <param name="request">Backup request</param>
    /// <returns>Backup initiation result</returns>
    [HttpPost("create")]
    public async Task<IActionResult> CreateBackup([FromBody] CreateBackupRequest request)
    {
        // Atomic check-and-set to prevent concurrent backup starts
        if (!_cacheService.TrySetIfAbsent("umediaops:backup:is-backing-up", true))
        {
            return BadRequest(new { message = "A backup is already in progress" });
        }

        // Rate limit: prevent backup starts within 10 seconds of each other
        var lastBackupStart = _cacheService.Get<DateTime?>("umediaops:backup:last-start");
        if (lastBackupStart.HasValue && (DateTime.UtcNow - lastBackupStart.Value).TotalSeconds < 10)
        {
            _cacheService.Remove("umediaops:backup:is-backing-up");
            return StatusCode(429, new { message = "Please wait before starting another backup" });
        }

        _cacheService.Set("umediaops:backup:last-start", DateTime.UtcNow, TimeSpan.FromMinutes(1));
        _cacheService.Set("umediaops:backup:progress", new BackupProgress { ProcessedFiles = 0, TotalFiles = 0 });

        var currentUser = _backOfficeSecurityAccessor.BackOfficeSecurity?.CurrentUser;
        var userId = currentUser?.Id;
        var userName = currentUser?.Name ?? currentUser?.Username ?? currentUser?.Email ?? "Unknown";

        // Start backup in background using a new DI scope
        _ = Task.Run(async () =>
        {
            using var scope = _scopeFactory.CreateScope();
            var backupService = scope.ServiceProvider.GetRequiredService<IBackupService>();
            var auditLogService = scope.ServiceProvider.GetRequiredService<IAuditLogService>();

            try
            {
                var progress = new Progress<BackupProgress>(p =>
                {
                    _cacheService.Set("umediaops:backup:progress", p);
                });

                var backupType = request.BackupType ?? "Full";
                var storageProvider = request.StorageProvider ?? "Local";

                var result = await backupService.CreateBackupAsync(
                    backupType,
                    storageProvider,
                    userName,
                    progress
                );

                // Log successful backup
                await auditLogService.LogActionAsync(
                    "Backup_Created",
                    null,
                    null,
                    userId,
                    userName,
                    new
                    {
                        backupId = result.BackupId,
                        backupType = result.BackupType,
                        fileCount = result.FileCount,
                        totalSize = result.TotalSize,
                        storageProvider = result.StorageProvider
                    },
                    true
                );
            }
            catch (Exception ex)
            {
                // Log failed backup
                await auditLogService.LogActionAsync(
                    "Backup_Failed",
                    null,
                    null,
                    userId,
                    userName,
                    new { backupType = request.BackupType, storageProvider = request.StorageProvider },
                    false,
                    ex.Message
                );
            }
            finally
            {
                _cacheService.Remove("umediaops:backup:is-backing-up");
            }
        });

        return Ok(new { message = "Backup started", isBackingUp = true });
    }

    /// <summary>
    /// Get backup progress
    /// </summary>
    /// <returns>Current backup progress</returns>
    [HttpGet("create/progress")]
    public IActionResult GetBackupProgress()
    {
        var progress = _cacheService.Get<BackupProgress>("umediaops:backup:progress") ?? new BackupProgress();
        var isBackingUp = _cacheService.Get<bool>("umediaops:backup:is-backing-up");
        return Ok(new
        {
            isBackingUp,
            progress = new
            {
                processedFiles = progress.ProcessedFiles,
                totalFiles = progress.TotalFiles,
                percentage = (int)progress.PercentageComplete,
                currentFile = progress.CurrentFile
            }
        });
    }

    /// <summary>
    /// Get all backups
    /// </summary>
    /// <returns>List of backups</returns>
    [HttpGet("list")]
    public async Task<IActionResult> GetBackups()
    {
        var backups = await _backupService.GetBackupsAsync();
        var backupDtos = backups.Select(b => new
        {
            b.BackupId,
            FileName = $"{b.BackupId}.zip",
            b.BackupType,
            b.StorageProvider,
            CreatedAt = b.StartedAt,
            b.CreatedBy,
            b.FileCount,
            b.TotalSize,
            IsVerified = b.Status == "Completed" && !string.IsNullOrEmpty(b.Checksum)
        });
        return Ok(backupDtos);
    }

    /// <summary>
    /// Get a specific backup
    /// </summary>
    /// <param name="backupId">Backup ID</param>
    /// <returns>Backup details</returns>
    [HttpGet("{backupId}")]
    public async Task<IActionResult> GetBackup(string backupId)
    {
        var backups = await _backupService.GetBackupsAsync();
        var backup = backups.FirstOrDefault(b => b.BackupId == backupId);

        if (backup == null)
        {
            return NotFound(new { message = $"Backup {backupId} not found" });
        }

        // Return DTO without internal paths (StoragePath, ManifestPath, ErrorMessage)
        return Ok(new
        {
            backup.BackupId,
            FileName = $"{backup.BackupId}.zip",
            backup.BackupType,
            backup.StorageProvider,
            CreatedAt = backup.StartedAt,
            backup.CompletedAt,
            backup.CreatedBy,
            backup.FileCount,
            backup.TotalSize,
            backup.CompressedSize,
            backup.Status,
            IsVerified = backup.Status == "Completed" && !string.IsNullOrEmpty(backup.Checksum),
            backup.ExpiresAt
        });
    }



    /// <summary>
    /// Verify backup integrity
    /// </summary>
    /// <param name="backupId">Backup ID</param>
    /// <returns>Verification result</returns>
    [HttpPost("{backupId}/verify")]
    public async Task<IActionResult> VerifyBackup(string backupId)
    {
        try
        {
            var isValid = await _backupService.VerifyBackupIntegrityAsync(backupId);

            var currentUser = _backOfficeSecurityAccessor.BackOfficeSecurity?.CurrentUser;
            var userName = currentUser?.Name ?? currentUser?.Username ?? currentUser?.Email ?? "Unknown";

            await _auditLogService.LogActionAsync(
                "Backup_Verified",
                null,
                null,
                currentUser?.Id,
                userName,
                new { backupId, isValid },
                true
            );

            return Ok(new
            {
                backupId,
                isValid,
                message = isValid ? "Backup integrity verified successfully" : "Backup integrity check failed"
            });
        }
        catch (Exception)
        {
            return BadRequest(new { message = "Failed to verify backup" });
        }
    }

    /// <summary>
    /// Delete a backup
    /// </summary>
    /// <param name="backupId">Backup ID</param>
    /// <returns>Deletion result</returns>
    [HttpDelete("{backupId}")]
    public async Task<IActionResult> DeleteBackup(string backupId)
    {
        try
        {
            var success = await _backupService.DeleteBackupAsync(backupId);

            if (!success)
            {
                return NotFound(new { message = $"Backup {backupId} not found" });
            }

            var currentUser = _backOfficeSecurityAccessor.BackOfficeSecurity?.CurrentUser;
            var userName = currentUser?.Name ?? currentUser?.Username ?? currentUser?.Email ?? "Unknown";

            await _auditLogService.LogActionAsync(
                "Backup_Deleted",
                null,
                null,
                currentUser?.Id,
                userName,
                new { backupId },
                true
            );

            return Ok(new { message = "Backup deleted successfully" });
        }
        catch (Exception)
        {
            return BadRequest(new { message = "Failed to delete backup" });
        }
    }

    /// <summary>
    /// Download a backup file
    /// </summary>
    /// <param name="backupId">Backup ID</param>
    /// <returns>Backup file</returns>
    [HttpGet("{backupId}/download")]
    public async Task<IActionResult> DownloadBackup(string backupId)
    {
        try
        {
            var backups = await _backupService.GetBackupsAsync();
            var backup = backups.FirstOrDefault(b => b.BackupId == backupId);

            if (backup == null)
            {
                return NotFound(new { message = $"Backup {backupId} not found" });
            }

            if (string.IsNullOrEmpty(backup.StoragePath) || !System.IO.File.Exists(backup.StoragePath))
            {
                return NotFound(new { message = "Backup file not found" });
            }

            // Security: validate the path is within the configured backup directory
            var configuredDir = _settings.BackupDirectory;
            if (configuredDir.StartsWith("~/"))
            {
                configuredDir = Path.Combine(Directory.GetCurrentDirectory(), configuredDir.Substring(2).Replace("/", Path.DirectorySeparatorChar.ToString()));
            }
            var expectedDir = Path.GetFullPath(configuredDir);
            var actualPath = Path.GetFullPath(backup.StoragePath);
            if (!actualPath.StartsWith(expectedDir, StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new { message = "Invalid backup path" });
            }

            var fileName = $"{backupId}.zip";
            return PhysicalFile(actualPath, "application/zip", fileName);
        }
        catch (Exception)
        {
            return BadRequest(new { message = "Failed to download backup" });
        }
    }
}

/// <summary>
/// Request model for creating a backup
/// </summary>
public class CreateBackupRequest
{
    [System.ComponentModel.DataAnnotations.RegularExpression("^(Full|Incremental)$", ErrorMessage = "BackupType must be 'Full' or 'Incremental'")]
    public string? BackupType { get; set; } = "Full";

    [System.ComponentModel.DataAnnotations.RegularExpression("^(Local)$", ErrorMessage = "Only 'Local' storage provider is currently supported")]
    public string? StorageProvider { get; set; } = "Local";
}
