using MarketData.Service.BackgroundServices;
using MarketData.Service.Channels;
using MarketData.Service.Providers;
using MarketData.Service.Resilience.Polly;
using MarketData.Service.Resilience.RateLimiting;
using MarketData.Service.Services;
using MarketData.Service.Services.Caching;
using MarketData.Service.Services.Orchestration;
using Microsoft.OpenApi.Models;
using Shared.Infrastructure.Logging;
using Shared.Infrastructure.MassTransit;
using Shared.Infrastructure.Middleware;
using Shared.Infrastructure.OpenTelemetry;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

builder.ConfigureSharedSerilog("MarketData.Service");

// Observability
builder.Services.AddSharedOpenTelemetry(builder.Configuration, "MarketData.Service");

// MassTransit
builder.Services.AddSharedMassTransit(builder.Configuration);

// Redis Connection (optional graceful connection)
var redisConn = builder.Configuration["Redis:ConnectionString"] ?? "localhost:6379";
try
{
    var multiplexer = ConnectionMultiplexer.Connect(new ConfigurationOptions
    {
        EndPoints = { redisConn },
        AbortOnConnectFail = false,
        ConnectTimeout = 3000
    });
    builder.Services.AddSingleton<IConnectionMultiplexer>(multiplexer);
}
catch
{
    // Will rely on local concurrent memory cache inside CacheService
}

// 1. Dual-Tier Caching Strategy (60-sec hot cache, 24-hr stale cache)
var cacheOptions = new CacheServiceOptions
{
    HotCacheTtl = TimeSpan.FromSeconds(builder.Configuration.GetValue<int>("MarketData:Caching:HotCacheTtlSeconds", 60)),
    StaleCacheTtl = TimeSpan.FromHours(builder.Configuration.GetValue<int>("MarketData:Caching:StaleCacheTtlHours", 24))
};
builder.Services.AddSingleton(cacheOptions);
builder.Services.AddSingleton<ICacheService, CacheService>();
builder.Services.AddSingleton<IRedisMarketCacheService, RedisMarketCacheService>();

// 2. Provider Rate Limiting (Finnhub & Alpha Vantage)
var rateLimiterOptions = new MarketDataRateLimiterOptions
{
    Finnhub = new ProviderRateLimitOptions
    {
        PermitLimit = builder.Configuration.GetValue<int>("MarketData:RateLimiting:Finnhub:PermitLimit", 30),
        Window = TimeSpan.FromSeconds(builder.Configuration.GetValue<int>("MarketData:RateLimiting:Finnhub:WindowSeconds", 60)),
        QueueLimit = builder.Configuration.GetValue<int>("MarketData:RateLimiting:Finnhub:QueueLimit", 0)
    },
    AlphaVantage = new ProviderRateLimitOptions
    {
        PermitLimit = builder.Configuration.GetValue<int>("MarketData:RateLimiting:AlphaVantage:PermitLimit", 5),
        Window = TimeSpan.FromSeconds(builder.Configuration.GetValue<int>("MarketData:RateLimiting:AlphaVantage:WindowSeconds", 60)),
        QueueLimit = builder.Configuration.GetValue<int>("MarketData:RateLimiting:AlphaVantage:QueueLimit", 0)
    }
};
builder.Services.AddSingleton(rateLimiterOptions);
builder.Services.AddSingleton<IProviderRateLimiter, MarketDataRateLimiter>();

