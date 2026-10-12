using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Shared.Contracts.Dtos;
using StackExchange.Redis;

namespace MarketData.Service.Services;

public interface IRedisMarketCacheService
{
    Task SetQuoteAsync(MarketQuoteDto quote, CancellationToken cancellationToken = default);
    Task<MarketQuoteDto?> GetQuoteAsync(string symbol, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetAllSymbolsAsync(CancellationToken cancellationToken = default);
}

public class RedisMarketCacheService : IRedisMarketCacheService
{
    private readonly IConnectionMultiplexer? _redis;
    private readonly ILogger<RedisMarketCacheService> _logger;
    private readonly ConcurrentDictionary<string, MarketQuoteDto> _inMemoryFallback = new();

    public RedisMarketCacheService(
        ILogger<RedisMarketCacheService> logger,
        IConnectionMultiplexer? redis = null)
    {
        _logger = logger;
        _redis = redis;
    }

    public async Task SetQuoteAsync(MarketQuoteDto quote, CancellationToken cancellationToken = default)
    {
        _inMemoryFallback[quote.Symbol.ToUpperInvariant()] = quote;

        if (_redis != null && _redis.IsConnected)
        {
            try
            {
                var db = _redis.GetDatabase();
                var key = $"quotes:{quote.Symbol.ToUpperInvariant()}";
                var json = JsonSerializer.Serialize(quote);
                await db.StringSetAsync(key, json, TimeSpan.FromMinutes(10));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to write quote for {Symbol} to Redis. Using in-memory fallback.", quote.Symbol);
            }
        }
    }

    public async Task<MarketQuoteDto?> GetQuoteAsync(string symbol, CancellationToken cancellationToken = default)
    {
        var keySymbol = symbol.ToUpperInvariant();

        if (_redis != null && _redis.IsConnected)
        {
            try
            {
                var db = _redis.GetDatabase();
                var key = $"quotes:{keySymbol}";
                var value = await db.StringGetAsync(key);

                if (value.HasValue)
                {
                    return JsonSerializer.Deserialize<MarketQuoteDto>(value.ToString()!);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read quote for {Symbol} from Redis. Checking in-memory fallback.", symbol);
            }
        }

        if (_inMemoryFallback.TryGetValue(keySymbol, out var cachedQuote))
        {
            return cachedQuote;
        }

        return null;
    }

    public Task<IReadOnlyList<string>> GetAllSymbolsAsync(CancellationToken cancellationToken = default)
    {
        var symbols = _inMemoryFallback.Keys.ToList();
        return Task.FromResult<IReadOnlyList<string>>(symbols);
    }
}
