using Polly.CircuitBreaker;

namespace MarketData.Service.Resilience.Polly;

public class ProviderResilienceConfig
{
    public int MaxRetryAttempts { get; set; } = 2;
    public TimeSpan InitialRetryDelay { get; set; } = TimeSpan.FromMilliseconds(300);
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(3);
    public double FailureRatio { get; set; } = 0.5;
    public int MinimumThroughput { get; set; } = 3;
    public TimeSpan SamplingDuration { get; set; } = TimeSpan.FromSeconds(15);
    public TimeSpan BreakDuration { get; set; } = TimeSpan.FromSeconds(20);
}

public class PollyResilienceOptions
{
    public ProviderResilienceConfig Finnhub { get; set; } = new()
    {
        MaxRetryAttempts = 2,
        InitialRetryDelay = TimeSpan.FromMilliseconds(300),
        Timeout = TimeSpan.FromSeconds(3),
        FailureRatio = 0.5,
        MinimumThroughput = 3,
        SamplingDuration = TimeSpan.FromSeconds(15),
        BreakDuration = TimeSpan.FromSeconds(20)
    };

    public ProviderResilienceConfig AlphaVantage { get; set; } = new()
    {
        MaxRetryAttempts = 2,
        InitialRetryDelay = TimeSpan.FromMilliseconds(400),
        Timeout = TimeSpan.FromSeconds(4),
        FailureRatio = 0.5,
        MinimumThroughput = 3,
        SamplingDuration = TimeSpan.FromSeconds(15),
        BreakDuration = TimeSpan.FromSeconds(20)
    };
}
