using uMediaOps.Models;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Hosting;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;

namespace uMediaOps.Services;

public interface IReferenceTrackingService
{
    // New signature with ScanProfile
    Task<IEnumerable<MediaReference>> GetReferencesAsync(int mediaId, ScanProfile profile);
    Task<(IEnumerable<MediaReference> references, ReferenceScanStatistics statistics)> GetReferencesWithStatisticsAsync(int mediaId, ScanProfile profile);
    
    // Bulk scanning method - scans all files once and returns a set of referenced media IDs
    Task<(HashSet<int> referencedMediaIds, ReferenceScanStatistics statistics)> GetAllReferencedMediaIdsAsync(
        IEnumerable<IMedia> allMedia, 
        ScanProfile profile, 
        CancellationToken cancellationToken = default);
    
    Task<bool> IsSafeToDeleteAsync(int mediaId);
}

public class ReferenceTrackingService : IReferenceTrackingService
{
    private readonly IContentService _contentService;
    private readonly IMediaService _mediaService;
    private readonly ILogger<ReferenceTrackingService> _logger;
    private readonly FileScanner _fileScanner;
    private readonly IWebHostEnvironment _webHostEnvironment;

    public ReferenceTrackingService(
        IContentService contentService,
        IMediaService mediaService,
        ILogger<ReferenceTrackingService> logger,
        IWebHostEnvironment webHostEnvironment)
    {
        _contentService = contentService;
        _mediaService = mediaService;
        _logger = logger;
        _webHostEnvironment = webHostEnvironment;
        _fileScanner = new FileScanner(logger);
    }

    // New method with ScanProfile
    public async Task<IEnumerable<MediaReference>> GetReferencesAsync(int mediaId, ScanProfile profile)
    {
        var (references, _) = await GetReferencesWithStatisticsAsync(mediaId, profile);
        return references;
    }

    // New method with ScanProfile
    public async Task<(IEnumerable<MediaReference> references, ReferenceScanStatistics statistics)> GetReferencesWithStatisticsAsync(
        int mediaId, 
        ScanProfile profile)
    {
        var config = ScanProfileConfiguration.GetConfiguration(profile);
        var statistics = new ReferenceScanStatistics();
        var allReferences = new List<MediaReference>();

        var media = _mediaService.GetById(mediaId);
        if (media == null)
        {
            _logger.LogWarning("Media {MediaId} not found", mediaId);
            return (allReferences, statistics);
        }

        var mediaKey = media.Key;

        // Always scan content (all profiles include this)
        if (config.ScanContent)
        {
            var contentRefs = await ScanContentAsync(mediaId, mediaKey, statistics);
            allReferences.AddRange(contentRefs);
        }

        // Scan Views folder (Deep and Complete profiles)
        if (config.ScanViews)
        {
            var viewRefs = await ScanViewsAsync(mediaId, mediaKey, statistics);
            allReferences.AddRange(viewRefs);
        }

        // Scan JavaScript (Deep and Complete profiles)
        if (config.ScanJavaScript)
        {
            var jsRefs = await ScanJavaScriptAsync(mediaId, mediaKey, statistics);
            allReferences.AddRange(jsRefs);
        }

        // Scan CSS (Deep and Complete profiles)
        if (config.ScanCss)
        {
            var cssRefs = await ScanCssAsync(mediaId, mediaKey, statistics);
            allReferences.AddRange(cssRefs);
        }

        // Scan TypeScript (Complete profile only)
        if (config.ScanTypeScript)
        {
            var tsRefs = await ScanTypeScriptAsync(mediaId, mediaKey, statistics);
            allReferences.AddRange(tsRefs);
        }

        // Scan SCSS (Complete profile only)
        if (config.ScanScss)
        {
            var scssRefs = await ScanScssAsync(mediaId, mediaKey, statistics);
            allReferences.AddRange(scssRefs);
        }

        // Scan wwwroot (Complete profile only)
        if (config.ScanWwwroot)
        {
            var wwwrootRefs = await ScanWwwrootAsync(mediaId, mediaKey, statistics);
            allReferences.AddRange(wwwrootRefs);
        }

        // Scan config files (Complete profile only)
        if (config.ScanConfig)
        {
            var configRefs = await ScanConfigAsync(mediaId, mediaKey, statistics);
            allReferences.AddRange(configRefs);
        }

        return (allReferences, statistics);
    }

