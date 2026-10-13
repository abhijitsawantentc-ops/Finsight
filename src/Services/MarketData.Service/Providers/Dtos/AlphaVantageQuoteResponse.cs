using System.Globalization;
using System.Text.Json.Serialization;
using Shared.Contracts.Dtos;

namespace MarketData.Service.Providers.Dtos;

/// <summary>
/// Strongly-typed Data Transfer Object representing the raw JSON quote payload from Alpha Vantage API.
/// Endpoint: /query?function=GLOBAL_QUOTE&symbol={symbol}&apikey={apikey}
/// </summary>
public class AlphaVantageQuoteResponse
{
    [JsonPropertyName("Global Quote")]
    public AlphaVantageGlobalQuoteData? GlobalQuote { get; set; }

    [JsonPropertyName("Note")]
    public string? Note { get; set; }

    [JsonPropertyName("Information")]
    public string? Information { get; set; }

    [JsonPropertyName("Error Message")]
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Indicates whether Alpha Vantage responded with a rate limit message.
    /// </summary>
    public bool IsRateLimited() =>
        !string.IsNullOrWhiteSpace(Note) || !string.IsNullOrWhiteSpace(Information);

    /// <summary>
    /// Returns rate limit message if present.
    /// </summary>
    public string? GetRateLimitMessage() =>
        Note ?? Information;

    /// <summary>
    /// Indicates whether a valid Global Quote is present in the payload.
    /// </summary>
    public bool HasValidQuote() =>
        GlobalQuote != null && GlobalQuote.TryGetPrice(out var price) && price > 0;
}

/// <summary>
/// Nested fields inside the Alpha Vantage "Global Quote" object.
/// </summary>
public class AlphaVantageGlobalQuoteData
{
    [JsonPropertyName("01. symbol")]
    public string? Symbol { get; set; }

    [JsonPropertyName("02. open")]
    public string? Open { get; set; }

    [JsonPropertyName("03. high")]
    public string? High { get; set; }

    [JsonPropertyName("04. low")]
    public string? Low { get; set; }

    [JsonPropertyName("05. price")]
    public string? Price { get; set; }

    [JsonPropertyName("06. volume")]
    public string? Volume { get; set; }

    [JsonPropertyName("07. latest trading day")]
    public string? LatestTradingDay { get; set; }

    [JsonPropertyName("08. previous close")]
    public string? PreviousClose { get; set; }

    [JsonPropertyName("09. change")]
    public string? Change { get; set; }

    [JsonPropertyName("10. change percent")]
    public string? ChangePercent { get; set; }

    public bool TryGetPrice(out decimal price) =>
        decimal.TryParse(Price, NumberStyles.Any, CultureInfo.InvariantCulture, out price);

    public long GetVolumeOrDefault(long fallback = 10000L) =>
        long.TryParse(Volume, NumberStyles.Any, CultureInfo.InvariantCulture, out var vol) ? vol : fallback;

    /// <summary>
    /// Maps this nested DTO to the unified <see cref="StockQuote"/> model.
    /// </summary>
    public StockQuote ToStockQuote(string defaultSymbol, string providerName = "AlphaVantage")
    {
        var targetSymbol = (!string.IsNullOrWhiteSpace(Symbol) ? Symbol : defaultSymbol).ToUpperInvariant();
        TryGetPrice(out var price);

        var spread = Math.Max(0.01m, Math.Round(price * 0.0005m, 2));
        var bid = Math.Round(price - (spread / 2m), 2);
        var ask = Math.Round(price + (spread / 2m), 2);

        return new StockQuote(
            Symbol: targetSymbol,
            Price: price,
            Bid: bid,
            Ask: ask,
            Volume: GetVolumeOrDefault(),
            Currency: "USD",
            TimestampUtc: DateTime.UtcNow,
            IsStale: false,
            SourceProvider: providerName
        );
    }
}
