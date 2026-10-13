namespace MarketData.Service.Exceptions;

/// <summary>
/// Exception thrown when an external market data provider rejects authentication (HTTP 401 or 403).
/// </summary>
public class MarketDataAuthenticationException : Exception
{
    public string Provider { get; }

    public MarketDataAuthenticationException(string provider, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Provider = provider;
    }
}
