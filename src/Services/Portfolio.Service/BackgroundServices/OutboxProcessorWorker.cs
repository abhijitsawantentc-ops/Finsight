using System.Text.Json;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Portfolio.Service.Data;
using Shared.Contracts.Events;

namespace Portfolio.Service.BackgroundServices;

public class OutboxProcessorWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<OutboxProcessorWorker> _logger;

    public OutboxProcessorWorker(
        IServiceProvider serviceProvider,
        ILogger<OutboxProcessorWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Portfolio OutboxProcessorWorker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();
                var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

                var pendingMessages = await dbContext.OutboxMessages
                    .Where(m => m.ProcessedOnUtc == null)
                    .OrderBy(m => m.OccurredOnUtc)
                    .Take(20)
                    .ToListAsync(stoppingToken);

                foreach (var message in pendingMessages)
                {
                    try
                    {
                        if (message.Type.Contains(nameof(OrderExecutedEvent)))
                        {
                            var @event = JsonSerializer.Deserialize<OrderExecutedEvent>(message.Content);
                            if (@event != null)
                            {
                                await publishEndpoint.Publish(@event, stoppingToken);
                            }
                        }
                        else if (message.Type.Contains(nameof(PortfolioRebalancedEvent)))
                        {
                            var @event = JsonSerializer.Deserialize<PortfolioRebalancedEvent>(message.Content);
                            if (@event != null)
                            {
                                await publishEndpoint.Publish(@event, stoppingToken);
                            }
                        }

                        message.ProcessedOnUtc = DateTime.UtcNow;
                    }
                    catch (Exception ex)
                    {
                        message.Error = ex.Message;
                        _logger.LogError(ex, "Failed to publish outbox message {MessageId}", message.Id);
                    }
                }

                if (pendingMessages.Count > 0)
                {
                    await dbContext.SaveChangesAsync(stoppingToken);
                }

                await Task.Delay(1000, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception in Portfolio OutboxProcessorWorker loop.");
                await Task.Delay(3000, stoppingToken);
            }
        }

        _logger.LogInformation("Portfolio OutboxProcessorWorker stopping.");
    }
}
