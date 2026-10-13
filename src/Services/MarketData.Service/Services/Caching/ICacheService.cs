using Shared.Contracts.Dtos;

namespace MarketData.Service.Services.Caching;

public interface ICacheService
{
    /// <summary>
    /// Writes the quote to both 60-second hot cache and 24-hour stale cache.
    /// </summary>
    Task SetQuoteAsync(MarketQuoteDto quote, CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempts to retrieve fresh quote from 60-second hot cache.
    /// Returns null if hot cache expired or key not found.
    /// </summary>
    Task<MarketQuoteDto?> GetHotQuoteAsync(string symbol, CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempts to retrieve fallback quote from 24-hour stale cache.
    /// Result will have IsStale = true.
    /// Returns null if stale cache expired (>24 hours) or key not found.
    /// </summary>
    Task<MarketQuoteDto?> GetStaleQuoteAsync(string symbol, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves quote from hot cache if available; otherwise returns null.
    /// </summary>
    Task<MarketQuoteDto?> GetQuoteAsync(string symbol, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all known symbols currently stored across active caches.
    /// </summary>
    Task<IReadOnlyList<string>> GetAllSymbolsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears expired entries from memory stores (for maintenance or testing).
    /// </summary>
    void CleanupExpiredEntries();
}
