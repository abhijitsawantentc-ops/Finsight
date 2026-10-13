using Shared.Contracts.Dtos;

namespace MarketData.Service.Providers;

/// <summary>
/// Standardizes interactions with external market data providers (e.g. Finnhub, Alpha Vantage).
/// </summary>
public interface IMarketDataProvider
{
    /// <summary>
    /// Unique identifier and display name of the market data provider.
    /// </summary>
    string ProviderName { get; }

    /// <summary>
    /// Priority order for provider resolution (1 = Primary, 2 = Secondary failover).
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// Fetches the latest real-time stock quote for the specified symbol.
    /// </summary>
    /// <param name="symbol">Ticker symbol (e.g., AAPL, MSFT).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Unified <see cref="StockQuote"/> containing current market pricing.</returns>
    Task<StockQuote> GetQuoteAsync(string symbol, CancellationToken cancellationToken = default);
}
