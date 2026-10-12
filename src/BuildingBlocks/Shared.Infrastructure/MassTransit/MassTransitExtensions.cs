using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Shared.Infrastructure.MassTransit;

public static class MassTransitExtensions
{
    public static IServiceCollection AddSharedMassTransit(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<IBusRegistrationConfigurator>? configureConsumers = null)
    {
        services.AddMassTransit(x =>
        {
            x.SetKebabCaseEndpointNameFormatter();

            configureConsumers?.Invoke(x);

            x.UsingRabbitMq((context, cfg) =>
            {
                var host = configuration["RabbitMQ:Host"] ?? "localhost";
                var username = configuration["RabbitMQ:Username"] ?? "quant_broker";
                var password = configuration["RabbitMQ:Password"] ?? "QuantBrokerPass123!";

                cfg.Host(host, "/", h =>
                {
                    h.Username(username);
                    h.Password(password);
                });

                // Configure standard message retry & circuit breaker policies
                cfg.UseMessageRetry(retry => retry.Incremental(
                    retryLimit: 3,
                    initialInterval: TimeSpan.FromMilliseconds(200),
                    intervalIncrement: TimeSpan.FromMilliseconds(500)
                ));

                cfg.ConfigureEndpoints(context);
            });
        });

        return services;
    }
}
