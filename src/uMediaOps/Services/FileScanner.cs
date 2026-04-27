using System.Text.RegularExpressions;
using uMediaOps.Models;
using Microsoft.Extensions.Logging;

namespace uMediaOps.Services;

/// <summary>
/// Reusable file scanner for detecting media references in various file types.
/// Optimized for performance with compiled regex patterns and streaming.
/// </summary>
public class FileScanner
{
    private readonly ILogger _logger;
    
    // Compiled regex patterns for performance
    private static readonly Regex MediaUrlPattern = new(
        @"(?:src|href|url|background|background-image|data-src|data-udi)\s*[=:]\s*[""']?([^""'\s>]+/media/[^""'\s>]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    
    private static readonly Regex MediaUdiPattern = new(
        @"umb://media/([a-f0-9]{32})",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    
    private static readonly Regex MediaGuidPattern = new(
        @"([a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12})",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public FileScanner(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Scans a directory for files matching a pattern and checks for media references.
    /// Uses streaming for memory efficiency with large files.
    /// </summary>
    public async Task<List<MediaReference>> ScanDirectoryAsync(
        string directoryPath,
        string searchPattern,
        int mediaId,
        Guid mediaKey,
        string referenceType,
        CancellationToken cancellationToken = default)
    {
        var references = new List<MediaReference>();
        
        if (!Directory.Exists(directoryPath))
        {
            _logger.LogDebug("Directory not found: {Path}", directoryPath);
            return references;
        }

        try
        {
            var files = Directory.GetFiles(directoryPath, searchPattern, SearchOption.AllDirectories);
            _logger.LogDebug("Scanning {Count} {Pattern} files in {Path}", files.Length, searchPattern, directoryPath);

            foreach (var filePath in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                var fileReferences = await ScanFileAsync(filePath, mediaId, mediaKey, referenceType, cancellationToken);
                references.AddRange(fileReferences);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("File scan cancelled");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error scanning directory {Path}", directoryPath);
        }

        return references;
    }

    /// <summary>
    /// Scans a single file for media references.
    /// Uses streaming to handle large files efficiently.
    /// </summary>
    public async Task<List<MediaReference>> ScanFileAsync(
        string filePath,
        int mediaId,
        Guid mediaKey,
        string referenceType,
        CancellationToken cancellationToken = default)
    {
        var references = new List<MediaReference>();
        
        try
        {
            // Read entire file content for reference scanning
            var content = await File.ReadAllTextAsync(filePath, cancellationToken);
            
            if (ContainsMediaReference(content, mediaId, mediaKey))
            {
                var fileName = Path.GetFileName(filePath);
                references.Add(new MediaReference
                {
                    MediaId = mediaId,
                    ContentId = 0, // File-based references don't have content IDs
                    ContentName = fileName,
                    ContentType = referenceType,
                    PropertyAlias = filePath,
                    Url = filePath,
                    LastChecked = DateTime.UtcNow,
                    ReferenceType = referenceType,
                    RequiresManualUpdate = true,
                    RiskLevel = RiskLevel.Review,
                    WarningMessage = $"Found in {referenceType}: {fileName}. Manual code update required if deleted."
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error scanning file {Path}", filePath);
        }

        return references;
    }

    /// <summary>
    /// Checks if content contains a reference to the specified media item.
    /// Uses multiple detection strategies for reliability.
    /// </summary>
    private bool ContainsMediaReference(string content, int mediaId, Guid mediaKey)
    {
        if (string.IsNullOrEmpty(content))
            return false;

        var contentLower = content.ToLowerInvariant();
        var mediaKeyString = mediaKey.ToString().ToLowerInvariant();
        var mediaKeyNoHyphens = mediaKey.ToString("N").ToLowerInvariant();
        var mediaIdString = mediaId.ToString();

        // Check for various reference formats
        return contentLower.Contains(mediaKeyString) ||
               contentLower.Contains(mediaKeyNoHyphens) ||
               contentLower.Contains($"umb://media/{mediaKeyNoHyphens}") ||
               MediaUrlPattern.IsMatch(content) ||
               MediaUdiPattern.IsMatch(content) ||
               (contentLower.Contains("media") && contentLower.Contains(mediaIdString));
    }

    /// <summary>
    /// Gets the count of files in a directory matching a pattern.
    /// Used for statistics without scanning content.
    /// </summary>
    public int GetFileCount(string directoryPath, string searchPattern)
    {
        if (!Directory.Exists(directoryPath))
            return 0;

        try
        {
            return Directory.GetFiles(directoryPath, searchPattern, SearchOption.AllDirectories).Length;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error counting files in {Path}", directoryPath);
            return 0;
        }
    }
}
