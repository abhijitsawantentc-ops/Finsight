using System.Net;
using FluentAssertions;
using MarketData.Service.Exceptions;
using MarketData.Service.Providers;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Contracts.Dtos;
using Xunit;

namespace MarketData.Service.Tests;

public class AlphaVantageMarketDataProviderTests
{
    private readonly NullLogger<AlphaVantageMarketDataProvider> _logger = NullLogger<AlphaVantageMarketDataProvider>.Instance;

    [Fact]
    public async Task GetQuoteAsync_ValidGlobalQuote_ParsesAndReturnsUnifiedStockQuote()
    {
        // Arrange
        var json = @"{
            ""Global Quote"": {
                ""01. symbol"": ""MSFT"",
                ""02. open"": ""444.0000"",
                ""03. high"": ""446.5000"",
                ""04. low"": ""443.2000"",
                ""05. price"": ""445.2000"",
                ""06. volume"": ""12345"",
                ""07. latest trading day"": ""2026-10-13"",
                ""08. previous close"": ""442.8000"",
                ""09. change"": ""2.4000"",
                ""10. change percent"": ""0.5428%""
            }
        }";

        var mockHandler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json)
        });

        var httpClient = new HttpClient(mockHandler) { BaseAddress = new Uri("https://www.alphavantage.co/") };
        var options = new MarketDataProviderOptions
        {
            AlphaVantage = new AlphaVantageOptions { ApiKey = "valid-av-key" },
            EnableSandboxFallback = false
        };

        var provider = new AlphaVantageMarketDataProvider(httpClient, _logger, options);

        // Act
        StockQuote quote = await provider.GetQuoteAsync("MSFT");

        // Assert
        quote.Should().NotBeNull();
        quote.Symbol.Should().Be("MSFT");
        quote.Price.Should().Be(445.20m);
        quote.Volume.Should().Be(12345);
        quote.Bid.Should().BeLessThan(quote.Price);
        quote.Ask.Should().BeGreaterThan(quote.Price);
        quote.Currency.Should().Be("USD");
        quote.IsStale.Should().BeFalse();
        quote.SourceProvider.Should().Be("AlphaVantage");
    }

    [Fact]
    public async Task GetQuoteAsync_RateLimitExceededHttp429_ThrowsRateLimitExceededException()
    {
        // Arrange
        var mockHandler = new MockHttpMessageHandler(_ =>
        {
            var msg = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            msg.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(30));
            return msg;
        });

        var httpClient = new HttpClient(mockHandler) { BaseAddress = new Uri("https://www.alphavantage.co/") };
        var options = new MarketDataProviderOptions
        {
            AlphaVantage = new AlphaVantageOptions { ApiKey = "test-key" },
            EnableSandboxFallback = false
        };

        var provider = new AlphaVantageMarketDataProvider(httpClient, _logger, options);

        // Act
        var act = async () => await provider.GetQuoteAsync("MSFT");

        // Assert
        var ex = await act.Should().ThrowAsync<RateLimitExceededException>();
        ex.Which.Provider.Should().Be("AlphaVantage");
        ex.Which.RetryAfter.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task GetQuoteAsync_EmbeddedNoteInPayload_ThrowsRateLimitExceededException()
    {
        // Arrange
        var json = @"{
            ""Note"": ""Thank you for using Alpha Vantage! Our standard API call frequency is 5 calls per minute and 100 calls per day.""
        }";

        var mockHandler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json)
        });

        var httpClient = new HttpClient(mockHandler) { BaseAddress = new Uri("https://www.alphavantage.co/") };
        var options = new MarketDataProviderOptions
        {
            AlphaVantage = new AlphaVantageOptions { ApiKey = "test-key" },
            EnableSandboxFallback = false
        };

        var provider = new AlphaVantageMarketDataProvider(httpClient, _logger, options);

        // Act
        var act = async () => await provider.GetQuoteAsync("MSFT");

        // Assert
        var ex = await act.Should().ThrowAsync<RateLimitExceededException>();
        ex.Which.Provider.Should().Be("AlphaVantage");
        ex.Which.Message.Should().Contain("5 calls per minute");
    }

    [Fact]
    public async Task GetQuoteAsync_EmbeddedInformationInPayload_ThrowsRateLimitExceededException()
    {
        // Arrange
        var json = @"{
            ""Information"": ""You have reached the daily rate limit of 25 requests.""
        }";

        var mockHandler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json)
        });

        var httpClient = new HttpClient(mockHandler) { BaseAddress = new Uri("https://www.alphavantage.co/") };
        var options = new MarketDataProviderOptions
        {
            AlphaVantage = new AlphaVantageOptions { ApiKey = "test-key" },
            EnableSandboxFallback = false
        };

        var provider = new AlphaVantageMarketDataProvider(httpClient, _logger, options);

        // Act
        var act = async () => await provider.GetQuoteAsync("GOOGL");

        // Assert
        var ex = await act.Should().ThrowAsync<RateLimitExceededException>();
        ex.Which.Provider.Should().Be("AlphaVantage");
        ex.Which.Message.Should().Contain("daily rate limit");
    }

    [Fact]
    public async Task GetQuoteAsync_AuthenticationFailed_ThrowsMarketDataAuthenticationException()
    {
        // Arrange
        var mockHandler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            ReasonPhrase = "Forbidden key"
        });

        var httpClient = new HttpClient(mockHandler) { BaseAddress = new Uri("https://www.alphavantage.co/") };
        var options = new MarketDataProviderOptions
        {
            AlphaVantage = new AlphaVantageOptions { ApiKey = "bad-key" },
            EnableSandboxFallback = false
        };

        var provider = new AlphaVantageMarketDataProvider(httpClient, _logger, options);

        // Act
        var act = async () => await provider.GetQuoteAsync("MSFT");

        // Assert
        var ex = await act.Should().ThrowAsync<MarketDataAuthenticationException>();
        ex.Which.Provider.Should().Be("AlphaVantage");
    }

    [Fact]
    public async Task GetQuoteAsync_SymbolNotFound_ThrowsSymbolNotFoundException()
    {
        // Arrange
        var mockHandler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var httpClient = new HttpClient(mockHandler) { BaseAddress = new Uri("https://www.alphavantage.co/") };
        var options = new MarketDataProviderOptions
        {
            AlphaVantage = new AlphaVantageOptions { ApiKey = "test-key" },
            EnableSandboxFallback = false
        };

        var provider = new AlphaVantageMarketDataProvider(httpClient, _logger, options);

        // Act
        var act = async () => await provider.GetQuoteAsync("BADSYM");

        // Assert
        var ex = await act.Should().ThrowAsync<SymbolNotFoundException>();
        ex.Which.Symbol.Should().Be("BADSYM");
        ex.Which.Provider.Should().Be("AlphaVantage");
    }

    [Fact]
    public async Task GetQuoteAsync_EmptyApiKey_ReturnsRealisticSandboxQuote_WhenSandboxFallbackEnabled()
    {
        // Arrange
        var mockHandler = new MockHttpMessageHandler(_ => throw new InvalidOperationException("Should not be called"));
        var httpClient = new HttpClient(mockHandler);
        var options = new MarketDataProviderOptions
        {
            AlphaVantage = new AlphaVantageOptions { ApiKey = "" },
            EnableSandboxFallback = true
        };

        var provider = new AlphaVantageMarketDataProvider(httpClient, _logger, options);

        // Act
        var quote = await provider.GetQuoteAsync("MSFT");

        // Assert
        quote.Should().NotBeNull();
        quote.Symbol.Should().Be("MSFT");
        quote.Price.Should().Be(445.10m);
        quote.SourceProvider.Should().Be("AlphaVantage");
    }

    [Fact]
    public async Task GetQuoteAsync_ErrorMessageInPayload_ThrowsHttpRequestException_WhenSandboxDisabled()
    {
        // Arrange
        var json = @"{ ""Error Message"": ""Invalid API call. Please check your parameter."" }";
        var mockHandler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json)
        });

        var httpClient = new HttpClient(mockHandler) { BaseAddress = new Uri("https://www.alphavantage.co/") };
        var options = new MarketDataProviderOptions
        {
            AlphaVantage = new AlphaVantageOptions { ApiKey = "valid-key" },
            EnableSandboxFallback = false
        };

        var provider = new AlphaVantageMarketDataProvider(httpClient, _logger, options);

        // Act
        var act = async () => await provider.GetQuoteAsync("INVALID");

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>()
            .WithMessage("*Invalid API call*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetQuoteAsync_InvalidSymbol_ThrowsArgumentException(string symbol)
    {
        var httpClient = new HttpClient(new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)));
        var provider = new AlphaVantageMarketDataProvider(httpClient, _logger);

        var act = async () => await provider.GetQuoteAsync(symbol);

        await act.Should().ThrowAsync<ArgumentException>();
    }
}
