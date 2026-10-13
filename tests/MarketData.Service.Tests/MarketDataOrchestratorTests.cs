using System.Net;
using FluentAssertions;
using MarketData.Service.Exceptions;
using MarketData.Service.Providers;
using MarketData.Service.Resilience.Polly;
using MarketData.Service.Resilience.RateLimiting;
using MarketData.Service.Services.Caching;
using MarketData.Service.Services.Orchestration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Polly.CircuitBreaker;
using Shared.Contracts.Dtos;
using Xunit;

namespace MarketData.Service.Tests;

public class MarketDataOrchestratorTests
{
    private readonly CacheService _cacheService;
    private readonly Mock<IMarketDataProvider> _primaryProviderMock;
    private readonly Mock<IMarketDataProvider> _secondaryProviderMock;
    private readonly IProviderRateLimiter _rateLimiter;
    private readonly IPollyResilienceRegistry _resilienceRegistry;

    public MarketDataOrchestratorTests()
    {
        _cacheService = new CacheService(
            NullLogger<CacheService>.Instance,
            null,
            new CacheServiceOptions
            {
                HotCacheTtl = TimeSpan.FromSeconds(60),
                StaleCacheTtl = TimeSpan.FromHours(24)
            });

        _primaryProviderMock = new Mock<IMarketDataProvider>();
        _primaryProviderMock.SetupGet(p => p.ProviderName).Returns("Finnhub");
        _primaryProviderMock.SetupGet(p => p.Priority).Returns(1);

        _secondaryProviderMock = new Mock<IMarketDataProvider>();
        _secondaryProviderMock.SetupGet(p => p.ProviderName).Returns("AlphaVantage");
        _secondaryProviderMock.SetupGet(p => p.Priority).Returns(2);

        var rateLimiterOptions = new MarketDataRateLimiterOptions
        {
            Finnhub = new ProviderRateLimitOptions { PermitLimit = 100, Window = TimeSpan.FromMinutes(1) },
            AlphaVantage = new ProviderRateLimitOptions { PermitLimit = 100, Window = TimeSpan.FromMinutes(1) }
        };
        _rateLimiter = new MarketDataRateLimiter(NullLogger<MarketDataRateLimiter>.Instance, rateLimiterOptions);

        var resilienceOptions = new PollyResilienceOptions
        {
            Finnhub = new ProviderResilienceConfig
            {
                MaxRetryAttempts = 1,
                InitialRetryDelay = TimeSpan.FromMilliseconds(10),
                Timeout = TimeSpan.FromSeconds(2),
                FailureRatio = 0.5,
                MinimumThroughput = 2,
                BreakDuration = TimeSpan.FromSeconds(5)
            },
            AlphaVantage = new ProviderResilienceConfig
            {
                MaxRetryAttempts = 1,
                InitialRetryDelay = TimeSpan.FromMilliseconds(10),
                Timeout = TimeSpan.FromSeconds(2),
                FailureRatio = 0.5,
                MinimumThroughput = 2,
                BreakDuration = TimeSpan.FromSeconds(5)
            }
        };
        _resilienceRegistry = new PollyResilienceRegistry(NullLogger<PollyResilienceRegistry>.Instance, resilienceOptions);
    }

    private MarketDataOrchestrator CreateOrchestrator(IProviderRateLimiter? rateLimiter = null)
    {
        var providers = new List<IMarketDataProvider>
        {
            _primaryProviderMock.Object,
            _secondaryProviderMock.Object
        };

        return new MarketDataOrchestrator(
            _cacheService,
            rateLimiter ?? _rateLimiter,
            _resilienceRegistry,
            providers,
            NullLogger<MarketDataOrchestrator>.Instance);
    }

