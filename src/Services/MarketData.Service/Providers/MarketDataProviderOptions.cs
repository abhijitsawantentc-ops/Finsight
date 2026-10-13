namespace MarketData.Service.Providers;

public class FinnhubOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://finnhub.io/api/v1/";
}

public class AlphaVantageOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://www.alphavantage.co/";
}

public class MarketDataProviderOptions
{
    public FinnhubOptions Finnhub { get; set; } = new();
    public AlphaVantageOptions AlphaVantage { get; set; } = new();
    public bool EnableSandboxFallback { get; set; } = true;
}