    public async Task<bool> IsSafeToDeleteAsync(int mediaId)
    {
        // Get references (will do real-time scan if needed)
        var references = await GetReferencesAsync(mediaId, ScanProfile.Quick);
        return !references.Any();
    }

    private string? GetContentUrl(IContent content)
    {
        return $"/umbraco/section/content/workspace/document/edit/{content.Key}/invariant";
    }

    /// <summary>
    /// Extracts the media file path from an IMedia object.
    /// Handles both JSON format (ImageCropper) and string format.
    /// </summary>
    /// <param name="media">The media item</param>
    /// <returns>The media path, or null if not found or invalid</returns>
    private string? GetMediaPath(IMedia media)
    {
        if (!media.HasProperty("umbracoFile")) return null;

        var fileValue = media.GetValue("umbracoFile");
        if (fileValue == null) return null;

        // Handle JSON format (ImageCropper)
        if (fileValue is string strValue && strValue.StartsWith("{"))
        {
            try
            {
                using var json = System.Text.Json.JsonDocument.Parse(strValue);
                if (json.RootElement.TryGetProperty("src", out var srcElement))
                {
                    return srcElement.GetString();
                }
            }
            catch
            {
                // Fall through to string conversion
            }
        }

        return fileValue.ToString();
    }

    /// <summary>
    /// Checks if content contains a reference to the specified media item.
    /// Implements multiple detection strategies: direct path, GUID (with/without hyphens),
    /// UDI format, and contextual filename matching.
    /// </summary>
    /// <param name="content">The content to search (e.g., template or partial view content)</param>
    /// <param name="mediaPath">The media file path</param>
    /// <param name="mediaKey">The media GUID</param>
    /// <returns>True if a reference is found, false otherwise</returns>
    private bool ContainsMediaReference(string content, string mediaPath, Guid mediaKey)
    {
        if (string.IsNullOrEmpty(content) || string.IsNullOrEmpty(mediaPath))
            return false;

        var contentLower = content.ToLowerInvariant();
        var mediaPathLower = mediaPath.ToLowerInvariant();
        var mediaKeyString = mediaKey.ToString().ToLowerInvariant();
        var mediaKeyNoHyphens = mediaKey.ToString("N").ToLowerInvariant();

        // Strategy 1: Direct path match (case-insensitive)
        if (contentLower.Contains(mediaPathLower))
            return true;

        // Strategy 2: GUID with hyphens
        if (contentLower.Contains(mediaKeyString))
            return true;

        // Strategy 3: GUID without hyphens
        if (contentLower.Contains(mediaKeyNoHyphens))
            return true;

        // Strategy 4: UDI format (umb://media/{guid})
        if (contentLower.Contains($"umb://media/{mediaKeyNoHyphens}"))
            return true;

        // Strategy 5: Filename in media context
        // Only match filename if it appears near media-related keywords
        var fileName = Path.GetFileName(mediaPath);
        if (!string.IsNullOrEmpty(fileName))
        {
            var fileNameLower = fileName.ToLowerInvariant();
            var fileNameIndex = contentLower.IndexOf(fileNameLower);
            
            if (fileNameIndex >= 0)
            {
                // Check context within 50 characters before and after the filename
                var contextStart = Math.Max(0, fileNameIndex - 50);
                var contextEnd = Math.Min(contentLower.Length, fileNameIndex + fileNameLower.Length + 50);
                var contextLength = contextEnd - contextStart;
                var context = contentLower.Substring(contextStart, contextLength);

                if (context.Contains("/media/") ||
                    context.Contains("src=") ||
                    context.Contains("href="))
                    return true;
            }
        }

        return false;
    }

    // New scanning methods for comprehensive scan profiles

