using MarketData.Service.Services.Caching;
using Shared.Contracts.Dtos;

namespace MarketData.Service.Services;

public interface IRedisMarketCacheService
{
    Task SetQuoteAsync(MarketQuoteDto quote, CancellationToken cancellationToken = default);
    Task<MarketQuoteDto?> GetQuoteAsync(string symbol, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetAllSymbolsAsync(CancellationToken cancellationToken = default);
}

public class RedisMarketCacheService : IRedisMarketCacheService
{
    private readonly ICacheService _cacheService;

    public RedisMarketCacheService(ICacheService cacheService)
    {
        _cacheService = cacheService;
    }

    public Task SetQuoteAsync(MarketQuoteDto quote, CancellationToken cancellationToken = default)
        => _cacheService.SetQuoteAsync(quote, cancellationToken);

    public Task<MarketQuoteDto?> GetQuoteAsync(string symbol, CancellationToken cancellationToken = default)
        => _cacheService.GetHotQuoteAsync(symbol, cancellationToken);

    public Task<IReadOnlyList<string>> GetAllSymbolsAsync(CancellationToken cancellationToken = default)
        => _cacheService.GetAllSymbolsAsync(cancellationToken);
}
