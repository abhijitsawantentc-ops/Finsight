using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;

namespace Shared.Infrastructure.Logging;

public static class SerilogExtensions
{
    public static void ConfigureSharedSerilog(this WebApplicationBuilder builder, string serviceName)
    {
        var seqUrl = builder.Configuration["Seq:ServerUrl"] ?? "http://localhost:5341";

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
            .MinimumLevel.Override("MassTransit", LogEventLevel.Information)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Service", serviceName)
            .Enrich.WithProperty("Environment", builder.Environment.EnvironmentName)
            .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{Service}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.Seq(seqUrl)
            .CreateLogger();

        builder.Host.UseSerilog();
    }
}