    private async Task<List<MediaReference>> ScanContentAsync(int mediaId, Guid mediaKey, ReferenceScanStatistics statistics)
    {
        var references = new List<MediaReference>();
        
        try
        {
            // Load content in pages to avoid loading entire content tree into memory
            var contentPageSize = 1000;
            var contentPageIndex = 0;
            long totalRecords;
            var allContentList = new List<Umbraco.Cms.Core.Models.IContent>();

            do
            {
                var page = _contentService.GetPagedDescendants(-1, contentPageIndex, contentPageSize, out totalRecords);
                allContentList.AddRange(page);
                contentPageIndex++;
            } while ((long)contentPageIndex * contentPageSize < totalRecords);

            statistics.ContentItemsScanned = allContentList.Count;
            
            _logger.LogDebug("Scanning {Count} content items for media {MediaId}", allContentList.Count, mediaId);

            var mediaKeyString = mediaKey.ToString().ToLowerInvariant();
            var mediaKeyNoHyphens = mediaKey.ToString("N").ToLowerInvariant();
            var mediaIdString = mediaId.ToString();

            foreach (var content in allContentList)
            {
                try
                {
                    bool foundInContent = false;
                    string? foundInProperty = null;

                    foreach (var property in content.Properties)
                    {
                        // Check all culture/segment variants
                        foreach (var propertyValue in property.Values)
                        {
                            var value = propertyValue.EditedValue?.ToString();
                            if (string.IsNullOrEmpty(value))
                                continue;

                            var valueLower = value.ToLowerInvariant();
                            bool hasKeyReference = valueLower.Contains(mediaKeyString) || valueLower.Contains(mediaKeyNoHyphens);
                            bool hasUdiReference = valueLower.Contains($"umb://media/{mediaKeyNoHyphens}");
                            bool hasIdReference = value.Contains($"\"id\":{mediaIdString}") || value.Contains($"\"id\": {mediaIdString}");
                        
                            if (hasKeyReference || hasUdiReference || hasIdReference)
                            {
                                foundInContent = true;
                                foundInProperty = property.Alias;
                                break;
                            }
                        }

                        if (foundInContent) break;
                    }

                    if (foundInContent)
                    {
                        references.Add(new MediaReference
                        {
                            MediaId = mediaId,
                            ContentId = content.Id,
                            ContentName = content.Name ?? string.Empty,
                            ContentType = content.ContentType.Alias,
                            PropertyAlias = foundInProperty ?? "Unknown",
                            Url = $"/umbraco/section/content/workspace/document/edit/{content.Key}/invariant",
                            LastChecked = DateTime.UtcNow,
                            ReferenceType = "Content",
                            RequiresManualUpdate = false,
                            RiskLevel = RiskLevel.HighRisk // Content references are high risk
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error scanning content {ContentId} for media {MediaId}", content.Id, mediaId);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error scanning content for media {MediaId}", mediaId);
        }

        return references;
    }

    private async Task<List<MediaReference>> ScanViewsAsync(int mediaId, Guid mediaKey, ReferenceScanStatistics statistics)
    {
        var references = new List<MediaReference>();
        
        try
        {
            var viewsPath = Path.Combine(_webHostEnvironment.ContentRootPath, "Views");
            
            // Scan all .cshtml files in Views folder
            var viewFiles = await _fileScanner.ScanDirectoryAsync(
                viewsPath, "*.cshtml", mediaId, mediaKey, "View");
            
            references.AddRange(viewFiles);
            
            // Count different types of views for statistics
            statistics.ViewFilesScanned = _fileScanner.GetFileCount(viewsPath, "*.cshtml");
            statistics.TemplatesScanned = _fileScanner.GetFileCount(viewsPath, "*.cshtml"); // Simplified - all cshtml
            statistics.PartialViewsScanned = _fileScanner.GetFileCount(Path.Combine(viewsPath, "Partials"), "*.cshtml");
            
            _logger.LogDebug("Scanned {Count} view files for media {MediaId}", statistics.ViewFilesScanned, mediaId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error scanning views for media {MediaId}", mediaId);
        }

        return references;
    }

    private async Task<List<MediaReference>> ScanJavaScriptAsync(int mediaId, Guid mediaKey, ReferenceScanStatistics statistics)
    {
        var references = new List<MediaReference>();
        
        try
        {
            var wwwrootPath = Path.Combine(_webHostEnvironment.ContentRootPath, "wwwroot");
            var jsFiles = await _fileScanner.ScanDirectoryAsync(
                wwwrootPath, "*.js", mediaId, mediaKey, "JavaScript");
            
            references.AddRange(jsFiles);
            statistics.JavaScriptFilesScanned = _fileScanner.GetFileCount(wwwrootPath, "*.js");
            
            _logger.LogDebug("Scanned {Count} JavaScript files for media {MediaId}", statistics.JavaScriptFilesScanned, mediaId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error scanning JavaScript for media {MediaId}", mediaId);
        }

        return references;
    }

    private async Task<List<MediaReference>> ScanCssAsync(int mediaId, Guid mediaKey, ReferenceScanStatistics statistics)
    {
        var references = new List<MediaReference>();
        
        try
        {
            var wwwrootPath = Path.Combine(_webHostEnvironment.ContentRootPath, "wwwroot");
            var cssFiles = await _fileScanner.ScanDirectoryAsync(
                wwwrootPath, "*.css", mediaId, mediaKey, "CSS");
            
            references.AddRange(cssFiles);
            statistics.CssFilesScanned = _fileScanner.GetFileCount(wwwrootPath, "*.css");
            
            _logger.LogDebug("Scanned {Count} CSS files for media {MediaId}", statistics.CssFilesScanned, mediaId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error scanning CSS for media {MediaId}", mediaId);
        }

        return references;
    }

    private async Task<List<MediaReference>> ScanTypeScriptAsync(int mediaId, Guid mediaKey, ReferenceScanStatistics statistics)
    {
        var references = new List<MediaReference>();
        
        try
        {
            var rootPath = _webHostEnvironment.ContentRootPath;
            
            // Scan .ts files
            var tsFiles = await _fileScanner.ScanDirectoryAsync(
                rootPath, "*.ts", mediaId, mediaKey, "TypeScript");
            references.AddRange(tsFiles);
            
            // Scan .tsx files
            var tsxFiles = await _fileScanner.ScanDirectoryAsync(
                rootPath, "*.tsx", mediaId, mediaKey, "TypeScript");
            references.AddRange(tsxFiles);
            
            statistics.TypeScriptFilesScanned = 
                _fileScanner.GetFileCount(rootPath, "*.ts") + 
                _fileScanner.GetFileCount(rootPath, "*.tsx");
            
            _logger.LogDebug("Scanned {Count} TypeScript files for media {MediaId}", statistics.TypeScriptFilesScanned, mediaId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error scanning TypeScript for media {MediaId}", mediaId);
        }

        return references;
    }

    private async Task<List<MediaReference>> ScanScssAsync(int mediaId, Guid mediaKey, ReferenceScanStatistics statistics)
    {
        var references = new List<MediaReference>();
        
        try
        {
            var rootPath = _webHostEnvironment.ContentRootPath;
            
            // Scan .scss files
            var scssFiles = await _fileScanner.ScanDirectoryAsync(
                rootPath, "*.scss", mediaId, mediaKey, "SCSS");
            references.AddRange(scssFiles);
            
            // Scan .less files
            var lessFiles = await _fileScanner.ScanDirectoryAsync(
                rootPath, "*.less", mediaId, mediaKey, "LESS");
            references.AddRange(lessFiles);
            
            statistics.ScssFilesScanned = 
                _fileScanner.GetFileCount(rootPath, "*.scss") + 
                _fileScanner.GetFileCount(rootPath, "*.less");
            
            _logger.LogDebug("Scanned {Count} SCSS/LESS files for media {MediaId}", statistics.ScssFilesScanned, mediaId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error scanning SCSS for media {MediaId}", mediaId);
        }

        return references;
    }

    private async Task<List<MediaReference>> ScanWwwrootAsync(int mediaId, Guid mediaKey, ReferenceScanStatistics statistics)
    {
        var references = new List<MediaReference>();
        
        try
        {
            var wwwrootPath = Path.Combine(_webHostEnvironment.ContentRootPath, "wwwroot");
            
            // Scan all files in wwwroot (excluding already scanned JS/CSS)
            var patterns = new[] { "*.html", "*.htm", "*.xml", "*.json", "*.svg" };
            
            foreach (var pattern in patterns)
            {
                var files = await _fileScanner.ScanDirectoryAsync(
                    wwwrootPath, pattern, mediaId, mediaKey, "Wwwroot");
                references.AddRange(files);
            }
            
            // Count all files in wwwroot for statistics
            statistics.WwwrootFilesScanned = patterns.Sum(p => _fileScanner.GetFileCount(wwwrootPath, p));
            
            _logger.LogDebug("Scanned {Count} wwwroot files for media {MediaId}", statistics.WwwrootFilesScanned, mediaId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error scanning wwwroot for media {MediaId}", mediaId);
        }

        return references;
    }

    private async Task<List<MediaReference>> ScanConfigAsync(int mediaId, Guid mediaKey, ReferenceScanStatistics statistics)
    {
        var references = new List<MediaReference>();
        
        try
        {
            var rootPath = _webHostEnvironment.ContentRootPath;
            
            // Scan appsettings files
            var appSettingsFiles = await _fileScanner.ScanDirectoryAsync(
                rootPath, "appsettings*.json", mediaId, mediaKey, "Config");
            references.AddRange(appSettingsFiles);
            
            // Scan web.config
            var webConfigPath = Path.Combine(rootPath, "web.config");
            if (System.IO.File.Exists(webConfigPath))
            {
                var webConfigRefs = await _fileScanner.ScanFileAsync(
                    webConfigPath, mediaId, mediaKey, "Config");
                references.AddRange(webConfigRefs);
            }
            
            statistics.ConfigFilesScanned = 
                _fileScanner.GetFileCount(rootPath, "appsettings*.json") + 
                (System.IO.File.Exists(webConfigPath) ? 1 : 0);
            
            _logger.LogDebug("Scanned {Count} config files for media {MediaId}", statistics.ConfigFilesScanned, mediaId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error scanning config files for media {MediaId}", mediaId);
        }

        return references;
    }

    /// <summary>
    /// OPTIMIZED: Scans all files once and returns a set of all referenced media IDs.
    /// This is much faster than scanning files for each media item individually.
    /// </summary>
    public async Task<(HashSet<int> referencedMediaIds, ReferenceScanStatistics statistics)> GetAllReferencedMediaIdsAsync(
        IEnumerable<IMedia> allMedia,
        ScanProfile profile,
        CancellationToken cancellationToken = default)
    {
        var config = ScanProfileConfiguration.GetConfiguration(profile);
        var statistics = new ReferenceScanStatistics();
        var referencedMediaIds = new HashSet<int>();

        _logger.LogInformation("Starting optimized bulk scan with profile {Profile}", profile);

        // Build lookup dictionaries for fast media ID/Key/Path matching
        var mediaByKey = new Dictionary<string, int>();
        var mediaByKeyNoHyphens = new Dictionary<string, int>();
        var mediaByPath = new Dictionary<string, int>();
        var mediaById = new Dictionary<string, int>();

        foreach (var media in allMedia)
        {
            var mediaKey = media.Key.ToString().ToLowerInvariant();
            var mediaKeyNoHyphens = media.Key.ToString("N").ToLowerInvariant();
            var mediaPath = GetMediaPath(media);

            mediaByKey[mediaKey] = media.Id;
            mediaByKeyNoHyphens[mediaKeyNoHyphens] = media.Id;
            mediaById[media.Id.ToString()] = media.Id;

            if (!string.IsNullOrEmpty(mediaPath))
            {
                mediaByPath[mediaPath.ToLowerInvariant()] = media.Id;
            }
        }

        _logger.LogInformation("Built lookup index for {Count} media items", allMedia.Count());

        // Scan content (always included)
        if (config.ScanContent)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ScanContentBulkAsync(referencedMediaIds, mediaByKey, mediaByKeyNoHyphens, mediaById, statistics, cancellationToken);
        }

        // Scan Views folder (Deep and Complete profiles)
        if (config.ScanViews)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ScanViewsBulkAsync(referencedMediaIds, mediaByKey, mediaByKeyNoHyphens, mediaByPath, statistics, cancellationToken);
        }

        // Scan JavaScript (Deep and Complete profiles)
        if (config.ScanJavaScript)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ScanJavaScriptBulkAsync(referencedMediaIds, mediaByKey, mediaByKeyNoHyphens, mediaByPath, statistics, cancellationToken);
        }

        // Scan CSS (Deep and Complete profiles)
        if (config.ScanCss)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ScanCssBulkAsync(referencedMediaIds, mediaByKey, mediaByKeyNoHyphens, mediaByPath, statistics, cancellationToken);
        }

        // Scan TypeScript (Complete profile only)
        if (config.ScanTypeScript)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ScanTypeScriptBulkAsync(referencedMediaIds, mediaByKey, mediaByKeyNoHyphens, mediaByPath, statistics, cancellationToken);
        }

        // Scan SCSS (Complete profile only)
        if (config.ScanScss)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ScanScssBulkAsync(referencedMediaIds, mediaByKey, mediaByKeyNoHyphens, mediaByPath, statistics, cancellationToken);
        }

        // Scan wwwroot (Complete profile only)
        if (config.ScanWwwroot)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ScanWwwrootBulkAsync(referencedMediaIds, mediaByKey, mediaByKeyNoHyphens, mediaByPath, statistics, cancellationToken);
        }

        // Scan config files (Complete profile only)
        if (config.ScanConfig)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ScanConfigBulkAsync(referencedMediaIds, mediaByKey, mediaByKeyNoHyphens, mediaByPath, statistics, cancellationToken);
        }

        _logger.LogInformation("Bulk scan completed: Found {Count} referenced media items", referencedMediaIds.Count);

        return (referencedMediaIds, statistics);
    }

    private async Task ScanContentBulkAsync(
        HashSet<int> referencedMediaIds,
        Dictionary<string, int> mediaByKey,
        Dictionary<string, int> mediaByKeyNoHyphens,
        Dictionary<string, int> mediaById,
        ReferenceScanStatistics statistics,
        CancellationToken cancellationToken)
    {
        try
        {
            // Load content in pages to avoid loading entire content tree into memory
            var contentPageSize = 1000;
            var contentPageIndex = 0;
            long totalRecords;
            var allContentList = new List<Umbraco.Cms.Core.Models.IContent>();

            do
            {
                var page = _contentService.GetPagedDescendants(-1, contentPageIndex, contentPageSize, out totalRecords);
                allContentList.AddRange(page);
                contentPageIndex++;
            } while ((long)contentPageIndex * contentPageSize < totalRecords);

            statistics.ContentItemsScanned = allContentList.Count;

            _logger.LogInformation("Scanning {Count} content items", allContentList.Count);

            foreach (var content in allContentList)
            {
                cancellationToken.ThrowIfCancellationRequested();

                foreach (var property in content.Properties)
                {
                    // Check all culture/segment variants
                    foreach (var propertyValue in property.Values)
                    {
                        var value = propertyValue.EditedValue?.ToString();
                        if (string.IsNullOrEmpty(value)) continue;

                        var valueLower = value.ToLowerInvariant();

                        // Check for media references
                        foreach (var kvp in mediaByKey)
                        {
                            if (valueLower.Contains(kvp.Key))
                            {
                                referencedMediaIds.Add(kvp.Value);
                            }
                        }

                        foreach (var kvp in mediaByKeyNoHyphens)
                        {
                            if (valueLower.Contains(kvp.Key) || valueLower.Contains($"umb://media/{kvp.Key}"))
                            {
                                referencedMediaIds.Add(kvp.Value);
                            }
                        }

                        foreach (var kvp in mediaById)
                        {
                            if (value.Contains($"\"id\":{kvp.Key}") || value.Contains($"\"id\": {kvp.Key}"))
                            {
                                referencedMediaIds.Add(kvp.Value);
                            }
                        }
                    }
                }
            }

            _logger.LogInformation("Content scan complete: Found {Count} referenced media", referencedMediaIds.Count);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Content scan cancelled");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during bulk content scan");
        }
    }

    private async Task ScanViewsBulkAsync(
        HashSet<int> referencedMediaIds,
        Dictionary<string, int> mediaByKey,
        Dictionary<string, int> mediaByKeyNoHyphens,
        Dictionary<string, int> mediaByPath,
        ReferenceScanStatistics statistics,
        CancellationToken cancellationToken)
    {
        try
        {
            var viewsPath = Path.Combine(_webHostEnvironment.ContentRootPath, "Views");
            if (!Directory.Exists(viewsPath)) return;

            var viewFiles = Directory.GetFiles(viewsPath, "*.cshtml", SearchOption.AllDirectories);
            statistics.ViewFilesScanned = viewFiles.Length;
            statistics.TemplatesScanned = viewFiles.Length;

            _logger.LogInformation("Scanning {Count} view files", viewFiles.Length);

            foreach (var file in viewFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var content = await System.IO.File.ReadAllTextAsync(file, cancellationToken);
                    var contentLower = content.ToLowerInvariant();

                    ScanContentForMediaReferences(contentLower, content, mediaByKey, mediaByKeyNoHyphens, mediaByPath, referencedMediaIds);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error scanning view file {File}", file);
                }
            }

            _logger.LogInformation("Views scan complete");
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Views scan cancelled");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during bulk views scan");
        }
    }

