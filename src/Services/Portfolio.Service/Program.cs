using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using Portfolio.Service.BackgroundServices;
using Portfolio.Service.Consumers;
using Portfolio.Service.Data;
using Shared.Infrastructure.Logging;
using Shared.Infrastructure.MassTransit;
using Shared.Infrastructure.Middleware;
using Shared.Infrastructure.OpenTelemetry;

var builder = WebApplication.CreateBuilder(args);

builder.ConfigureSharedSerilog("Portfolio.Service");

// Observability
builder.Services.AddSharedOpenTelemetry(builder.Configuration, "Portfolio.Service");

// MassTransit with Consumer
builder.Services.AddSharedMassTransit(builder.Configuration, x =>
{
    x.AddConsumer<MarketDataTickedConsumer>();
});

// Database
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<PortfolioDbContext>(options =>
{
    options.UseNpgsql(connectionString);
});

// MediatR CQRS
builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(Program).Assembly));

// Transactional Outbox Background Worker
builder.Services.AddHostedService<OutboxProcessorWorker>();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Portfolio Service API", Version = "v1" });
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
