using Shared.Contracts.Dtos;

namespace MarketData.Service.Providers;

public interface IMarketDataProvider
{
    string ProviderName { get; }
    int Priority { get; }
    Task<MarketQuoteDto> GetQuoteAsync(string symbol, CancellationToken cancellationToken = default);
}