    private async Task ScanJavaScriptBulkAsync(
        HashSet<int> referencedMediaIds,
        Dictionary<string, int> mediaByKey,
        Dictionary<string, int> mediaByKeyNoHyphens,
        Dictionary<string, int> mediaByPath,
        ReferenceScanStatistics statistics,
        CancellationToken cancellationToken)
    {
        try
        {
            var wwwrootPath = Path.Combine(_webHostEnvironment.ContentRootPath, "wwwroot");
            if (!Directory.Exists(wwwrootPath)) return;

            var jsFiles = Directory.GetFiles(wwwrootPath, "*.js", SearchOption.AllDirectories);
            statistics.JavaScriptFilesScanned = jsFiles.Length;

            _logger.LogInformation("Scanning {Count} JavaScript files", jsFiles.Length);

            foreach (var file in jsFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var content = await System.IO.File.ReadAllTextAsync(file, cancellationToken);
                    var contentLower = content.ToLowerInvariant();

                    ScanContentForMediaReferences(contentLower, content, mediaByKey, mediaByKeyNoHyphens, mediaByPath, referencedMediaIds);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error scanning JS file {File}", file);
                }
            }

            _logger.LogInformation("JavaScript scan complete");
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("JavaScript scan cancelled");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during bulk JavaScript scan");
        }
    }

    private async Task ScanCssBulkAsync(
        HashSet<int> referencedMediaIds,
        Dictionary<string, int> mediaByKey,
        Dictionary<string, int> mediaByKeyNoHyphens,
        Dictionary<string, int> mediaByPath,
        ReferenceScanStatistics statistics,
        CancellationToken cancellationToken)
    {
        try
        {
            var wwwrootPath = Path.Combine(_webHostEnvironment.ContentRootPath, "wwwroot");
            if (!Directory.Exists(wwwrootPath)) return;

            var cssFiles = Directory.GetFiles(wwwrootPath, "*.css", SearchOption.AllDirectories);
            statistics.CssFilesScanned = cssFiles.Length;

            _logger.LogInformation("Scanning {Count} CSS files", cssFiles.Length);

            foreach (var file in cssFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var content = await System.IO.File.ReadAllTextAsync(file, cancellationToken);
                    var contentLower = content.ToLowerInvariant();

                    ScanContentForMediaReferences(contentLower, content, mediaByKey, mediaByKeyNoHyphens, mediaByPath, referencedMediaIds);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error scanning CSS file {File}", file);
                }
            }

            _logger.LogInformation("CSS scan complete");
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("CSS scan cancelled");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during bulk CSS scan");
        }
    }

    private async Task ScanTypeScriptBulkAsync(
        HashSet<int> referencedMediaIds,
        Dictionary<string, int> mediaByKey,
        Dictionary<string, int> mediaByKeyNoHyphens,
        Dictionary<string, int> mediaByPath,
        ReferenceScanStatistics statistics,
        CancellationToken cancellationToken)
    {
        try
        {
            var rootPath = _webHostEnvironment.ContentRootPath;
            var tsFiles = Directory.GetFiles(rootPath, "*.ts", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles(rootPath, "*.tsx", SearchOption.AllDirectories))
                .Where(f => !f.Contains("node_modules") && !f.Contains("bin") && !f.Contains("obj"))
                .ToArray();

            statistics.TypeScriptFilesScanned = tsFiles.Length;

            _logger.LogInformation("Scanning {Count} TypeScript files", tsFiles.Length);

            foreach (var file in tsFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var content = await System.IO.File.ReadAllTextAsync(file, cancellationToken);
                    var contentLower = content.ToLowerInvariant();

                    ScanContentForMediaReferences(contentLower, content, mediaByKey, mediaByKeyNoHyphens, mediaByPath, referencedMediaIds);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error scanning TS file {File}", file);
                }
            }

            _logger.LogInformation("TypeScript scan complete");
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("TypeScript scan cancelled");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during bulk TypeScript scan");
        }
    }

    private async Task ScanScssBulkAsync(
        HashSet<int> referencedMediaIds,
        Dictionary<string, int> mediaByKey,
        Dictionary<string, int> mediaByKeyNoHyphens,
        Dictionary<string, int> mediaByPath,
        ReferenceScanStatistics statistics,
        CancellationToken cancellationToken)
    {
        try
        {
            var rootPath = _webHostEnvironment.ContentRootPath;
            var scssFiles = Directory.GetFiles(rootPath, "*.scss", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles(rootPath, "*.less", SearchOption.AllDirectories))
                .Where(f => !f.Contains("node_modules") && !f.Contains("bin") && !f.Contains("obj"))
                .ToArray();

            statistics.ScssFilesScanned = scssFiles.Length;

            _logger.LogInformation("Scanning {Count} SCSS/LESS files", scssFiles.Length);

            foreach (var file in scssFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var content = await System.IO.File.ReadAllTextAsync(file, cancellationToken);
                    var contentLower = content.ToLowerInvariant();

                    ScanContentForMediaReferences(contentLower, content, mediaByKey, mediaByKeyNoHyphens, mediaByPath, referencedMediaIds);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error scanning SCSS file {File}", file);
                }
            }

            _logger.LogInformation("SCSS scan complete");
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("SCSS scan cancelled");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during bulk SCSS scan");
        }
    }

    private async Task ScanWwwrootBulkAsync(
        HashSet<int> referencedMediaIds,
        Dictionary<string, int> mediaByKey,
        Dictionary<string, int> mediaByKeyNoHyphens,
        Dictionary<string, int> mediaByPath,
        ReferenceScanStatistics statistics,
        CancellationToken cancellationToken)
    {
        try
        {
            var wwwrootPath = Path.Combine(_webHostEnvironment.ContentRootPath, "wwwroot");
            if (!Directory.Exists(wwwrootPath)) return;

            var patterns = new[] { "*.html", "*.htm", "*.xml", "*.json", "*.svg" };
            var files = patterns.SelectMany(p => Directory.GetFiles(wwwrootPath, p, SearchOption.AllDirectories)).ToArray();

            statistics.WwwrootFilesScanned = files.Length;

            _logger.LogInformation("Scanning {Count} wwwroot files", files.Length);

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var content = await System.IO.File.ReadAllTextAsync(file, cancellationToken);
                    var contentLower = content.ToLowerInvariant();

                    ScanContentForMediaReferences(contentLower, content, mediaByKey, mediaByKeyNoHyphens, mediaByPath, referencedMediaIds);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error scanning wwwroot file {File}", file);
                }
            }

            _logger.LogInformation("Wwwroot scan complete");
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Wwwroot scan cancelled");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during bulk wwwroot scan");
        }
    }

    private async Task ScanConfigBulkAsync(
        HashSet<int> referencedMediaIds,
        Dictionary<string, int> mediaByKey,
        Dictionary<string, int> mediaByKeyNoHyphens,
        Dictionary<string, int> mediaByPath,
        ReferenceScanStatistics statistics,
        CancellationToken cancellationToken)
    {
        try
        {
            var rootPath = _webHostEnvironment.ContentRootPath;
            var configFiles = Directory.GetFiles(rootPath, "appsettings*.json", SearchOption.TopDirectoryOnly).ToList();

            var webConfigPath = Path.Combine(rootPath, "web.config");
            if (System.IO.File.Exists(webConfigPath))
            {
                configFiles.Add(webConfigPath);
            }

            statistics.ConfigFilesScanned = configFiles.Count;

            _logger.LogInformation("Scanning {Count} config files", configFiles.Count);

            foreach (var file in configFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var content = await System.IO.File.ReadAllTextAsync(file, cancellationToken);
                    var contentLower = content.ToLowerInvariant();

                    ScanContentForMediaReferences(contentLower, content, mediaByKey, mediaByKeyNoHyphens, mediaByPath, referencedMediaIds);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error scanning config file {File}", file);
                }
            }

            _logger.LogInformation("Config scan complete");
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Config scan cancelled");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during bulk config scan");
        }
    }

    private void ScanContentForMediaReferences(
        string contentLower,
        string content,
        Dictionary<string, int> mediaByKey,
        Dictionary<string, int> mediaByKeyNoHyphens,
        Dictionary<string, int> mediaByPath,
        HashSet<int> referencedMediaIds)
    {
        // Check for media keys
        foreach (var kvp in mediaByKey)
        {
            if (contentLower.Contains(kvp.Key))
            {
                referencedMediaIds.Add(kvp.Value);
            }
        }

        // Check for media keys without hyphens and UDI format
        foreach (var kvp in mediaByKeyNoHyphens)
        {
            if (contentLower.Contains(kvp.Key) || contentLower.Contains($"umb://media/{kvp.Key}"))
            {
                referencedMediaIds.Add(kvp.Value);
            }
        }

        // Check for media paths
        foreach (var kvp in mediaByPath)
        {
            if (contentLower.Contains(kvp.Key))
            {
                referencedMediaIds.Add(kvp.Value);
            }
        }
    }
}
