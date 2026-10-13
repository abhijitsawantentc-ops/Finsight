using MarketData.Service.Exceptions;
using MarketData.Service.Providers;
using MarketData.Service.Resilience.Polly;
using MarketData.Service.Resilience.RateLimiting;
using MarketData.Service.Services.Caching;
using Microsoft.Extensions.Logging;
using Shared.Contracts.Dtos;

namespace MarketData.Service.Services.Orchestration;

public class MarketDataOrchestrator : IMarketDataOrchestrator
{
    private readonly ICacheService _cacheService;
    private readonly IProviderRateLimiter _rateLimiter;
    private readonly IPollyResilienceRegistry _resilienceRegistry;
    private readonly IReadOnlyList<IMarketDataProvider> _providers;
    private readonly ILogger<MarketDataOrchestrator> _logger;

    public MarketDataOrchestrator(
        ICacheService cacheService,
        IProviderRateLimiter rateLimiter,
        IPollyResilienceRegistry resilienceRegistry,
        IEnumerable<IMarketDataProvider> providers,
        ILogger<MarketDataOrchestrator> logger)
    {
        _cacheService = cacheService;
        _rateLimiter = rateLimiter;
        _resilienceRegistry = resilienceRegistry;
        _logger = logger;
        _providers = providers.OrderBy(p => p.Priority).ToList();
    }

    public async Task<MarketQuoteDto> GetQuoteAsync(string symbol, CancellationToken cancellationToken = default)
    {
        var upperSymbol = symbol.ToUpperInvariant();

        // 1. Hot Cache Lookup (60 seconds TTL)
        var hotQuote = await _cacheService.GetHotQuoteAsync(upperSymbol, cancellationToken);
        if (hotQuote != null)
        {
            _logger.LogDebug("Cache HIT (Hot, <=60s) for symbol {Symbol}", upperSymbol);
            return hotQuote with { IsStale = false };
        }

        _logger.LogInformation("Cache MISS (Hot) for symbol {Symbol}. Querying external providers...", upperSymbol);

        var errors = new List<string>();

        // 2. Query Providers with Primary -> Secondary Failover
        for (var i = 0; i < _providers.Count; i++)
        {
            var provider = _providers[i];
            var isPrimary = i == 0;
            var providerRole = isPrimary ? "Primary" : "Secondary Failover";

            try
            {
                _logger.LogInformation("Attempting {Role} provider '{Provider}' for symbol {Symbol}",
                    providerRole, provider.ProviderName, upperSymbol);

                // Execute via rate limiter and Polly resilience pipeline (Timeout -> Retry w/ Backoff -> Circuit Breaker)
                var pipeline = _resilienceRegistry.GetPipeline(provider.ProviderName);

                var quote = await _rateLimiter.ExecuteWithRateLimitAsync(
                    provider.ProviderName,
                    async ct => await pipeline.ExecuteAsync(async innerCt => await provider.GetQuoteAsync(upperSymbol, innerCt), ct),
                    cancellationToken);

                if (quote != null)
                {
                    _logger.LogInformation("Successfully received quote for {Symbol} from {Role} provider '{Provider}'",
                        upperSymbol, providerRole, provider.ProviderName);

                    // Update hot (60s) and stale (24h) cache
                    await _cacheService.SetQuoteAsync(quote, cancellationToken);
                    return quote with { IsStale = false, SourceProvider = provider.ProviderName };
                }
            }
            catch (Exception ex)
            {
                var errorMsg = $"[{providerRole}:{provider.ProviderName}] {ex.GetType().Name}: {ex.Message}";
                errors.Add(errorMsg);

                if (isPrimary && _providers.Count > 1)
                {
                    _logger.LogWarning(ex, "Primary provider '{Provider}' failed for {Symbol}. Triggering automatic failover to secondary provider.",
                        provider.ProviderName, upperSymbol);
                }
                else
                {
                    _logger.LogWarning(ex, "Provider '{Provider}' failed for {Symbol}.", provider.ProviderName, upperSymbol);
                }
            }
        }

        // 3. Stale-Cache Fallback (<= 24 hours TTL)
        _logger.LogWarning("All providers failed for symbol {Symbol}. Attempting stale cache fallback (<=24h)...", upperSymbol);
        var staleQuote = await _cacheService.GetStaleQuoteAsync(upperSymbol, cancellationToken);
        if (staleQuote != null)
        {
            _logger.LogWarning("Stale cache fallback HIT for symbol {Symbol}. Returning quote marked with IsStale=true.", upperSymbol);
            return staleQuote with { IsStale = true, SourceProvider = "StaleCacheFallback" };
        }

        // 4. Complete Failure - No cache and all providers exhausted
        _logger.LogError("Critical market data failure: All providers and stale cache fallback exhausted for {Symbol}. Errors: {Errors}",
            upperSymbol, string.Join("; ", errors));

        throw new MarketDataUnavailableException(upperSymbol, errors);
    }

    public Task<MarketDataOrchestratorStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var primary = _providers.ElementAtOrDefault(0)?.ProviderName ?? "None";
        var secondary = _providers.ElementAtOrDefault(1)?.ProviderName ?? "None";

        var status = new MarketDataOrchestratorStatus(
            HotCacheActive: true,
            StaleCacheActive: true,
            PrimaryProvider: primary,
            SecondaryProvider: secondary,
            PrimaryRateLimitAvailable: _rateLimiter.CanAcquire(primary),
            SecondaryRateLimitAvailable: _rateLimiter.CanAcquire(secondary),
            TimestampUtc: DateTime.UtcNow
        );

        return Task.FromResult(status);
    }
}
