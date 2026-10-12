using Shared.Infrastructure.Logging;
using Shared.Infrastructure.Middleware;
using Shared.Infrastructure.OpenTelemetry;

var builder = WebApplication.CreateBuilder(args);

builder.ConfigureSharedSerilog("ApiGateway");

// Observability
builder.Services.AddSharedOpenTelemetry(builder.Configuration, "ApiGateway");

// Reverse Proxy
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

builder.Services.AddCors(options =>
{
    options.AddPolicy("GatewayCorsPolicy", policy =>
    {
        policy.AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials()
              .SetIsOriginAllowed(_ => true);
    });
});

builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<GlobalExceptionHandlerMiddleware>();

app.UseCors("GatewayCorsPolicy");

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapHealthChecks("/health");

app.MapReverseProxy();

app.MapFallbackToFile("index.html");

app.Run();
