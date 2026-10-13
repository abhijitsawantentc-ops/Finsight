namespace MarketData.Service.Exceptions;

/// <summary>
/// Exception thrown when a requested ticker symbol is not found by an external market data provider (HTTP 404).
/// </summary>
public class SymbolNotFoundException : Exception
{
    public string Provider { get; }
    public string Symbol { get; }

    public SymbolNotFoundException(string provider, string symbol, string? message = null)
        : base(message ?? $"Symbol '{symbol}' was not found by market data provider '{provider}'.")
    {
        Provider = provider;
        Symbol = symbol;
    }
}
