using System.Globalization;
using System.Net;
using System.Text.Json;
using MarketData.Service.Exceptions;
using Microsoft.Extensions.Logging;
using Shared.Contracts.Dtos;

namespace MarketData.Service.Providers;

public class AlphaVantageMarketDataProvider : IMarketDataProvider
{
    private readonly HttpClient _httpClient;
    private readonly MarketDataProviderOptions _options;
    private readonly ILogger<AlphaVantageMarketDataProvider> _logger;

    public string ProviderName => "AlphaVantage";
    public int Priority => 2; // Secondary

    public AlphaVantageMarketDataProvider(
        HttpClient httpClient,
        ILogger<AlphaVantageMarketDataProvider> logger,
        MarketDataProviderOptions? options = null)
    {
        _httpClient = httpClient;
        _logger = logger;
        _options = options ?? new MarketDataProviderOptions();

        if (_httpClient.BaseAddress == null && !string.IsNullOrWhiteSpace(_options.AlphaVantage.BaseUrl))
        {
            _httpClient.BaseAddress = new Uri(_options.AlphaVantage.BaseUrl);
        }
    }

    public async Task<MarketQuoteDto> GetQuoteAsync(string symbol, CancellationToken cancellationToken = default)
    {
        var upperSymbol = symbol.ToUpperInvariant();
        var apiKey = _options.AlphaVantage.ApiKey;

        if (string.IsNullOrWhiteSpace(apiKey) && _options.EnableSandboxFallback)
        {
            _logger.LogDebug("AlphaVantage API key not configured. Generating realistic quote for {Symbol}.", upperSymbol);
            return GenerateSandboxQuote(upperSymbol);
        }

        var endpoint = $"query?function=GLOBAL_QUOTE&symbol={Uri.EscapeDataString(upperSymbol)}&apikey={Uri.EscapeDataString(apiKey)}";
        _logger.LogInformation("Querying secondary provider AlphaVantage for symbol {Symbol}", upperSymbol);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.GetAsync(endpoint, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network request to AlphaVantage failed for {Symbol}", upperSymbol);
            throw;
        }

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            var retryAfter = response.Headers.RetryAfter?.Delta;
            _logger.LogWarning("AlphaVantage returned HTTP 429 Too Many Requests for {Symbol}", upperSymbol);
            throw new RateLimitExceededException(ProviderName, "AlphaVantage rate limit exceeded (HTTP 429)", retryAfter);
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("AlphaVantage returned non-success status code {StatusCode} for {Symbol}: {Body}",
                response.StatusCode, upperSymbol, errorBody);
            throw new HttpRequestException($"AlphaVantage HTTP error {(int)response.StatusCode}: {response.ReasonPhrase}", null, response.StatusCode);
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Alpha Vantage signals rate limits via Note in 200 OK body:
        // { "Note": "Thank you for using Alpha Vantage! Our standard API call frequency is 5 calls per minute..." }
        if (root.TryGetProperty("Note", out var noteProp) || root.TryGetProperty("Information", out _))
        {
            var noteMessage = noteProp.GetString() ?? "AlphaVantage standard rate limit reached";
            _logger.LogWarning("AlphaVantage rate limit detected in payload for {Symbol}: {Note}", upperSymbol, noteMessage);
            throw new RateLimitExceededException(ProviderName, noteMessage);
        }

        // Global Quote payload:
        // { "Global Quote": { "01. symbol": "AAPL", "05. price": "228.5000", "06. volume": "12345" } }
        if (root.TryGetProperty("Global Quote", out var quoteProp) &&
            quoteProp.TryGetProperty("05. price", out var priceElement) &&
            decimal.TryParse(priceElement.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var price) &&
            price > 0)
        {
            var volume = 10000L;
            if (quoteProp.TryGetProperty("06. volume", out var volElement) &&
                long.TryParse(volElement.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedVol))
            {
                volume = parsedVol;
            }

            var spread = Math.Max(0.01m, Math.Round(price * 0.0005m, 2));
            var bid = Math.Round(price - (spread / 2m), 2);
            var ask = Math.Round(price + (spread / 2m), 2);

            return new MarketQuoteDto(
                Symbol: upperSymbol,
                Price: price,
                Bid: bid,
                Ask: ask,
                Volume: volume,
                Currency: "USD",
                TimestampUtc: DateTime.UtcNow,
                IsStale: false,
                SourceProvider: ProviderName
            );
        }

        if (_options.EnableSandboxFallback)
        {
            return GenerateSandboxQuote(upperSymbol);
        }

        throw new HttpRequestException($"AlphaVantage returned no valid quote data for symbol '{upperSymbol}'.");
    }

    private MarketQuoteDto GenerateSandboxQuote(string symbol)
    {
        var basePrice = symbol switch
        {
            "AAPL" => 228.45m,
            "MSFT" => 445.10m,
            "GOOGL" => 182.05m,
            "AMZN" => 195.35m,
            "NVDA" => 132.75m,
            "TSLA" => 252.55m,
            "BTC-USD" => 67490.00m,
            "ETH-USD" => 3448.00m,
            _ => 100.00m
        };

        var spread = Math.Max(0.01m, Math.Round(basePrice * 0.0005m, 2));
        return new MarketQuoteDto(
            Symbol: symbol,
            Price: basePrice,
            Bid: basePrice - (spread / 2m),
            Ask: basePrice + (spread / 2m),
            Volume: 12000,
            Currency: "USD",
            TimestampUtc: DateTime.UtcNow,
            IsStale: false,
            SourceProvider: ProviderName
        );
    }
}
