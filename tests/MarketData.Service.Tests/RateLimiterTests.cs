using FluentAssertions;
using MarketData.Service.Exceptions;
using MarketData.Service.Resilience.RateLimiting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MarketData.Service.Tests;

public class RateLimiterTests
{
    [Fact]
    public async Task FinnhubRateLimiter_AllowsConfiguredCalls_AndRejectsExcess()
    {
        // Arrange: configured limit of 3 permits per minute
        var options = new MarketDataRateLimiterOptions
        {
            Finnhub = new ProviderRateLimitOptions
            {
                PermitLimit = 3,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }
        };

        using var rateLimiter = new MarketDataRateLimiter(NullLogger<MarketDataRateLimiter>.Instance, options);

        // Act & Assert: first 3 calls should succeed
        for (var i = 0; i < 3; i++)
        {
            var result = await rateLimiter.ExecuteWithRateLimitAsync("Finnhub", ct => Task.FromResult(i));
            result.Should().Be(i);
        }

        // 4th call should immediately throw RateLimitExceededException
        var act = async () => await rateLimiter.ExecuteWithRateLimitAsync("Finnhub", ct => Task.FromResult(99));
        await act.Should().ThrowAsync<RateLimitExceededException>()
            .WithMessage("*Finnhub*");
    }

    [Fact]
    public async Task AlphaVantageRateLimiter_EnforcesDistinctLimits()
    {
        // Arrange
        var options = new MarketDataRateLimiterOptions
        {
            Finnhub = new ProviderRateLimitOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 },
            AlphaVantage = new ProviderRateLimitOptions { PermitLimit = 2, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }
        };

        using var rateLimiter = new MarketDataRateLimiter(NullLogger<MarketDataRateLimiter>.Instance, options);

        // Alpha Vantage has limit of 2
        await rateLimiter.ExecuteWithRateLimitAsync("AlphaVantage", ct => Task.FromResult(1));
        await rateLimiter.ExecuteWithRateLimitAsync("AlphaVantage", ct => Task.FromResult(2));

        var act = async () => await rateLimiter.ExecuteWithRateLimitAsync("AlphaVantage", ct => Task.FromResult(3));
        await act.Should().ThrowAsync<RateLimitExceededException>();

        // Finnhub should still be available and not impacted by Alpha Vantage limit
        var finnhubResult = await rateLimiter.ExecuteWithRateLimitAsync("Finnhub", ct => Task.FromResult("OK"));
        finnhubResult.Should().Be("OK");
    }

    [Fact]
    public async Task CanAcquire_ReflectsCapacityAccurately()
    {
        var options = new MarketDataRateLimiterOptions
        {
            Finnhub = new ProviderRateLimitOptions { PermitLimit = 1, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }
        };

        using var rateLimiter = new MarketDataRateLimiter(NullLogger<MarketDataRateLimiter>.Instance, options);

        rateLimiter.CanAcquire("Finnhub").Should().BeTrue();
        await rateLimiter.ExecuteWithRateLimitAsync("Finnhub", ct => Task.FromResult(1));
        rateLimiter.CanAcquire("Finnhub").Should().BeFalse();
    }
}
