using MarketData.Service.BackgroundServices;
using MarketData.Service.Channels;
using MarketData.Service.Services;
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
    // Will rely on local concurrent memory cache inside RedisMarketCacheService
}

// System.Threading.Channels Bounded Channel
builder.Services.AddSingleton<IMarketDataChannel, MarketDataChannel>();
builder.Services.AddSingleton<IRedisMarketCacheService, RedisMarketCacheService>();

// Background Ingestion & Channel Consumer
builder.Services.AddHostedService<MarketDataIngestionWorker>();
builder.Services.AddHostedService<MarketDataChannelConsumerWorker>();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Market Data Service API", Version = "v1" });
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
