namespace MarketData.Service.Exceptions;

public class RateLimitExceededException : Exception
{
    public string Provider { get; }
    public TimeSpan? RetryAfter { get; }

    public RateLimitExceededException(string provider, string message, TimeSpan? retryAfter = null)
        : base(message)
    {
        Provider = provider;
        RetryAfter = retryAfter;
    }
}
