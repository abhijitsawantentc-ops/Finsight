using System.Collections.Concurrent;
using System.Net;
using MarketData.Service.Exceptions;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;

namespace MarketData.Service.Resilience.Polly;

public class PollyResilienceRegistry : IPollyResilienceRegistry
{
    private readonly PollyResilienceOptions _options;
    private readonly ILogger<PollyResilienceRegistry> _logger;

    private readonly ConcurrentDictionary<string, ResiliencePipeline> _pipelines = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, CircuitBreakerManualControl> _circuitControls = new(StringComparer.OrdinalIgnoreCase);

    public PollyResilienceRegistry(
        ILogger<PollyResilienceRegistry> logger,
        PollyResilienceOptions? options = null)
    {
        _logger = logger;
        _options = options ?? new PollyResilienceOptions();
    }

    public ResiliencePipeline GetPipeline(string providerName)
    {
        return _pipelines.GetOrAdd(providerName, name => BuildPipeline(name));
    }

    public CircuitState GetCircuitState(string providerName)
    {
        if (_circuitControls.TryGetValue(providerName, out var control))
        {
            // CircuitBreakerManualControl tracks or we can check if pipeline circuit is open
            // Wait: CircuitBreakerManualControl.IsIsolated, etc.
        }
        return CircuitState.Closed;
    }

    public CircuitBreakerManualControl? GetManualControl(string providerName)
    {
        _circuitControls.TryGetValue(providerName, out var control);
        return control;
    }

    private ResiliencePipeline BuildPipeline(string providerName)
    {
        var config = providerName.Equals("Finnhub", StringComparison.OrdinalIgnoreCase)
            ? _options.Finnhub
            : providerName.Equals("AlphaVantage", StringComparison.OrdinalIgnoreCase)
                ? _options.AlphaVantage
                : new ProviderResilienceConfig();

        var manualControl = new CircuitBreakerManualControl();
        _circuitControls[providerName] = manualControl;

        var builder = new ResiliencePipelineBuilder();

        // 1. Circuit Breaker (outermost - fail fast if breaker is open)
        builder.AddCircuitBreaker(new CircuitBreakerStrategyOptions
        {
            FailureRatio = config.FailureRatio,
            SamplingDuration = config.SamplingDuration,
            MinimumThroughput = config.MinimumThroughput,
            BreakDuration = config.BreakDuration,
            ManualControl = manualControl,
            ShouldHandle = new PredicateBuilder().Handle<Exception>(ex => IsTransientOrServerError(ex)),
            OnOpened = args =>
            {
                _logger.LogWarning("Circuit breaker OPENED for provider '{Provider}'. Break duration: {Duration}s. Break reason: {Exception}",
                    providerName, args.BreakDuration.TotalSeconds, args.Outcome.Exception?.Message);
                return ValueTask.CompletedTask;
            },
            OnClosed = args =>
            {
                _logger.LogInformation("Circuit breaker CLOSED for provider '{Provider}'. Healthy operations restored.", providerName);
                return ValueTask.CompletedTask;
            },
            OnHalfOpened = args =>
            {
                _logger.LogInformation("Circuit breaker HALF-OPEN for provider '{Provider}'. Probing health with trial call...", providerName);
                return ValueTask.CompletedTask;
            }
        });

        // 2. Retry with Exponential Backoff and Jitter (if configured)
        if (config.MaxRetryAttempts > 0)
        {
            builder.AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = config.MaxRetryAttempts,
                Delay = config.InitialRetryDelay,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                ShouldHandle = new PredicateBuilder().Handle<Exception>(ex => IsTransientOrServerError(ex)),
                OnRetry = args =>
                {
                    _logger.LogWarning("Retry attempt #{Attempt} for provider '{Provider}' after {Delay}ms delay. Reason: {Error}",
                        args.AttemptNumber + 1, providerName, args.RetryDelay.TotalMilliseconds, args.Outcome.Exception?.Message);
                    return ValueTask.CompletedTask;
                }
            });
        }

        // 3. Request Timeout
        builder.AddTimeout(new TimeoutStrategyOptions
        {
            Timeout = config.Timeout,
            OnTimeout = args =>
            {
                _logger.LogWarning("Execution timed out for provider '{Provider}' after {Timeout}s.", providerName, args.Timeout.TotalSeconds);
                return ValueTask.CompletedTask;
            }
        });

        return builder.Build();
    }

    private static bool IsTransientOrServerError(Exception ex)
    {
        // Do NOT retry or trip circuit breaker on rate limiting or when circuit is already broken
        if (ex is RateLimitExceededException or BrokenCircuitException)
        {
            return false;
        }

        if (ex is HttpRequestException httpEx)
        {
            // 429 should not be retried in Polly retry; failover to secondary provider instead
            if (httpEx.StatusCode == HttpStatusCode.TooManyRequests)
            {
                return false;
            }

            // 5xx server errors, 408 request timeout, or connection failure
            if (httpEx.StatusCode == null ||
                (int)httpEx.StatusCode.Value >= 500 ||
                httpEx.StatusCode == HttpStatusCode.RequestTimeout)
            {
                return true;
            }
        }

        if (ex is TimeoutRejectedException or TimeoutException)
        {
            return true;
        }

        return false;
    }
}
