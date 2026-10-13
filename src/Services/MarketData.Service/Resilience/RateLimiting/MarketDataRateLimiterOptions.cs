namespace MarketData.Service.Resilience.RateLimiting;

public class ProviderRateLimitOptions
{
    public int PermitLimit { get; set; } = 30;
    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(1);
    public int QueueLimit { get; set; } = 0;
}

public class MarketDataRateLimiterOptions
{
    public ProviderRateLimitOptions Finnhub { get; set; } = new()
    {
        PermitLimit = 30,
        Window = TimeSpan.FromMinutes(1),
        QueueLimit = 0
    };

    public ProviderRateLimitOptions AlphaVantage { get; set; } = new()
    {
        PermitLimit = 5,
        Window = TimeSpan.FromMinutes(1),
        QueueLimit = 0
    };
}
