using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Portfolio.Service.Data;
using Shared.Contracts.Events;

namespace Portfolio.Service.Consumers;

public class MarketDataTickedConsumer : IConsumer<MarketDataTickedEvent>
{
    private readonly PortfolioDbContext _dbContext;
    private readonly ILogger<MarketDataTickedConsumer> _logger;

    public MarketDataTickedConsumer(PortfolioDbContext dbContext, ILogger<MarketDataTickedConsumer> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<MarketDataTickedEvent> context)
    {
        var message = context.Message;

        var matchingPositions = await _dbContext.Positions
            .Where(p => p.Symbol == message.Symbol)
            .ToListAsync(context.CancellationToken);

        if (matchingPositions.Count == 0)
        {
            return;
        }

        foreach (var position in matchingPositions)
        {
            position.CurrentMarketPrice = message.Price;
            position.UpdatedAtUtc = message.TimestampUtc;
        }

        await _dbContext.SaveChangesAsync(context.CancellationToken);
        _logger.LogDebug("Updated {Count} position(s) for symbol {Symbol} to price {Price}",
            matchingPositions.Count, message.Symbol, message.Price);
    }
}
