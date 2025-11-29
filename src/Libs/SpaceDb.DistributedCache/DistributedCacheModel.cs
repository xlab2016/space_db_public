using System.Collections.Concurrent;
using System.Diagnostics;

namespace SpaceDb.DistributedCache;

/// <summary>
/// High-performance distributed cache model with support for async background refresh.
/// Thread-safe and optimized for high loads (1K+ RPS).
/// </summary>
public class DistributedCacheModel
{
    private class CacheEntry
    {
        public object? Value { get; set; }
        public DateTime ExpiresAt { get; set; }
        public bool IsRefreshing { get; set; }
        public Task? RefreshTask { get; set; }
    }

    private static readonly ConcurrentDictionary<string, CacheEntry> _cache = new();
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    // Statistics tracking
    private static long _putHitsCount;
    private static long _getHitsCount;
    private static readonly Stopwatch _putStopwatch = Stopwatch.StartNew();
    private static readonly Stopwatch _getStopwatch = Stopwatch.StartNew();
    private static long _putLastHitsCount;
    private static long _getLastHitsCount;
    private static long _putLastTicks;
    private static long _getLastTicks;

    /// <summary>
    /// Stores a value in cache with automatic refresh support.
    /// </summary>
    /// <typeparam name="T">Type of the cached value.</typeparam>
    /// <param name="key">Cache key.</param>
    /// <param name="liveTime">Time-to-live for the cached value.</param>
    /// <param name="getAsync">Function to retrieve the value if not in cache or expired.</param>
    /// <param name="asyncGet">If true, refreshes cache in background while serving stale data.</param>
    /// <returns>The cached or newly retrieved value.</returns>
    public async Task<T> Put<T>(string key, TimeSpan liveTime, Task<T> getAsync, bool asyncGet = false)
    {
        // Track statistics
        Interlocked.Increment(ref _putHitsCount);

        var now = DateTime.UtcNow;

        // Fast path: check if value exists and is not expired
        if (_cache.TryGetValue(key, out var entry))
        {
            if (entry.ExpiresAt > now)
            {
                return (T)entry.Value!;
            }

            // Value expired
            if (asyncGet)
            {
                // Return stale value while refreshing in background
                if (!entry.IsRefreshing)
                {
                    // Start background refresh
                    var lockObj = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));

                    // Try to acquire lock without blocking
                    if (lockObj.Wait(0))
                    {
                        try
                        {
                            // Double-check after acquiring lock
                            if (!entry.IsRefreshing)
                            {
                                entry.IsRefreshing = true;

                                // Start background refresh task
                                _ = Task.Run(async () =>
                                {
                                    try
                                    {
                                        var newValue = await getAsync;

                                        // Update cache entry
                                        if (_cache.TryGetValue(key, out var currentEntry))
                                        {
                                            currentEntry.Value = newValue;
                                            currentEntry.ExpiresAt = DateTime.UtcNow.Add(liveTime);
                                            currentEntry.IsRefreshing = false;
                                        }
                                    }
                                    catch
                                    {
                                        // On error, mark as not refreshing so retry can happen
                                        if (_cache.TryGetValue(key, out var currentEntry))
                                        {
                                            currentEntry.IsRefreshing = false;
                                        }
                                    }
                                });
                            }
                        }
                        finally
                        {
                            lockObj.Release();
                        }
                    }
                }

                // Return stale value
                return (T)entry.Value!;
            }
        }

        // Slow path: need to fetch value synchronously
        var lockSemaphore = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await lockSemaphore.WaitAsync();

        try
        {
            // Double-check pattern: another thread might have populated the cache
            if (_cache.TryGetValue(key, out var existingEntry) && existingEntry.ExpiresAt > DateTime.UtcNow)
            {
                return (T)existingEntry.Value!;
            }

            // Fetch new value
            var value = await getAsync;

            // Store in cache
            var newEntry = new CacheEntry
            {
                Value = value,
                ExpiresAt = DateTime.UtcNow.Add(liveTime),
                IsRefreshing = false
            };

            _cache[key] = newEntry;

            return value;
        }
        finally
        {
            lockSemaphore.Release();
        }
    }

    /// <summary>
    /// Retrieves a value from cache.
    /// </summary>
    /// <typeparam name="T">Type of the cached value.</typeparam>
    /// <param name="key">Cache key.</param>
    /// <returns>The cached value, or default if not found or expired.</returns>
    public Task<T?> Get<T>(string key)
    {
        // Track statistics
        Interlocked.Increment(ref _getHitsCount);

        if (_cache.TryGetValue(key, out var entry))
        {
            if (entry.ExpiresAt > DateTime.UtcNow)
            {
                return Task.FromResult((T?)entry.Value);
            }
        }

        return Task.FromResult(default(T));
    }

    /// <summary>
    /// Gets statistics for Put operations.
    /// </summary>
    /// <returns>Statistics model with hits count and RPS.</returns>
    public StatisticModel GetPutStatistics()
    {
        var currentTicks = _putStopwatch.ElapsedTicks;
        var currentHits = Interlocked.Read(ref _putHitsCount);

        var lastTicks = Interlocked.Read(ref _putLastTicks);
        var lastHits = Interlocked.Read(ref _putLastHitsCount);

        var ticksDelta = currentTicks - lastTicks;
        var hitsDelta = currentHits - lastHits;

        var rps = 0.0;
        if (ticksDelta > 0)
        {
            rps = (double)hitsDelta / ticksDelta * Stopwatch.Frequency;
        }

        // Update last values for next calculation
        Interlocked.Exchange(ref _putLastTicks, currentTicks);
        Interlocked.Exchange(ref _putLastHitsCount, currentHits);

        return new StatisticModel
        {
            HitsCount = currentHits,
            Rps = rps
        };
    }

    /// <summary>
    /// Gets statistics for Get operations.
    /// </summary>
    /// <returns>Statistics model with hits count and RPS.</returns>
    public StatisticModel GetGetStatistics()
    {
        var currentTicks = _getStopwatch.ElapsedTicks;
        var currentHits = Interlocked.Read(ref _getHitsCount);

        var lastTicks = Interlocked.Read(ref _getLastTicks);
        var lastHits = Interlocked.Read(ref _getLastHitsCount);

        var ticksDelta = currentTicks - lastTicks;
        var hitsDelta = currentHits - lastHits;

        var rps = 0.0;
        if (ticksDelta > 0)
        {
            rps = (double)hitsDelta / ticksDelta * Stopwatch.Frequency;
        }

        // Update last values for next calculation
        Interlocked.Exchange(ref _getLastTicks, currentTicks);
        Interlocked.Exchange(ref _getLastHitsCount, currentHits);

        return new StatisticModel
        {
            HitsCount = currentHits,
            Rps = rps
        };
    }

    /// <summary>
    /// Clears all cached entries. Useful for testing.
    /// </summary>
    public void Clear()
    {
        _cache.Clear();
    }
}
