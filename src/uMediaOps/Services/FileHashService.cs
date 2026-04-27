using System.Security.Cryptography;
using uMediaOps.Models;
using uMediaOps.Repositories;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Services;

namespace uMediaOps.Services;

/// <summary>
/// Service for computing and managing file hashes
/// </summary>
public interface IFileHashService
{
    Task<string> ComputeHashAsync(Stream fileStream);
    Task<FileHash> GetOrComputeHashAsync(int mediaId, Stream fileStream, long fileSize);
}

/// <summary>
/// Implementation of file hash service
/// </summary>
public class FileHashService : IFileHashService
{
    private readonly IFileHashRepository _repository;
    private readonly ILogger<FileHashService> _logger;
    private const int BufferSize = 81920; // 80KB chunks for efficient large file hashing

    public FileHashService(
        IFileHashRepository repository,
        ILogger<FileHashService> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<string> ComputeHashAsync(Stream fileStream)
    {
        try
        {
            using var sha256 = SHA256.Create();
            
            // Reset stream position if possible
            if (fileStream.CanSeek)
            {
                fileStream.Position = 0;
            }

            // Read file in chunks to handle large files efficiently
            var buffer = new byte[BufferSize];
            int bytesRead;

            while ((bytesRead = await fileStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                sha256.TransformBlock(buffer, 0, bytesRead, null, 0);
            }

            // Finalize hash computation
            sha256.TransformFinalBlock(Array.Empty<byte>(), 0, 0);

            // Convert hash to hex string
            var hashBytes = sha256.Hash ?? Array.Empty<byte>();
            return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error computing hash for stream");
            throw;
        }
    }

    public async Task<FileHash> GetOrComputeHashAsync(int mediaId, Stream fileStream, long fileSize)
    {
        try
        {
            // Check if hash already exists
            var existing = await _repository.GetByMediaIdAsync(mediaId);

            // If exists and file size matches, return cached hash
            if (existing != null && existing.FileSize == fileSize)
            {
                _logger.LogDebug("Using cached hash for media {MediaId}", mediaId);
                return existing;
            }

            // Compute new hash
            _logger.LogDebug("Computing new hash for media {MediaId}", mediaId);
            var hash = await ComputeHashAsync(fileStream);

            var fileHash = new FileHash
            {
                MediaId = mediaId,
                Hash = hash,
                FileSize = fileSize,
                ComputedAt = DateTime.UtcNow
            };

            // Save to database
            await _repository.SaveAsync(fileHash);

            return fileHash;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting or computing hash for media {MediaId}", mediaId);
            throw;
        }
    }

}
