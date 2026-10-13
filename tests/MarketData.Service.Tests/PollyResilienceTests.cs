using System.Net;
using FluentAssertions;
using MarketData.Service.Exceptions;
using MarketData.Service.Resilience.Polly;
using Microsoft.Extensions.Logging.Abstractions;
using Polly.CircuitBreaker;
using Polly.Timeout;
using Xunit;

namespace MarketData.Service.Tests;

public class PollyResilienceTests
{
    [Fact]
    public async Task RetryStrategy_RetriesOn5xxError_AndSucceedsOnSubsequentAttempt()
    {
        var options = new PollyResilienceOptions
        {
            Finnhub = new ProviderResilienceConfig
            {
                MaxRetryAttempts = 3,
                InitialRetryDelay = TimeSpan.FromMilliseconds(50),
                Timeout = TimeSpan.FromSeconds(2),
                FailureRatio = 0.9,
                MinimumThroughput = 10
            }
        };

        var registry = new PollyResilienceRegistry(NullLogger<PollyResilienceRegistry>.Instance, options);
        var pipeline = registry.GetPipeline("Finnhub");

        var attempts = 0;
        var result = await pipeline.ExecuteAsync(async ct =>
        {
            attempts++;
            if (attempts < 3)
            {
                throw new HttpRequestException("Simulated 503 Service Unavailable", null, HttpStatusCode.ServiceUnavailable);
            }
            return await Task.FromResult("Success");
        });

        result.Should().Be("Success");
        attempts.Should().Be(3);
    }

    [Fact]
    public async Task RetryStrategy_DoesNotRetry_OnRateLimitExceededException()
    {
        var options = new PollyResilienceOptions
        {
            Finnhub = new ProviderResilienceConfig
            {
                MaxRetryAttempts = 3,
                InitialRetryDelay = TimeSpan.FromMilliseconds(50),
                Timeout = TimeSpan.FromSeconds(2)
            }
        };

        var registry = new PollyResilienceRegistry(NullLogger<PollyResilienceRegistry>.Instance, options);
        var pipeline = registry.GetPipeline("Finnhub");

        var attempts = 0;
        var act = async () => await pipeline.ExecuteAsync(async ct =>
        {
            attempts++;
            throw new RateLimitExceededException("Finnhub", "Rate limit reached");
#pragma warning disable CS0162 // Unreachable code detected
            return await Task.FromResult("Success");
#pragma warning restore CS0162 // Unreachable code detected
        });

        await act.Should().ThrowAsync<RateLimitExceededException>();
        attempts.Should().Be(1, "Rate limiting should fail fast without retrying the exhausted provider");
    }

    [Fact]
    public async Task TimeoutStrategy_ThrowsTimeoutRejectedException_WhenExecutionExceedsThreshold()
    {
        var options = new PollyResilienceOptions
        {
            Finnhub = new ProviderResilienceConfig
            {
                MaxRetryAttempts = 0,
                Timeout = TimeSpan.FromMilliseconds(100)
            }
        };

        var registry = new PollyResilienceRegistry(NullLogger<PollyResilienceRegistry>.Instance, options);
        var pipeline = registry.GetPipeline("Finnhub");

        var act = async () => await pipeline.ExecuteAsync(async ct =>
        {
            await Task.Delay(300, ct);
            return "Too late";
        });

        await act.Should().ThrowAsync<TimeoutRejectedException>();
    }

    [Fact]
    public async Task CircuitBreakerStrategy_TripsCircuit_AfterThresholdFailures()
    {
        var options = new PollyResilienceOptions
        {
            Finnhub = new ProviderResilienceConfig
            {
                MaxRetryAttempts = 0, // no retries, test circuit breaker directly
                Timeout = TimeSpan.FromSeconds(2),
                FailureRatio = 0.5,
                MinimumThroughput = 3,
                SamplingDuration = TimeSpan.FromSeconds(10),
                BreakDuration = TimeSpan.FromSeconds(5)
            }
        };

        var registry = new PollyResilienceRegistry(NullLogger<PollyResilienceRegistry>.Instance, options);
        var pipeline = registry.GetPipeline("Finnhub");

        // Fail 3 times to trip breaker
        for (var i = 0; i < 3; i++)
        {
            try
            {
                await pipeline.ExecuteAsync(ct =>
                    throw new HttpRequestException("Server Down 500", null, HttpStatusCode.InternalServerError));
            }
            catch (HttpRequestException)
            {
                // Expected
            }
        }

        // 4th call should immediately fail with BrokenCircuitException without calling provider
        var providerCalled = false;
        var act = async () => await pipeline.ExecuteAsync(ct =>
        {
            providerCalled = true;
            return ValueTask.FromResult("OK");
        });

        await act.Should().ThrowAsync<BrokenCircuitException>();
        providerCalled.Should().BeFalse("circuit breaker is open, should reject immediately");
    }
}
