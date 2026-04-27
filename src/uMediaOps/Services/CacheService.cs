using Microsoft.Extensions.Caching.Memory;

namespace uMediaOps.Services;

/// <summary>
/// Service for caching scan results and other data
/// </summary>
public interface ICacheService
{
    T? Get<T>(string key);
    void Set<T>(string key, T value, TimeSpan? expiration = null);
    void Remove(string key);
    void RemoveByPattern(string pattern);
    /// <summary>
    /// Atomically sets a value only if the key does not already exist.
    /// Returns true if the value was set, false if the key already existed.
    /// </summary>
    bool TrySetIfAbsent<T>(string key, T value, TimeSpan? expiration = null);
}

/// <summary>
/// Implementation of cache service using IMemoryCache
/// </summary>
public class CacheService : ICacheService
{
    private readonly IMemoryCache _cache;
    private readonly HashSet<string> _cacheKeys = new();
    private readonly object _lock = new();
    private const int DefaultExpirationMinutes = 5;

    public CacheService(IMemoryCache cache)
    {
        _cache = cache;
    }

    public T? Get<T>(string key)
    {
        return _cache.TryGetValue(key, out T? value) ? value : default;
    }

    public void Set<T>(string key, T value, TimeSpan? expiration = null)
    {
        var expirationTime = expiration ?? TimeSpan.FromMinutes(DefaultExpirationMinutes);
        
        var cacheEntryOptions = new MemoryCacheEntryOptions()
            .SetAbsoluteExpiration(expirationTime)
            .RegisterPostEvictionCallback((k, v, r, s) =>
            {
                // Remove from tracking when evicted
                lock (_lock)
                {
                    _cacheKeys.Remove(k.ToString() ?? string.Empty);
                }
            });

        _cache.Set(key, value, cacheEntryOptions);
        
        // Track the key
        lock (_lock)
        {
            _cacheKeys.Add(key);
        }
    }

    public void Remove(string key)
    {
        _cache.Remove(key);
        lock (_lock)
        {
            _cacheKeys.Remove(key);
        }
    }

    public void RemoveByPattern(string pattern)
    {
        lock (_lock)
        {
            var keysToRemove = _cacheKeys
                .Where(k => k.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var key in keysToRemove)
            {
                _cache.Remove(key);
                _cacheKeys.Remove(key);
            }
        }
    }

    public bool TrySetIfAbsent<T>(string key, T value, TimeSpan? expiration = null)
    {
        lock (_lock)
        {
            if (_cache.TryGetValue(key, out _))
            {
                return false; // Key already exists
            }

            var expirationTime = expiration ?? TimeSpan.FromMinutes(DefaultExpirationMinutes);
            var cacheEntryOptions = new MemoryCacheEntryOptions()
                .SetAbsoluteExpiration(expirationTime)
                .RegisterPostEvictionCallback((k, v, r, s) =>
                {
                    lock (_lock)
                    {
                        _cacheKeys.Remove(k.ToString() ?? string.Empty);
                    }
                });

            _cache.Set(key, value, cacheEntryOptions);
            _cacheKeys.Add(key);
            return true;
        }
    }
}

/// <summary>
/// Cache key constants for uMediaOps
/// </summary>
public static class CacheKeys
{
    public const string DuplicateScanResult = "umediaops:duplicates:scan-result";
    public const string DuplicateScanProgress = "umediaops:duplicates:scan-progress";
    public const string DuplicateGroups = "umediaops:duplicates:groups";
    
    public const string UnusedMediaScanResult = "umediaops:unused:scan-result";
    public const string UnusedMediaScanProgress = "umediaops:unused:scan-progress";
}