    [Fact]
    public async Task Scenario1_CacheHit_ReturnsHotQuoteImmediately_WithoutCallingProviders()
    {
        // Arrange
        var orchestrator = CreateOrchestrator();
        var initialQuote = new MarketQuoteDto("AAPL", 228.50m, 228.40m, 228.60m, 50000, "USD", DateTime.UtcNow);
        await _cacheService.SetQuoteAsync(initialQuote);

        // Act
        var result = await orchestrator.GetQuoteAsync("AAPL");

        // Assert
        result.Should().NotBeNull();
        result.Symbol.Should().Be("AAPL");
        result.Price.Should().Be(228.50m);
        result.IsStale.Should().BeFalse();

        _primaryProviderMock.Verify(p => p.GetQuoteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _secondaryProviderMock.Verify(p => p.GetQuoteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Scenario2_CacheMiss_CallsPrimaryProvider_AndCachesResult()
    {
        // Arrange
        var orchestrator = CreateOrchestrator();
        var primaryQuote = new MarketQuoteDto("MSFT", 445.00m, 444.80m, 445.20m, 30000, "USD", DateTime.UtcNow);

        _primaryProviderMock
            .Setup(p => p.GetQuoteAsync("MSFT", It.IsAny<CancellationToken>()))
            .ReturnsAsync(primaryQuote);

        // Act
        var result = await orchestrator.GetQuoteAsync("MSFT");

        // Assert
        result.Should().NotBeNull();
        result.Symbol.Should().Be("MSFT");
        result.Price.Should().Be(445.00m);
        result.IsStale.Should().BeFalse();
        result.SourceProvider.Should().Be("Finnhub");

        _primaryProviderMock.Verify(p => p.GetQuoteAsync("MSFT", It.IsAny<CancellationToken>()), Times.Once);
        _secondaryProviderMock.Verify(p => p.GetQuoteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

        // Verify it was cached in Hot Cache
        var cachedHot = await _cacheService.GetHotQuoteAsync("MSFT");
        cachedHot.Should().NotBeNull();
        cachedHot!.Price.Should().Be(445.00m);
    }

    [Fact]
    public async Task Scenario3_RateLimit429_FailsOverToSecondaryProvider()
    {
        // Arrange
        var orchestrator = CreateOrchestrator();

        // Primary throws RateLimitExceededException (e.g. HTTP 429)
        _primaryProviderMock
            .Setup(p => p.GetQuoteAsync("GOOGL", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RateLimitExceededException("Finnhub", "Rate limit exceeded (HTTP 429)"));

        // Secondary provider succeeds
        var secondaryQuote = new MarketQuoteDto("GOOGL", 182.50m, 182.40m, 182.60m, 20000, "USD", DateTime.UtcNow);
        _secondaryProviderMock
            .Setup(p => p.GetQuoteAsync("GOOGL", It.IsAny<CancellationToken>()))
            .ReturnsAsync(secondaryQuote);

        // Act
        var result = await orchestrator.GetQuoteAsync("GOOGL");

        // Assert
        result.Should().NotBeNull();
        result.Symbol.Should().Be("GOOGL");
        result.Price.Should().Be(182.50m);
        result.IsStale.Should().BeFalse();
        result.SourceProvider.Should().Be("AlphaVantage");

        _primaryProviderMock.Verify(p => p.GetQuoteAsync("GOOGL", It.IsAny<CancellationToken>()), Times.Once);
        _secondaryProviderMock.Verify(p => p.GetQuoteAsync("GOOGL", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Scenario4_Timeout_FailsOverToSecondaryProvider()
    {
        // Arrange
        var orchestrator = CreateOrchestrator();

        // Primary hangs and throws TaskCanceledException / TimeoutException
        _primaryProviderMock
            .Setup(p => p.GetQuoteAsync("AMZN", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("Primary provider timed out"));

        var secondaryQuote = new MarketQuoteDto("AMZN", 195.00m, 194.90m, 195.10m, 25000, "USD", DateTime.UtcNow);
        _secondaryProviderMock
            .Setup(p => p.GetQuoteAsync("AMZN", It.IsAny<CancellationToken>()))
            .ReturnsAsync(secondaryQuote);

        // Act
        var result = await orchestrator.GetQuoteAsync("AMZN");

        // Assert
        result.Should().NotBeNull();
        result.Symbol.Should().Be("AMZN");
        result.Price.Should().Be(195.00m);
        result.SourceProvider.Should().Be("AlphaVantage");

        _secondaryProviderMock.Verify(p => p.GetQuoteAsync("AMZN", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Scenario5_5xxServerError_RetriesAndFailsOverToSecondaryProvider()
    {
        // Arrange
        var orchestrator = CreateOrchestrator();

        _primaryProviderMock
            .Setup(p => p.GetQuoteAsync("TSLA", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Server Down 500", null, HttpStatusCode.InternalServerError));

        var secondaryQuote = new MarketQuoteDto("TSLA", 252.00m, 251.80m, 252.20m, 40000, "USD", DateTime.UtcNow);
        _secondaryProviderMock
            .Setup(p => p.GetQuoteAsync("TSLA", It.IsAny<CancellationToken>()))
            .ReturnsAsync(secondaryQuote);

        // Act
        var result = await orchestrator.GetQuoteAsync("TSLA");

        // Assert
        result.Should().NotBeNull();
        result.Price.Should().Be(252.00m);
        result.SourceProvider.Should().Be("AlphaVantage");

        // Primary retried (attempt 1 + 1 retry = 2 calls)
        _primaryProviderMock.Verify(p => p.GetQuoteAsync("TSLA", It.IsAny<CancellationToken>()), Times.Exactly(2));
        _secondaryProviderMock.Verify(p => p.GetQuoteAsync("TSLA", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Scenario6_CircuitBreaker_TripsAndFailsOverToSecondary()
    {
        // Arrange
        var orchestrator = CreateOrchestrator();

        _primaryProviderMock
            .Setup(p => p.GetQuoteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Fatal 503", null, HttpStatusCode.ServiceUnavailable));

        _secondaryProviderMock
            .Setup(p => p.GetQuoteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string sym, CancellationToken ct) =>
                new MarketQuoteDto(sym, 67000m, 66990m, 67010m, 100, "USD", DateTime.UtcNow));

        // Act - Call multiple times to trip primary circuit breaker
        for (var i = 0; i < 4; i++)
        {
            var result = await orchestrator.GetQuoteAsync($"BTC-USD-{i}");
            result.SourceProvider.Should().Be("AlphaVantage");
        }

        // Assert: All calls succeeded by failing over to secondary
        _secondaryProviderMock.Verify(p => p.GetQuoteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(4));
    }

    [Fact]
    public async Task Scenario7_StaleFallback_ReturnsStaleQuote_WhenAllProvidersFail()
    {
        // Arrange: Pre-populate cache with an entry that will serve as stale fallback
        var historicalQuote = new MarketQuoteDto("NVDA", 132.50m, 132.40m, 132.60m, 50000, "USD", DateTime.UtcNow.AddMinutes(-5));
        await _cacheService.SetQuoteAsync(historicalQuote);

        // Create orchestrator with expired hot cache scenario
        // Clear hot entry to simulate hot cache miss, but keep stale entry intact
        var fastCache = new CacheService(
            NullLogger<CacheService>.Instance,
            null,
            new CacheServiceOptions
            {
                HotCacheTtl = TimeSpan.FromMilliseconds(50),
                StaleCacheTtl = TimeSpan.FromHours(24)
            });

        await fastCache.SetQuoteAsync(historicalQuote);
        await Task.Delay(100); // hot expired!

        var providers = new List<IMarketDataProvider>
        {
            _primaryProviderMock.Object,
            _secondaryProviderMock.Object
        };

        var orchestrator = new MarketDataOrchestrator(
            fastCache,
            _rateLimiter,
            _resilienceRegistry,
            providers,
            NullLogger<MarketDataOrchestrator>.Instance);

        // Both providers fail
        _primaryProviderMock
            .Setup(p => p.GetQuoteAsync("NVDA", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Finnhub down"));

        _secondaryProviderMock
            .Setup(p => p.GetQuoteAsync("NVDA", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("AlphaVantage down"));

        // Act
        var result = await orchestrator.GetQuoteAsync("NVDA");

        // Assert
        result.Should().NotBeNull();
        result.Symbol.Should().Be("NVDA");
        result.Price.Should().Be(132.50m);
        result.IsStale.Should().BeTrue("both providers failed, so stale cache was returned");
        result.SourceProvider.Should().Be("StaleCacheFallback");
    }

    [Fact]
    public async Task Scenario8_ExhaustedFallback_ThrowsMarketDataUnavailableException_WhenNoStaleCacheExists()
    {
        // Arrange
        var orchestrator = CreateOrchestrator();

        _primaryProviderMock
            .Setup(p => p.GetQuoteAsync("UNKNOWN", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Finnhub 500", null, HttpStatusCode.InternalServerError));

        _secondaryProviderMock
            .Setup(p => p.GetQuoteAsync("UNKNOWN", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RateLimitExceededException("AlphaVantage", "Exceeded calls"));

        // Act
        var act = async () => await orchestrator.GetQuoteAsync("UNKNOWN");

        // Assert
        var ex = await act.Should().ThrowAsync<MarketDataUnavailableException>();
        ex.Which.Symbol.Should().Be("UNKNOWN");
        ex.Which.ProviderErrors.Should().HaveCount(2);
        ex.Which.ProviderErrors[0].Should().Contain("Finnhub");
        ex.Which.ProviderErrors[1].Should().Contain("AlphaVantage");
    }
}
