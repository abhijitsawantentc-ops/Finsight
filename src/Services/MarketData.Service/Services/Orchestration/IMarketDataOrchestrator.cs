using Shared.Contracts.Dtos;

namespace MarketData.Service.Services.Orchestration;

public interface IMarketDataOrchestrator
{
    /// <summary>
    /// Retrieves market quote using hot cache (60s), primary provider (Finnhub),
    /// secondary failover provider (Alpha Vantage), and stale cache fallback (24h).
    /// </summary>
    Task<MarketQuoteDto> GetQuoteAsync(string symbol, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns current health/resilience status of providers and caches.
    /// </summary>
    Task<MarketDataOrchestratorStatus> GetStatusAsync(CancellationToken cancellationToken = default);
}

public record MarketDataOrchestratorStatus(
    bool HotCacheActive,
    bool StaleCacheActive,
    string PrimaryProvider,
    string SecondaryProvider,
    bool PrimaryRateLimitAvailable,
    bool SecondaryRateLimitAvailable,
    DateTime TimestampUtc
);
