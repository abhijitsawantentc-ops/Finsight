using System.Net;
using System.Text.Json;
using MarketData.Service.Exceptions;
using MarketData.Service.Providers.Dtos;
using Microsoft.Extensions.Logging;
using Shared.Contracts.Dtos;

namespace MarketData.Service.Providers;

/// <summary>
/// Market data provider communicating with Alpha Vantage's REST API.
/// Maps responses via <see cref="AlphaVantageQuoteResponse"/> to unified <see cref="StockQuote"/>.
/// </summary>
public class AlphaVantageMarketDataProvider : IMarketDataProvider
{
    private readonly HttpClient _httpClient;
    private readonly MarketDataProviderOptions _options;
    private readonly ILogger<AlphaVantageMarketDataProvider> _logger;

    public string ProviderName => "AlphaVantage";
    public int Priority => 2; // Secondary Failover Provider

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

    public async Task<StockQuote> GetQuoteAsync(string symbol, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            throw new ArgumentException("Ticker symbol cannot be null or empty.", nameof(symbol));
        }

        var upperSymbol = symbol.Trim().ToUpperInvariant();
        var apiKey = _options.AlphaVantage.ApiKey;

        // Graceful sandbox fallback for local development without credentials
        if (string.IsNullOrWhiteSpace(apiKey) && _options.EnableSandboxFallback)
        {
            _logger.LogDebug("AlphaVantage API key not configured. Generating realistic sandbox quote for {Symbol}.", upperSymbol);
            return GenerateSandboxQuote(upperSymbol);
        }

        var endpoint = $"query?function=GLOBAL_QUOTE&symbol={Uri.EscapeDataString(upperSymbol)}&apikey={Uri.EscapeDataString(apiKey)}";
        _logger.LogInformation("Querying secondary market data provider AlphaVantage for symbol {Symbol}", upperSymbol);

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

        // Provider-specific HTTP Error Handling (Step 3.1)
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            var retryAfter = response.Headers.RetryAfter?.Delta;
            _logger.LogWarning("AlphaVantage returned HTTP 429 Too Many Requests for {Symbol}. Retry-After: {RetryAfter}",
                upperSymbol, retryAfter);
            throw new RateLimitExceededException(ProviderName, "AlphaVantage rate limit exceeded (HTTP 429)", retryAfter);
        }

        if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
        {
            _logger.LogError("AlphaVantage rejected authentication ({StatusCode}) for {Symbol}. Verify configured API key.",
                response.StatusCode, upperSymbol);
            throw new MarketDataAuthenticationException(ProviderName, $"AlphaVantage authentication failed: {response.ReasonPhrase}");
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogWarning("AlphaVantage reported symbol {Symbol} not found (HTTP 404)", upperSymbol);
            throw new SymbolNotFoundException(ProviderName, upperSymbol);
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("AlphaVantage returned non-success status code {StatusCode} for {Symbol}: {Body}",
                response.StatusCode, upperSymbol, errorBody);
            throw new HttpRequestException($"AlphaVantage HTTP error {(int)response.StatusCode}: {response.ReasonPhrase}", null, response.StatusCode);
        }

        // Response Parsing & DTO Mapping (Step 3.2)
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        AlphaVantageQuoteResponse? responseDto;
        try
        {
            responseDto = JsonSerializer.Deserialize<AlphaVantageQuoteResponse>(json);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to deserialize AlphaVantage JSON response for {Symbol}: {Json}", upperSymbol, json);
            if (_options.EnableSandboxFallback)
            {
                return GenerateSandboxQuote(upperSymbol);
            }
            throw new HttpRequestException($"AlphaVantage returned malformed JSON for symbol '{upperSymbol}'.", ex);
        }

        if (responseDto == null)
        {
            if (_options.EnableSandboxFallback)
            {
                return GenerateSandboxQuote(upperSymbol);
            }
            throw new HttpRequestException($"AlphaVantage returned an empty response for symbol '{upperSymbol}'.");
        }

        // Alpha Vantage signals frequency limits via Note/Information in HTTP 200 payload
        if (responseDto.IsRateLimited())
        {
            var rateLimitMsg = responseDto.GetRateLimitMessage() ?? "Alpha Vantage standard rate limit reached";
            _logger.LogWarning("AlphaVantage rate limit detected in payload for {Symbol}: {Message}", upperSymbol, rateLimitMsg);
            throw new RateLimitExceededException(ProviderName, rateLimitMsg);
        }

        if (!string.IsNullOrWhiteSpace(responseDto.ErrorMessage))
        {
            _logger.LogWarning("AlphaVantage returned error message for {Symbol}: {Error}", upperSymbol, responseDto.ErrorMessage);
            if (_options.EnableSandboxFallback)
            {
                return GenerateSandboxQuote(upperSymbol);
            }
            throw new HttpRequestException($"AlphaVantage API Error: {responseDto.ErrorMessage}");
        }

        if (!responseDto.HasValidQuote())
        {
            if (_options.EnableSandboxFallback)
            {
                _logger.LogWarning("AlphaVantage returned no valid quote data for {Symbol}. Using sandbox fallback.", upperSymbol);
                return GenerateSandboxQuote(upperSymbol);
            }
            throw new HttpRequestException($"AlphaVantage returned no valid quote data for symbol '{upperSymbol}'.");
        }

        return responseDto.GlobalQuote!.ToStockQuote(upperSymbol, ProviderName);
    }

    private StockQuote GenerateSandboxQuote(string symbol)
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
        return new StockQuote(
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
