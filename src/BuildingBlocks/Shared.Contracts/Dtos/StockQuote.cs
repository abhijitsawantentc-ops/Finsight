namespace Shared.Contracts.Dtos;

/// <summary>
/// Unified stock quote record used across the platform for market data ingestion,
/// algorithmic pricing, caching, and risk calculations.
/// </summary>
public record StockQuote(
    string Symbol,
    decimal Price,
    decimal Bid,
    decimal Ask,
    long Volume,
    string Currency,
    DateTime TimestampUtc,
    bool IsStale = false,
    string? SourceProvider = null
)
{
    /// <summary>
    /// Converts this unified StockQuote to a MarketQuoteDto.
    /// </summary>
    public MarketQuoteDto ToMarketQuoteDto() =>
        new(Symbol, Price, Bid, Ask, Volume, Currency, TimestampUtc, IsStale, SourceProvider);

    public static implicit operator MarketQuoteDto(StockQuote quote) =>
        new(quote.Symbol, quote.Price, quote.Bid, quote.Ask, quote.Volume, quote.Currency, quote.TimestampUtc, quote.IsStale, quote.SourceProvider);

    public static implicit operator StockQuote(MarketQuoteDto dto) =>
        new(dto.Symbol, dto.Price, dto.Bid, dto.Ask, dto.Volume, dto.Currency, dto.TimestampUtc, dto.IsStale, dto.SourceProvider);
}
