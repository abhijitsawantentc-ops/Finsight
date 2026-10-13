using System.Text.Json.Serialization;
using Shared.Contracts.Dtos;

namespace MarketData.Service.Providers.Dtos;

/// <summary>
/// Strongly-typed Data Transfer Object representing the raw JSON quote payload from Finnhub API.
/// Endpoint: /api/v1/quote?symbol={symbol}&token={token}
/// </summary>
public class FinnhubQuoteResponse
{
    /// <summary>
    /// Current price ("c").
    /// </summary>
    [JsonPropertyName("c")]
    public decimal CurrentPrice { get; set; }

    /// <summary>
    /// Change ("d").
    /// </summary>
    [JsonPropertyName("d")]
    public decimal? Change { get; set; }

    /// <summary>
    /// Percent change ("dp").
    /// </summary>
    [JsonPropertyName("dp")]
    public decimal? PercentChange { get; set; }

    /// <summary>
    /// High price of the day ("h").
    /// </summary>
    [JsonPropertyName("h")]
    public decimal HighPrice { get; set; }

    /// <summary>
    /// Low price of the day ("l").
    /// </summary>
    [JsonPropertyName("l")]
    public decimal LowPrice { get; set; }

    /// <summary>
    /// Open price of the day ("o").
    /// </summary>
    [JsonPropertyName("o")]
    public decimal OpenPrice { get; set; }

    /// <summary>
    /// Previous close price ("pc").
    /// </summary>
    [JsonPropertyName("pc")]
    public decimal PreviousClosePrice { get; set; }

    /// <summary>
    /// UNIX timestamp in seconds ("t").
    /// </summary>
    [JsonPropertyName("t")]
    public long Timestamp { get; set; }

    /// <summary>
    /// Determines whether the response contains valid price data.
    /// </summary>
    public bool IsValid() => CurrentPrice > 0;

    /// <summary>
    /// Maps this provider DTO to the unified <see cref="StockQuote"/> model.
    /// </summary>
    public StockQuote ToStockQuote(string symbol, string providerName = "Finnhub")
    {
        var upperSymbol = symbol.ToUpperInvariant();
        var spread = Math.Max(0.01m, Math.Round(CurrentPrice * 0.0005m, 2));
        var bid = Math.Round(CurrentPrice - (spread / 2m), 2);
        var ask = Math.Round(CurrentPrice + (spread / 2m), 2);

        var timestampUtc = Timestamp > 0
            ? DateTimeOffset.FromUnixTimeSeconds(Timestamp).UtcDateTime
            : DateTime.UtcNow;

        return new StockQuote(
            Symbol: upperSymbol,
            Price: CurrentPrice,
            Bid: bid,
            Ask: ask,
            Volume: 10000,
            Currency: "USD",
            TimestampUtc: timestampUtc,
            IsStale: false,
            SourceProvider: providerName
        );
    }
}