// 3. Polly Resilience Pipelines (Retry, Exponential Backoff, Circuit Breaker, Timeout)
var resilienceOptions = new PollyResilienceOptions
{
    Finnhub = new ProviderResilienceConfig
    {
        MaxRetryAttempts = builder.Configuration.GetValue<int>("MarketData:Resilience:Finnhub:MaxRetryAttempts", 2),
        InitialRetryDelay = TimeSpan.FromMilliseconds(builder.Configuration.GetValue<int>("MarketData:Resilience:Finnhub:InitialRetryDelayMs", 300)),
        Timeout = TimeSpan.FromSeconds(builder.Configuration.GetValue<int>("MarketData:Resilience:Finnhub:TimeoutSeconds", 3)),
        FailureRatio = builder.Configuration.GetValue<double>("MarketData:Resilience:Finnhub:FailureRatio", 0.5),
        MinimumThroughput = builder.Configuration.GetValue<int>("MarketData:Resilience:Finnhub:MinimumThroughput", 3),
        SamplingDuration = TimeSpan.FromSeconds(builder.Configuration.GetValue<int>("MarketData:Resilience:Finnhub:SamplingDurationSeconds", 15)),
        BreakDuration = TimeSpan.FromSeconds(builder.Configuration.GetValue<int>("MarketData:Resilience:Finnhub:BreakDurationSeconds", 20))
    },
    AlphaVantage = new ProviderResilienceConfig
    {
        MaxRetryAttempts = builder.Configuration.GetValue<int>("MarketData:Resilience:AlphaVantage:MaxRetryAttempts", 2),
        InitialRetryDelay = TimeSpan.FromMilliseconds(builder.Configuration.GetValue<int>("MarketData:Resilience:AlphaVantage:InitialRetryDelayMs", 400)),
        Timeout = TimeSpan.FromSeconds(builder.Configuration.GetValue<int>("MarketData:Resilience:AlphaVantage:TimeoutSeconds", 4)),
        FailureRatio = builder.Configuration.GetValue<double>("MarketData:Resilience:AlphaVantage:FailureRatio", 0.5),
        MinimumThroughput = builder.Configuration.GetValue<int>("MarketData:Resilience:AlphaVantage:MinimumThroughput", 3),
        SamplingDuration = TimeSpan.FromSeconds(builder.Configuration.GetValue<int>("MarketData:Resilience:AlphaVantage:SamplingDurationSeconds", 15)),
        BreakDuration = TimeSpan.FromSeconds(builder.Configuration.GetValue<int>("MarketData:Resilience:AlphaVantage:BreakDurationSeconds", 20))
    }
};
builder.Services.AddSingleton(resilienceOptions);
builder.Services.AddSingleton<IPollyResilienceRegistry, PollyResilienceRegistry>();

// 4. Market Data Providers (Primary Finnhub, Secondary Alpha Vantage)
var providerOptions = new MarketDataProviderOptions
{
    Finnhub = new FinnhubOptions
    {
        ApiKey = builder.Configuration["MarketData:Providers:Finnhub:ApiKey"] ?? string.Empty,
        BaseUrl = builder.Configuration["MarketData:Providers:Finnhub:BaseUrl"] ?? "https://finnhub.io/api/v1/"
    },
    AlphaVantage = new AlphaVantageOptions
    {
        ApiKey = builder.Configuration["MarketData:Providers:AlphaVantage:ApiKey"] ?? string.Empty,
        BaseUrl = builder.Configuration["MarketData:Providers:AlphaVantage:BaseUrl"] ?? "https://www.alphavantage.co/"
    },
    EnableSandboxFallback = builder.Configuration.GetValue<bool>("MarketData:Providers:EnableSandboxFallback", true)
};
builder.Services.AddSingleton(providerOptions);

builder.Services.AddHttpClient<FinnhubMarketDataProvider>(client =>
{
    client.BaseAddress = new Uri(providerOptions.Finnhub.BaseUrl);
});
builder.Services.AddHttpClient<AlphaVantageMarketDataProvider>(client =>
{
    client.BaseAddress = new Uri(providerOptions.AlphaVantage.BaseUrl);
});

builder.Services.AddSingleton<IMarketDataProvider>(sp => sp.GetRequiredService<FinnhubMarketDataProvider>());
builder.Services.AddSingleton<IMarketDataProvider>(sp => sp.GetRequiredService<AlphaVantageMarketDataProvider>());

// 5. MarketDataOrchestrator
builder.Services.AddSingleton<IMarketDataOrchestrator, MarketDataOrchestrator>();

// 6. System.Threading.Channels Bounded Channel
builder.Services.AddSingleton<IMarketDataChannel, MarketDataChannel>();

// 7. Background Ingestion & Channel Consumer
builder.Services.AddHostedService<MarketDataIngestionWorker>();
builder.Services.AddHostedService<MarketDataChannelConsumerWorker>();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Market Data Service API (Resilient & Fault-Tolerant)", Version = "v1" });
});

builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<GlobalExceptionHandlerMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();
