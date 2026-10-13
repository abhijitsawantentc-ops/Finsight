using Polly;
using Polly.CircuitBreaker;

namespace MarketData.Service.Resilience.Polly;

public interface IPollyResilienceRegistry
{
    /// <summary>
    /// Gets or creates the resilience pipeline for the specified provider (including timeout, retry with backoff, and circuit breaker).
    /// </summary>
    ResiliencePipeline GetPipeline(string providerName);

    /// <summary>
    /// Returns current state of the circuit breaker for the specified provider.
    /// </summary>
    CircuitState GetCircuitState(string providerName);

    /// <summary>
    /// Returns the manual circuit breaker control for testing or operations.
    /// </summary>
    CircuitBreakerManualControl? GetManualControl(string providerName);
}
