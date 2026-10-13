using System.Collections.Concurrent;
using System.Threading.RateLimiting;
using MarketData.Service.Exceptions;
using Microsoft.Extensions.Logging;

namespace MarketData.Service.Resilience.RateLimiting;

public class MarketDataRateLimiter : IProviderRateLimiter
{
    private readonly ConcurrentDictionary<string, RateLimiter> _limiters = new(StringComparer.OrdinalIgnoreCase);
    private readonly MarketDataRateLimiterOptions _options;
    private readonly ILogger<MarketDataRateLimiter> _logger;
    private bool _disposed;

    public MarketDataRateLimiter(
        ILogger<MarketDataRateLimiter> logger,
        MarketDataRateLimiterOptions? options = null)
    {
        _logger = logger;
        _options = options ?? new MarketDataRateLimiterOptions();

        // Initialize configured limiters
        _limiters["Finnhub"] = CreateLimiter(_options.Finnhub);
        _limiters["AlphaVantage"] = CreateLimiter(_options.AlphaVantage);
    }

    private static RateLimiter CreateLimiter(ProviderRateLimitOptions config)
    {
        return new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
        {
            PermitLimit = config.PermitLimit,
            Window = config.Window,
            QueueLimit = config.QueueLimit,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            AutoReplenishment = true
        });
    }

    private RateLimiter GetOrAddLimiter(string providerName)
    {
        return _limiters.GetOrAdd(providerName, name =>
        {
            _logger.LogInformation("Creating default rate limiter for provider {ProviderName} (30 req/min)", name);
            return CreateLimiter(new ProviderRateLimitOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 });
        });
    }

    public ValueTask<RateLimitLease> AcquireLeaseAsync(string providerName, int permitCount = 1, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var limiter = GetOrAddLimiter(providerName);
        return limiter.AcquireAsync(permitCount, cancellationToken);
    }

    public async Task<T> ExecuteWithRateLimitAsync<T>(string providerName, Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var limiter = GetOrAddLimiter(providerName);

        using var lease = await limiter.AcquireAsync(1, cancellationToken);
        if (!lease.IsAcquired)
        {
            TimeSpan? retryAfter = null;
            if (lease.TryGetMetadata(MetadataName.RetryAfter, out var retry))
            {
                retryAfter = retry;
            }

            _logger.LogWarning("Rate limit exceeded for provider {ProviderName}. Rejecting request to trigger failover.", providerName);
            throw new RateLimitExceededException(providerName, $"Rate limit exceeded for provider '{providerName}'.", retryAfter);
        }

        return await action(cancellationToken);
    }

    public bool CanAcquire(string providerName)
    {
        if (_disposed) return false;
        var limiter = GetOrAddLimiter(providerName);
        var stats = limiter.GetStatistics();
        return stats == null || stats.CurrentAvailablePermits > 0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var limiter in _limiters.Values)
        {
            limiter.Dispose();
        }
        _limiters.Clear();
        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var limiter in _limiters.Values)
        {
            await limiter.DisposeAsync();
        }
        _limiters.Clear();
        GC.SuppressFinalize(this);
    }
}
