using Microsoft.OpenApi.Models;
using RiskEngine.Service.Calculators;
using RiskEngine.Service.Consumers;
using Shared.Infrastructure.Logging;
using Shared.Infrastructure.MassTransit;
using Shared.Infrastructure.Middleware;
using Shared.Infrastructure.OpenTelemetry;

var builder = WebApplication.CreateBuilder(args);

builder.ConfigureSharedSerilog("RiskEngine.Service");

// Observability
builder.Services.AddSharedOpenTelemetry(builder.Configuration, "RiskEngine.Service");

// MassTransit with Risk Consumer
builder.Services.AddSharedMassTransit(builder.Configuration, x =>
{
    x.AddConsumer<PortfolioRebalancedConsumer>();
});

// Quantitative Risk Engine
builder.Services.AddSingleton<IQuantitativeRiskCalculator, QuantitativeRiskCalculator>();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Risk Engine Service API", Version = "v1" });
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
