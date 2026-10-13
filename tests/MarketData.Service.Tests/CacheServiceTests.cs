using FluentAssertions;
using MarketData.Service.Services.Caching;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Contracts.Dtos;
using Xunit;

namespace MarketData.Service.Tests;

public class CacheServiceTests
{
    private readonly CacheService _cacheService;

    public CacheServiceTests()
    {
        var options = new CacheServiceOptions
        {
            HotCacheTtl = TimeSpan.FromMilliseconds(200),
            StaleCacheTtl = TimeSpan.FromMilliseconds(600)
        };

        _cacheService = new CacheService(NullLogger<CacheService>.Instance, null, options);
    }

    [Fact]
    public async Task SetQuoteAsync_StoresHotAndStaleQuotes()
    {
        // Arrange
        var quote = new MarketQuoteDto("AAPL", 225.50m, 225.40m, 225.60m, 10000, "USD", DateTime.UtcNow);

        // Act
        await _cacheService.SetQuoteAsync(quote);

        // Assert - Hot Cache
        var hotQuote = await _cacheService.GetHotQuoteAsync("AAPL");
        hotQuote.Should().NotBeNull();
        hotQuote!.Symbol.Should().Be("AAPL");
        hotQuote.Price.Should().Be(225.50m);
        hotQuote.IsStale.Should().BeFalse();

        // Assert - Stale Cache
        var staleQuote = await _cacheService.GetStaleQuoteAsync("AAPL");
        staleQuote.Should().NotBeNull();
        staleQuote!.Symbol.Should().Be("AAPL");
        staleQuote.IsStale.Should().BeTrue();
    }

    [Fact]
    public async Task GetHotQuoteAsync_ReturnsNull_WhenExpired()
    {
        // Arrange
        var quote = new MarketQuoteDto("MSFT", 440.00m, 439.80m, 440.20m, 5000, "USD", DateTime.UtcNow);
        await _cacheService.SetQuoteAsync(quote);

        // Act - Wait beyond 200ms hot TTL
        await Task.Delay(250);

        var hotQuote = await _cacheService.GetHotQuoteAsync("MSFT");
        var staleQuote = await _cacheService.GetStaleQuoteAsync("MSFT");

        // Assert
        hotQuote.Should().BeNull("hot cache should expire after 200ms");
        staleQuote.Should().NotBeNull("stale cache should still be alive up to 600ms");
        staleQuote!.IsStale.Should().BeTrue();
    }

    [Fact]
    public async Task GetStaleQuoteAsync_ReturnsNull_WhenExpiredBeyondStaleTtl()
    {
        // Arrange
        var quote = new MarketQuoteDto("NVDA", 130.00m, 129.90m, 130.10m, 8000, "USD", DateTime.UtcNow);
        await _cacheService.SetQuoteAsync(quote);

        // Act - Wait beyond 600ms stale TTL
        await Task.Delay(650);

        var hotQuote = await _cacheService.GetHotQuoteAsync("NVDA");
        var staleQuote = await _cacheService.GetStaleQuoteAsync("NVDA");

        // Assert
        hotQuote.Should().BeNull();
        staleQuote.Should().BeNull("stale cache should expire after 600ms");
    }

    [Fact]
    public async Task GetAllSymbolsAsync_ReturnsDistinctAvailableSymbols()
    {
        // Arrange
        await _cacheService.SetQuoteAsync(new MarketQuoteDto("AAPL", 225m, 224m, 226m, 100, "USD", DateTime.UtcNow));
        await _cacheService.SetQuoteAsync(new MarketQuoteDto("GOOGL", 180m, 179m, 181m, 100, "USD", DateTime.UtcNow));

        // Act
        var symbols = await _cacheService.GetAllSymbolsAsync();

        // Assert
        symbols.Should().Contain("AAPL");
        symbols.Should().Contain("GOOGL");
    }
}
