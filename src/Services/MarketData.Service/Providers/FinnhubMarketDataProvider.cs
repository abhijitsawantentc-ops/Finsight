using System.Net;
using System.Text.Json;
using MarketData.Service.Exceptions;
using MarketData.Service.Providers.Dtos;
using Microsoft.Extensions.Logging;
using Shared.Contracts.Dtos;

namespace MarketData.Service.Providers;

/// <summary>
/// Market data provider communicating with Finnhub's REST API.
/// Maps responses via <see cref="FinnhubQuoteResponse"/> to unified <see cref="StockQuote"/>.
/// </summary>
public class FinnhubMarketDataProvider : IMarketDataProvider
{
    private readonly HttpClient _httpClient;
    private readonly MarketDataProviderOptions _options;
    private readonly ILogger<FinnhubMarketDataProvider> _logger;

    public string ProviderName => "Finnhub";
    public int Priority => 1; // Primary Provider

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

    public async Task<StockQuote> GetQuoteAsync(string symbol, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            throw new ArgumentException("Ticker symbol cannot be null or empty.", nameof(symbol));
        }

        var upperSymbol = symbol.Trim().ToUpperInvariant();
        var apiKey = _options.Finnhub.ApiKey;

        // Graceful sandbox fallback for local development without credentials
        if (string.IsNullOrWhiteSpace(apiKey) && _options.EnableSandboxFallback)
        {
            _logger.LogDebug("Finnhub API key not configured. Generating realistic sandbox quote for {Symbol}.", upperSymbol);
            return GenerateSandboxQuote(upperSymbol);
        }

        var endpoint = $"quote?symbol={Uri.EscapeDataString(upperSymbol)}&token={Uri.EscapeDataString(apiKey)}";
        _logger.LogInformation("Querying primary market data provider Finnhub for symbol {Symbol}", upperSymbol);

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

        // Provider-specific HTTP Error Handling (Step 3.1)
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            var retryAfter = response.Headers.RetryAfter?.Delta;
            _logger.LogWarning("Finnhub returned HTTP 429 Too Many Requests for {Symbol}. Retry-After: {RetryAfter}",
                upperSymbol, retryAfter);
            throw new RateLimitExceededException(ProviderName, "Finnhub rate limit exceeded (HTTP 429)", retryAfter);
        }

        if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
        {
            _logger.LogError("Finnhub rejected authentication ({StatusCode}) for {Symbol}. Verify configured API key.",
                response.StatusCode, upperSymbol);
            throw new MarketDataAuthenticationException(ProviderName, $"Finnhub authentication failed: {response.ReasonPhrase}");
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogWarning("Finnhub reported symbol {Symbol} not found (HTTP 404)", upperSymbol);
            throw new SymbolNotFoundException(ProviderName, upperSymbol);
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("Finnhub returned non-success status code {StatusCode} for {Symbol}: {Body}",
                response.StatusCode, upperSymbol, errorBody);
            throw new HttpRequestException($"Finnhub HTTP error {(int)response.StatusCode}: {response.ReasonPhrase}", null, response.StatusCode);
        }

        // Response Parsing & DTO Mapping (Step 3.2)
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        FinnhubQuoteResponse? quoteDto;
        try
        {
            quoteDto = JsonSerializer.Deserialize<FinnhubQuoteResponse>(json);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to deserialize Finnhub JSON response for {Symbol}: {Json}", upperSymbol, json);
            if (_options.EnableSandboxFallback)
            {
                return GenerateSandboxQuote(upperSymbol);
            }
            throw new HttpRequestException($"Finnhub returned malformed JSON for symbol '{upperSymbol}'.", ex);
        }

        if (quoteDto == null || !quoteDto.IsValid())
        {
            if (_options.EnableSandboxFallback)
            {
                _logger.LogWarning("Finnhub returned zero or invalid price data for {Symbol}. Using sandbox fallback.", upperSymbol);
                return GenerateSandboxQuote(upperSymbol);
            }

            throw new HttpRequestException($"Finnhub returned invalid price data for symbol '{upperSymbol}'.");
        }

        return quoteDto.ToStockQuote(upperSymbol, ProviderName);
    }

    private StockQuote GenerateSandboxQuote(string symbol)
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
        return new StockQuote(
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
