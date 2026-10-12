using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Shared.Infrastructure.OpenTelemetry;

public static class OpenTelemetryExtensions
{
    public static IServiceCollection AddSharedOpenTelemetry(
        this IServiceCollection services,
        IConfiguration configuration,
        string serviceName)
    {
        var resourceBuilder = ResourceBuilder.CreateDefault()
            .AddService(serviceName: serviceName, serviceVersion: "1.0.0");

        var otlpEndpoint = configuration["OpenTelemetry:Endpoint"] ?? "http://localhost:4317";

        services.AddOpenTelemetry()
            .WithTracing(tracerProviderBuilder =>
            {
                tracerProviderBuilder
                    .SetResourceBuilder(resourceBuilder)
                    .AddAspNetCoreInstrumentation(opts =>
                    {
                        opts.RecordException = true;
                    })
                    .AddHttpClientInstrumentation()
                    .AddSource(serviceName)
                    .AddSource("MassTransit");

                if (!string.IsNullOrEmpty(configuration["OpenTelemetry:Endpoint"]))
                {
                    tracerProviderBuilder.AddOtlpExporter(opts =>
                    {
                        opts.Endpoint = new Uri(otlpEndpoint);
                    });
                }
            })
            .WithMetrics(meterProviderBuilder =>
            {
                meterProviderBuilder
                    .SetResourceBuilder(resourceBuilder)
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();

                if (!string.IsNullOrEmpty(configuration["OpenTelemetry:Endpoint"]))
                {
                    meterProviderBuilder.AddOtlpExporter(opts =>
                    {
                        opts.Endpoint = new Uri(otlpEndpoint);
                    });
                }
            });

        return services;
    }
}
