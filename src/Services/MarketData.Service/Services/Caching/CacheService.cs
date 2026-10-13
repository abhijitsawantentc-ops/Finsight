using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Shared.Contracts.Dtos;
using StackExchange.Redis;

namespace MarketData.Service.Services.Caching;

public class CacheServiceOptions
{
    public TimeSpan HotCacheTtl { get; set; } = TimeSpan.FromSeconds(60);
    public TimeSpan StaleCacheTtl { get; set; } = TimeSpan.FromHours(24);
}

public class CacheService : ICacheService, IRedisMarketCacheService
{
    private readonly IConnectionMultiplexer? _redis;
    private readonly ILogger<CacheService> _logger;
    private readonly TimeSpan _hotCacheTtl;
    private readonly TimeSpan _staleCacheTtl;

    // In-memory dual-tier cache fallbacks: stores item + Utc expiration
    private readonly ConcurrentDictionary<string, (MarketQuoteDto Quote, DateTime ExpiresAtUtc)> _inMemoryHot = new();
    private readonly ConcurrentDictionary<string, (MarketQuoteDto Quote, DateTime ExpiresAtUtc)> _inMemoryStale = new();

    public CacheService(
        ILogger<CacheService> logger,
        IConnectionMultiplexer? redis = null,
        CacheServiceOptions? options = null)
    {
        _logger = logger;
        _redis = redis;
        _hotCacheTtl = options?.HotCacheTtl ?? TimeSpan.FromSeconds(60);
        _staleCacheTtl = options?.StaleCacheTtl ?? TimeSpan.FromHours(24);
    }

    public async Task SetQuoteAsync(MarketQuoteDto quote, CancellationToken cancellationToken = default)
    {
        var keySymbol = quote.Symbol.ToUpperInvariant();
        var now = DateTime.UtcNow;

        // 1. Hot entry (60s)
        var hotQuote = quote with { IsStale = false };
        _inMemoryHot[keySymbol] = (hotQuote, now.Add(_hotCacheTtl));

        // 2. Stale entry (24h)
        var staleQuote = quote with { IsStale = true };
        _inMemoryStale[keySymbol] = (staleQuote, now.Add(_staleCacheTtl));

        // 3. Write to Redis if available
        if (_redis != null && _redis.IsConnected)
        {
            try
            {
                var db = _redis.GetDatabase();
                var hotKey = $"quotes:hot:{keySymbol}";
                var staleKey = $"quotes:stale:{keySymbol}";
                var legacyKey = $"quotes:{keySymbol}";

                var hotJson = JsonSerializer.Serialize(hotQuote);
                var staleJson = JsonSerializer.Serialize(staleQuote);

                await Task.WhenAll(
                    db.StringSetAsync(hotKey, hotJson, _hotCacheTtl),
                    db.StringSetAsync(staleKey, staleJson, _staleCacheTtl),
                    db.StringSetAsync(legacyKey, hotJson, _hotCacheTtl)
                );
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to write dual-cache entry for {Symbol} to Redis. Active in-memory caches used.", quote.Symbol);
            }
        }
    }

    public async Task<MarketQuoteDto?> GetHotQuoteAsync(string symbol, CancellationToken cancellationToken = default)
    {
        var keySymbol = symbol.ToUpperInvariant();

        // 1. Check Redis Hot Cache
        if (_redis != null && _redis.IsConnected)
        {
            try
            {
                var db = _redis.GetDatabase();
                var hotKey = $"quotes:hot:{keySymbol}";
                var value = await db.StringGetAsync(hotKey);

                if (value.HasValue)
                {
                    var quote = JsonSerializer.Deserialize<MarketQuoteDto>(value.ToString()!);
                    if (quote != null)
                    {
                        return quote with { IsStale = false };
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read hot cache for {Symbol} from Redis. Checking in-memory hot cache.", symbol);
            }
        }

        // 2. Check In-Memory Hot Cache
        if (_inMemoryHot.TryGetValue(keySymbol, out var entry))
        {
            if (DateTime.UtcNow <= entry.ExpiresAtUtc)
            {
                return entry.Quote with { IsStale = false };
            }
            else
            {
                // Expired hot cache
                _inMemoryHot.TryRemove(keySymbol, out _);
            }
        }

        return null;
    }

    public async Task<MarketQuoteDto?> GetStaleQuoteAsync(string symbol, CancellationToken cancellationToken = default)
    {
        var keySymbol = symbol.ToUpperInvariant();

        // 1. Check Redis Stale Cache
        if (_redis != null && _redis.IsConnected)
        {
            try
            {
                var db = _redis.GetDatabase();
                var staleKey = $"quotes:stale:{keySymbol}";
                var value = await db.StringGetAsync(staleKey);

                if (value.HasValue)
                {
                    var quote = JsonSerializer.Deserialize<MarketQuoteDto>(value.ToString()!);
                    if (quote != null)
                    {
                        return quote with { IsStale = true };
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read stale cache for {Symbol} from Redis. Checking in-memory stale cache.", symbol);
            }
        }

        // 2. Check In-Memory Stale Cache
        if (_inMemoryStale.TryGetValue(keySymbol, out var entry))
        {
            if (DateTime.UtcNow <= entry.ExpiresAtUtc)
            {
                return entry.Quote with { IsStale = true };
            }
            else
            {
                // Expired stale cache
                _inMemoryStale.TryRemove(keySymbol, out _);
            }
        }

        return null;
    }

    public Task<MarketQuoteDto?> GetQuoteAsync(string symbol, CancellationToken cancellationToken = default)
    {
        // By default, GetQuoteAsync attempts hot cache lookup
        return GetHotQuoteAsync(symbol, cancellationToken);
    }

    public Task<IReadOnlyList<string>> GetAllSymbolsAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var activeSymbols = _inMemoryHot
            .Where(kvp => kvp.Value.ExpiresAtUtc >= now)
            .Select(kvp => kvp.Key)
            .Union(_inMemoryStale
                .Where(kvp => kvp.Value.ExpiresAtUtc >= now)
                .Select(kvp => kvp.Key))
            .Distinct()
            .ToList();

        return Task.FromResult<IReadOnlyList<string>>(activeSymbols);
    }

    public void CleanupExpiredEntries()
    {
        var now = DateTime.UtcNow;
        foreach (var (key, entry) in _inMemoryHot.ToList())
        {
            if (entry.ExpiresAtUtc < now)
            {
                _inMemoryHot.TryRemove(key, out _);
            }
        }

        foreach (var (key, entry) in _inMemoryStale.ToList())
        {
            if (entry.ExpiresAtUtc < now)
            {
                _inMemoryStale.TryRemove(key, out _);
            }
        }
    }
}
