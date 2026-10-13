using System.Net;
using FluentAssertions;
using MarketData.Service.Exceptions;
using MarketData.Service.Providers;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Contracts.Dtos;
using Xunit;

namespace MarketData.Service.Tests;

public class FinnhubMarketDataProviderTests
{
    private readonly NullLogger<FinnhubMarketDataProvider> _logger = NullLogger<FinnhubMarketDataProvider>.Instance;

    [Fact]
    public async Task GetQuoteAsync_ValidResponse_ParsesAndReturnsUnifiedStockQuote()
    {
        // Arrange
        var json = @"{ ""c"": 228.50, ""d"": 1.25, ""dp"": 0.55, ""h"": 229.10, ""l"": 227.30, ""o"": 227.80, ""pc"": 227.25, ""t"": 1728835200 }";
        var mockHandler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json)
        });

        var httpClient = new HttpClient(mockHandler) { BaseAddress = new Uri("https://finnhub.io/api/v1/") };
        var options = new MarketDataProviderOptions
        {
            Finnhub = new FinnhubOptions { ApiKey = "valid-finnhub-key" },
            EnableSandboxFallback = false
        };

        var provider = new FinnhubMarketDataProvider(httpClient, _logger, options);

        // Act
        StockQuote quote = await provider.GetQuoteAsync("AAPL");

        // Assert
        quote.Should().NotBeNull();
        quote.Symbol.Should().Be("AAPL");
        quote.Price.Should().Be(228.50m);
        quote.Bid.Should().BeLessThan(quote.Price);
        quote.Ask.Should().BeGreaterThan(quote.Price);
        quote.Currency.Should().Be("USD");
        quote.IsStale.Should().BeFalse();
        quote.SourceProvider.Should().Be("Finnhub");
        quote.TimestampUtc.Should().Be(DateTimeOffset.FromUnixTimeSeconds(1728835200).UtcDateTime);
    }

    [Fact]
    public async Task GetQuoteAsync_RateLimitExceededHttp429_ThrowsRateLimitExceededException()
    {
        // Arrange
        var mockHandler = new MockHttpMessageHandler(_ =>
        {
            var msg = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            msg.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(60));
            return msg;
        });

        var httpClient = new HttpClient(mockHandler) { BaseAddress = new Uri("https://finnhub.io/api/v1/") };
        var options = new MarketDataProviderOptions
        {
            Finnhub = new FinnhubOptions { ApiKey = "test-key" },
            EnableSandboxFallback = false
        };

        var provider = new FinnhubMarketDataProvider(httpClient, _logger, options);

        // Act
        var act = async () => await provider.GetQuoteAsync("AAPL");

        // Assert
        var ex = await act.Should().ThrowAsync<RateLimitExceededException>();
        ex.Which.Provider.Should().Be("Finnhub");
        ex.Which.RetryAfter.Should().Be(TimeSpan.FromSeconds(60));
    }

    [Fact]
    public async Task GetQuoteAsync_AuthenticationFailed_ThrowsMarketDataAuthenticationException()
    {
        // Arrange
        var mockHandler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            ReasonPhrase = "Unauthorized access - Invalid token"
        });

        var httpClient = new HttpClient(mockHandler) { BaseAddress = new Uri("https://finnhub.io/api/v1/") };
        var options = new MarketDataProviderOptions
        {
            Finnhub = new FinnhubOptions { ApiKey = "invalid-token" },
            EnableSandboxFallback = false
        };

        var provider = new FinnhubMarketDataProvider(httpClient, _logger, options);

        // Act
        var act = async () => await provider.GetQuoteAsync("AAPL");

        // Assert
        var ex = await act.Should().ThrowAsync<MarketDataAuthenticationException>();
        ex.Which.Provider.Should().Be("Finnhub");
    }

    [Fact]
    public async Task GetQuoteAsync_SymbolNotFound_ThrowsSymbolNotFoundException()
    {
        // Arrange
        var mockHandler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var httpClient = new HttpClient(mockHandler) { BaseAddress = new Uri("https://finnhub.io/api/v1/") };
        var options = new MarketDataProviderOptions
        {
            Finnhub = new FinnhubOptions { ApiKey = "test-key" },
            EnableSandboxFallback = false
        };

        var provider = new FinnhubMarketDataProvider(httpClient, _logger, options);

        // Act
        var act = async () => await provider.GetQuoteAsync("UNKNOWN123");

        // Assert
        var ex = await act.Should().ThrowAsync<SymbolNotFoundException>();
        ex.Which.Symbol.Should().Be("UNKNOWN123");
        ex.Which.Provider.Should().Be("Finnhub");
    }

    [Fact]
    public async Task GetQuoteAsync_EmptyApiKey_ReturnsRealisticSandboxQuote_WhenSandboxFallbackEnabled()
    {
        // Arrange
        var mockHandler = new MockHttpMessageHandler(_ => throw new InvalidOperationException("Should not be called"));
        var httpClient = new HttpClient(mockHandler);
        var options = new MarketDataProviderOptions
        {
            Finnhub = new FinnhubOptions { ApiKey = "" },
            EnableSandboxFallback = true
        };

        var provider = new FinnhubMarketDataProvider(httpClient, _logger, options);

        // Act
        var quote = await provider.GetQuoteAsync("AAPL");

        // Assert
        quote.Should().NotBeNull();
        quote.Symbol.Should().Be("AAPL");
        quote.Price.Should().Be(228.50m);
        quote.SourceProvider.Should().Be("Finnhub");
    }

    [Fact]
    public async Task GetQuoteAsync_ZeroPriceData_ThrowsHttpRequestException_WhenSandboxDisabled()
    {
        // Arrange
        var json = @"{ ""c"": 0, ""d"": 0, ""h"": 0, ""l"": 0, ""o"": 0, ""pc"": 0, ""t"": 0 }";
        var mockHandler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json)
        });

        var httpClient = new HttpClient(mockHandler) { BaseAddress = new Uri("https://finnhub.io/api/v1/") };
        var options = new MarketDataProviderOptions
        {
            Finnhub = new FinnhubOptions { ApiKey = "valid-key" },
            EnableSandboxFallback = false
        };

        var provider = new FinnhubMarketDataProvider(httpClient, _logger, options);

        // Act
        var act = async () => await provider.GetQuoteAsync("INVALID");

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>()
            .WithMessage("*invalid price data*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetQuoteAsync_InvalidSymbol_ThrowsArgumentException(string symbol)
    {
        var httpClient = new HttpClient(new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)));
        var provider = new FinnhubMarketDataProvider(httpClient, _logger);

        var act = async () => await provider.GetQuoteAsync(symbol);

        await act.Should().ThrowAsync<ArgumentException>();
    }
}
