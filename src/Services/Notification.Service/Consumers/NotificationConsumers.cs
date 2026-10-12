using MassTransit;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Notification.Service.Hubs;
using Shared.Contracts.Dtos;
using Shared.Contracts.Events;

namespace Notification.Service.Consumers;

public class RiskAlertNotificationConsumer : IConsumer<RiskThresholdExceededEvent>
{
    private readonly IHubContext<PortfolioNotificationHub, IPortfolioNotificationClient> _hubContext;
    private readonly ILogger<RiskAlertNotificationConsumer> _logger;

    public RiskAlertNotificationConsumer(
        IHubContext<PortfolioNotificationHub, IPortfolioNotificationClient> hubContext,
        ILogger<RiskAlertNotificationConsumer> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<RiskThresholdExceededEvent> context)
    {
        var msg = context.Message;
        _logger.LogWarning("Broadcasting risk threshold alert for portfolio {PortfolioId}: {Violation}",
            msg.PortfolioId, msg.ViolationDetails);

        var alert = new NotificationAlertDto(
            AlertId: Guid.NewGuid(),
            UserId: msg.UserId,
            Title: $"CRITICAL RISK ALERT: {msg.PortfolioName}",
            Message: msg.ViolationDetails,
            Severity: msg.Severity,
            TimestampUtc: msg.TriggeredAtUtc
        );

        // Push to portfolio subscribers and global
        await _hubContext.Clients.Group($"portfolio:{msg.PortfolioId}").ReceiveRiskAlert(alert);
        await _hubContext.Clients.All.ReceiveRiskAlert(alert);
    }
}

public class MarketTickNotificationConsumer : IConsumer<MarketDataTickedEvent>
{
    private readonly IHubContext<PortfolioNotificationHub, IPortfolioNotificationClient> _hubContext;

    public MarketTickNotificationConsumer(IHubContext<PortfolioNotificationHub, IPortfolioNotificationClient> hubContext)
    {
        _hubContext = hubContext;
    }

    public async Task Consume(ConsumeContext<MarketDataTickedEvent> context)
    {
        var msg = context.Message;
        var quoteDto = new MarketQuoteDto(
            msg.Symbol,
            msg.Price,
            msg.Bid,
            msg.Ask,
            msg.Volume,
            msg.Currency,
            msg.TimestampUtc
        );

        await _hubContext.Clients.Group($"symbol:{msg.Symbol.ToUpperInvariant()}").ReceiveMarketTick(quoteDto);
    }
}

public class PortfolioRiskNotificationConsumer : IConsumer<PortfolioRiskCalculatedEvent>
{
    private readonly IHubContext<PortfolioNotificationHub, IPortfolioNotificationClient> _hubContext;

    public PortfolioRiskNotificationConsumer(IHubContext<PortfolioNotificationHub, IPortfolioNotificationClient> hubContext)
    {
        _hubContext = hubContext;
    }

    public async Task Consume(ConsumeContext<PortfolioRiskCalculatedEvent> context)
    {
        var msg = context.Message;
        await _hubContext.Clients.Group($"portfolio:{msg.PortfolioId}").ReceiveRiskCalculated(msg);
    }
}
