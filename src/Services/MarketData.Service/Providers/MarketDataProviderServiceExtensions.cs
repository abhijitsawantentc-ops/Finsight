using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MarketData.Service.Providers;

/// <summary>
/// Dependency injection extensions for registering typed market data provider HttpClients and options.
/// </summary>
public static class MarketDataProviderServiceExtensions
{
    /// <summary>
    /// Configures typed HTTP clients, secure API credentials, and provider instances for Finnhub and Alpha Vantage.
    /// </summary>
    public static IServiceCollection AddMarketDataProviders(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var providerOptions = new MarketDataProviderOptions
        {
            Finnhub = new FinnhubOptions
            {
                ApiKey = configuration["MarketData:Providers:Finnhub:ApiKey"] ?? string.Empty,
                BaseUrl = configuration["MarketData:Providers:Finnhub:BaseUrl"] ?? "https://finnhub.io/api/v1/"
            },
            AlphaVantage = new AlphaVantageOptions
            {
                ApiKey = configuration["MarketData:Providers:AlphaVantage:ApiKey"] ?? string.Empty,
                BaseUrl = configuration["MarketData:Providers:AlphaVantage:BaseUrl"] ?? "https://www.alphavantage.co/"
            },
            EnableSandboxFallback = configuration.GetValue<bool>("MarketData:Providers:EnableSandboxFallback", true)
        };

        services.AddSingleton(providerOptions);

        // Typed HttpClient for Finnhub with default headers and timeout
        services.AddHttpClient<FinnhubMarketDataProvider>(client =>
        {
            client.BaseAddress = new Uri(providerOptions.Finnhub.BaseUrl);
            client.DefaultRequestHeaders.Add("Accept", "application/json");
            client.Timeout = TimeSpan.FromSeconds(10);
        });

        // Typed HttpClient for Alpha Vantage with default headers and timeout
        services.AddHttpClient<AlphaVantageMarketDataProvider>(client =>
        {
            client.BaseAddress = new Uri(providerOptions.AlphaVantage.BaseUrl);
            client.DefaultRequestHeaders.Add("Accept", "application/json");
            client.Timeout = TimeSpan.FromSeconds(10);
        });

        // Register both providers under the common IMarketDataProvider interface
        services.AddSingleton<IMarketDataProvider>(sp => sp.GetRequiredService<FinnhubMarketDataProvider>());
        services.AddSingleton<IMarketDataProvider>(sp => sp.GetRequiredService<AlphaVantageMarketDataProvider>());

        return services;
    }
}
