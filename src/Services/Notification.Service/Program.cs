using Microsoft.OpenApi.Models;
using Notification.Service.Consumers;
using Notification.Service.Hubs;
using Shared.Infrastructure.Logging;
using Shared.Infrastructure.MassTransit;
using Shared.Infrastructure.Middleware;
using Shared.Infrastructure.OpenTelemetry;

var builder = WebApplication.CreateBuilder(args);

builder.ConfigureSharedSerilog("Notification.Service");

// Observability
builder.Services.AddSharedOpenTelemetry(builder.Configuration, "Notification.Service");

// SignalR
builder.Services.AddSignalR();

// MassTransit with SignalR Consumers
builder.Services.AddSharedMassTransit(builder.Configuration, x =>
{
    x.AddConsumer<RiskAlertNotificationConsumer>();
    x.AddConsumer<MarketTickNotificationConsumer>();
    x.AddConsumer<PortfolioRiskNotificationConsumer>();
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("SignalRCorsPolicy", policy =>
    {
        policy.AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials()
              .SetIsOriginAllowed(_ => true);
    });
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Notification Service API", Version = "v1" });
});

builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<GlobalExceptionHandlerMiddleware>();

app.UseCors("SignalRCorsPolicy");

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();
app.MapHealthChecks("/health");

// SignalR Hub Endpoint
app.MapHub<PortfolioNotificationHub>("/hubs/notifications");

app.Run();
