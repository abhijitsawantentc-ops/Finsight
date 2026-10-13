using System.Threading.RateLimiting;

namespace MarketData.Service.Resilience.RateLimiting;

public interface IProviderRateLimiter : IAsyncDisposable, IDisposable
{
    /// <summary>
    /// Attempts to acquire a rate limit lease for the specified provider.
    /// </summary>
    ValueTask<RateLimitLease> AcquireLeaseAsync(string providerName, int permitCount = 1, CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes the callback within the rate limit lease. Throws RateLimitExceededException if rate limit is exhausted.
    /// </summary>
    Task<T> ExecuteWithRateLimitAsync<T>(string providerName, Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a permit is available immediately without consuming it (or probing capacity).
    /// </summary>
    bool CanAcquire(string providerName);
}
