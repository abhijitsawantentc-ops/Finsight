using System.Net;
using FluentAssertions;
using MarketData.Service.Exceptions;
using MarketData.Service.Providers;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MarketData.Service.Tests;

public class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

    public MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        _handler = handler;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return Task.FromResult(_handler(request));
    }
}

public class ProviderParsingTests
{
    [Fact]
    public async Task FinnhubProvider_ParsesValidJsonResponse()
    {
        var jsonResponse = @"{ ""c"": 228.50, ""h"": 229.00, ""l"": 227.00, ""o"": 227.50, ""pc"": 227.30, ""t"": 1728835200 }";
        var mockHandler = new MockHttpMessageHandler(req => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonResponse)
        });

        var httpClient = new HttpClient(mockHandler) { BaseAddress = new Uri("https://finnhub.io/api/v1/") };
        var options = new MarketDataProviderOptions
        {
            Finnhub = new FinnhubOptions { ApiKey = "test-key" },
            EnableSandboxFallback = false
        };

        var provider = new FinnhubMarketDataProvider(httpClient, NullLogger<FinnhubMarketDataProvider>.Instance, options);
        var quote = await provider.GetQuoteAsync("AAPL");

        quote.Should().NotBeNull();
        quote.Symbol.Should().Be("AAPL");
        quote.Price.Should().Be(228.50m);
        quote.IsStale.Should().BeFalse();
        quote.SourceProvider.Should().Be("Finnhub");
    }

    [Fact]
    public async Task FinnhubProvider_ThrowsRateLimitExceededException_OnHttp429()
    {
        var mockHandler = new MockHttpMessageHandler(req => new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        var httpClient = new HttpClient(mockHandler) { BaseAddress = new Uri("https://finnhub.io/api/v1/") };
        var options = new MarketDataProviderOptions
        {
            Finnhub = new FinnhubOptions { ApiKey = "test-key" },
            EnableSandboxFallback = false
        };

        var provider = new FinnhubMarketDataProvider(httpClient, NullLogger<FinnhubMarketDataProvider>.Instance, options);
        var act = async () => await provider.GetQuoteAsync("AAPL");

        await act.Should().ThrowAsync<RateLimitExceededException>()
            .WithMessage("*Finnhub rate limit*");
    }

    [Fact]
    public async Task AlphaVantageProvider_ParsesGlobalQuoteResponse()
    {
        var jsonResponse = @"{
            ""Global Quote"": {
                ""01. symbol"": ""MSFT"",
                ""05. price"": ""445.2000"",
                ""06. volume"": ""12345""
            }
        }";
        var mockHandler = new MockHttpMessageHandler(req => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonResponse)
        });

        var httpClient = new HttpClient(mockHandler) { BaseAddress = new Uri("https://www.alphavantage.co/") };
        var options = new MarketDataProviderOptions
        {
            AlphaVantage = new AlphaVantageOptions { ApiKey = "test-key" },
            EnableSandboxFallback = false
        };

        var provider = new AlphaVantageMarketDataProvider(httpClient, NullLogger<AlphaVantageMarketDataProvider>.Instance, options);
        var quote = await provider.GetQuoteAsync("MSFT");

        quote.Should().NotBeNull();
        quote.Symbol.Should().Be("MSFT");
        quote.Price.Should().Be(445.20m);
        quote.Volume.Should().Be(12345);
        quote.SourceProvider.Should().Be("AlphaVantage");
    }

    [Fact]
    public async Task AlphaVantageProvider_DetectsNoteInPayload_AndThrowsRateLimitExceededException()
    {
        var jsonResponse = @"{
            ""Note"": ""Thank you for using Alpha Vantage! Our standard API call frequency is 5 calls per minute...""
        }";
        var mockHandler = new MockHttpMessageHandler(req => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonResponse)
        });

        var httpClient = new HttpClient(mockHandler) { BaseAddress = new Uri("https://www.alphavantage.co/") };
        var options = new MarketDataProviderOptions
        {
            AlphaVantage = new AlphaVantageOptions { ApiKey = "test-key" },
            EnableSandboxFallback = false
        };

        var provider = new AlphaVantageMarketDataProvider(httpClient, NullLogger<AlphaVantageMarketDataProvider>.Instance, options);
        var act = async () => await provider.GetQuoteAsync("MSFT");

        await act.Should().ThrowAsync<RateLimitExceededException>()
            .WithMessage("*Alpha Vantage*");
    }
}
