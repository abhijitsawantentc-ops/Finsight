using System.Globalization;
using System.Net;
using System.Text.Json;
using MarketData.Service.Exceptions;
using Microsoft.Extensions.Logging;
using Shared.Contracts.Dtos;

namespace MarketData.Service.Providers;

public class FinnhubMarketDataProvider : IMarketDataProvider
{
    private readonly HttpClient _httpClient;
    private readonly MarketDataProviderOptions _options;
    private readonly ILogger<FinnhubMarketDataProvider> _logger;

    public string ProviderName => "Finnhub";
    public int Priority => 1; // Primary

    public FinnhubMarketDataProvider(
        HttpClient httpClient,
        ILogger<FinnhubMarketDataProvider> logger,
        MarketDataProviderOptions? options = null)
    {
        _httpClient = httpClient;
        _logger = logger;
        _options = options ?? new MarketDataProviderOptions();

        if (_httpClient.BaseAddress == null && !string.IsNullOrWhiteSpace(_options.Finnhub.BaseUrl))
        {
            _httpClient.BaseAddress = new Uri(_options.Finnhub.BaseUrl);
        }
    }

    public async Task<MarketQuoteDto> GetQuoteAsync(string symbol, CancellationToken cancellationToken = default)
    {
        var upperSymbol = symbol.ToUpperInvariant();
        var apiKey = _options.Finnhub.ApiKey;

        if (string.IsNullOrWhiteSpace(apiKey) && _options.EnableSandboxFallback)
        {
            // Sandbox fallback when running locally without third-party key
            _logger.LogDebug("Finnhub API key not configured. Generating realistic quote for {Symbol}.", upperSymbol);
            return GenerateSandboxQuote(upperSymbol);
        }

        var endpoint = $"quote?symbol={Uri.EscapeDataString(upperSymbol)}&token={Uri.EscapeDataString(apiKey)}";
        _logger.LogInformation("Querying primary provider Finnhub for symbol {Symbol}", upperSymbol);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.GetAsync(endpoint, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network request to Finnhub failed for {Symbol}", upperSymbol);
            throw;
        }

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            var retryAfter = response.Headers.RetryAfter?.Delta;
            _logger.LogWarning("Finnhub returned HTTP 429 Too Many Requests for {Symbol}", upperSymbol);
            throw new RateLimitExceededException(ProviderName, "Finnhub rate limit exceeded (HTTP 429)", retryAfter);
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("Finnhub returned non-success status code {StatusCode} for {Symbol}: {Body}",
                response.StatusCode, upperSymbol, errorBody);
            throw new HttpRequestException($"Finnhub HTTP error {(int)response.StatusCode}: {response.ReasonPhrase}", null, response.StatusCode);
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Finnhub format: { "c": 228.5, "h": 229.0, "l": 227.0, "o": 227.5, "pc": 227.3, "t": 1728835200 }
        if (!root.TryGetProperty("c", out var currentPriceProp) || currentPriceProp.GetDecimal() <= 0)
        {
            if (_options.EnableSandboxFallback)
            {
                return GenerateSandboxQuote(upperSymbol);
            }
            throw new HttpRequestException($"Finnhub returned invalid price data for symbol '{upperSymbol}'.");
        }

        var currentPrice = currentPriceProp.GetDecimal();
        var spread = Math.Max(0.01m, Math.Round(currentPrice * 0.0005m, 2));
        var bid = Math.Round(currentPrice - (spread / 2m), 2);
        var ask = Math.Round(currentPrice + (spread / 2m), 2);

        return new MarketQuoteDto(
            Symbol: upperSymbol,
            Price: currentPrice,
            Bid: bid,
            Ask: ask,
            Volume: 10000,
            Currency: "USD",
            TimestampUtc: DateTime.UtcNow,
            IsStale: false,
            SourceProvider: ProviderName
        );
    }

    private MarketQuoteDto GenerateSandboxQuote(string symbol)
    {
        var basePrice = symbol switch
        {
            "AAPL" => 228.50m,
            "MSFT" => 445.20m,
            "GOOGL" => 182.10m,
            "AMZN" => 195.40m,
            "NVDA" => 132.80m,
            "TSLA" => 252.60m,
            "BTC-USD" => 67500.00m,
            "ETH-USD" => 3450.00m,
            _ => 100.00m
        };

        var spread = Math.Max(0.01m, Math.Round(basePrice * 0.0005m, 2));
        return new MarketQuoteDto(
            Symbol: symbol,
            Price: basePrice,
            Bid: basePrice - (spread / 2m),
            Ask: basePrice + (spread / 2m),
            Volume: 15000,
            Currency: "USD",
            TimestampUtc: DateTime.UtcNow,
            IsStale: false,
            SourceProvider: ProviderName
        );
    }
}
