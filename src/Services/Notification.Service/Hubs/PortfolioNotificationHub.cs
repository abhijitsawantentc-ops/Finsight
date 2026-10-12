using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace Notification.Service.Hubs;

public interface IPortfolioNotificationClient
{
    Task ReceiveMarketTick(object tick);
    Task ReceiveRiskAlert(object alert);
    Task ReceiveRiskCalculated(object riskMetrics);
}

public class PortfolioNotificationHub : Hub<IPortfolioNotificationClient>
{
    private readonly ILogger<PortfolioNotificationHub> _logger;

    public PortfolioNotificationHub(ILogger<PortfolioNotificationHub> logger)
    {
        _logger = logger;
    }

    public async Task SubscribeToSymbol(string symbol)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"symbol:{symbol.ToUpperInvariant()}");
        _logger.LogInformation("Connection {ConnectionId} subscribed to symbol {Symbol}", Context.ConnectionId, symbol);
    }

    public async Task UnsubscribeFromSymbol(string symbol)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"symbol:{symbol.ToUpperInvariant()}");
    }

    public async Task SubscribeToPortfolio(string portfolioId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"portfolio:{portfolioId}");
        _logger.LogInformation("Connection {ConnectionId} subscribed to portfolio {PortfolioId}", Context.ConnectionId, portfolioId);
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("SignalR client connected: {ConnectionId}", Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("SignalR client disconnected: {ConnectionId}", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }
}
