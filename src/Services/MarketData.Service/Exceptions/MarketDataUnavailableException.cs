namespace MarketData.Service.Exceptions;

public class MarketDataUnavailableException : Exception
{
    public string Symbol { get; }
    public IReadOnlyList<string> ProviderErrors { get; }

    public MarketDataUnavailableException(string symbol, IEnumerable<string> errors)
        : base($"Market data unavailable for symbol '{symbol}'. All providers failed and no stale cache was available: {string.Join("; ", errors)}")
    {
        Symbol = symbol;
        ProviderErrors = errors.ToList();
    }

    public MarketDataUnavailableException(string symbol, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Symbol = symbol;
        ProviderErrors = innerException != null ? new[] { innerException.Message } : Array.Empty<string>();
    }
}
