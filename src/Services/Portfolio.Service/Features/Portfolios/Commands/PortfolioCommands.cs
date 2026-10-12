using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Portfolio.Service.Data;
using Portfolio.Service.Entities;
using Shared.Contracts.Dtos;
using Shared.Contracts.Enums;
using Shared.Contracts.Events;

namespace Portfolio.Service.Features.Portfolios.Commands;

public record CreatePortfolioCommand(
    Guid UserId,
    string Name,
    string BaseCurrency,
    decimal InitialDeposit
) : IRequest<PortfolioDto>;

public record ExecuteTradeCommand(
    Guid PortfolioId,
    Guid UserId,
    string Symbol,
    OrderSide Side,
    OrderType Type,
    decimal Quantity,
    decimal? LimitPrice,
    AssetClass AssetClass
) : IRequest<PortfolioDto>;

public record RebalancePortfolioCommand(
    Guid PortfolioId,
    Guid UserId
) : IRequest<PortfolioDto>;

public class PortfolioCommandHandlers :
    IRequestHandler<CreatePortfolioCommand, PortfolioDto>,
    IRequestHandler<ExecuteTradeCommand, PortfolioDto>,
    IRequestHandler<RebalancePortfolioCommand, PortfolioDto>
{
    private readonly PortfolioDbContext _dbContext;

    public PortfolioCommandHandlers(PortfolioDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<PortfolioDto> Handle(CreatePortfolioCommand request, CancellationToken cancellationToken)
    {
        var portfolio = new Entities.Portfolio
        {
            Id = Guid.NewGuid(),
            UserId = request.UserId,
            Name = request.Name,
            BaseCurrency = request.BaseCurrency,
            CashBalance = request.InitialDeposit,
            CreatedAtUtc = DateTime.UtcNow,
            LastUpdatedUtc = DateTime.UtcNow
        };

        _dbContext.Portfolios.Add(portfolio);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return MapToDto(portfolio);
    }

    public async Task<PortfolioDto> Handle(ExecuteTradeCommand request, CancellationToken cancellationToken)
    {
        var portfolio = await _dbContext.Portfolios
            .Include(p => p.Positions)
            .Include(p => p.Trades)
            .FirstOrDefaultAsync(p => p.Id == request.PortfolioId, cancellationToken);

        if (portfolio == null)
        {
            throw new KeyNotFoundException($"Portfolio with ID {request.PortfolioId} not found.");
        }

        var executionPrice = request.LimitPrice ?? 150.00m; // Fallback or market price
        var tradeAmount = request.Quantity * executionPrice;
        const decimal fee = 1.50m;

        if (request.Side == OrderSide.Buy)
        {
            if (portfolio.CashBalance < tradeAmount + fee)
            {
                throw new InvalidOperationException("Insufficient cash balance to execute buy order.");
            }

            portfolio.CashBalance -= (tradeAmount + fee);

            var existingPosition = portfolio.Positions.FirstOrDefault(p => p.Symbol.Equals(request.Symbol, StringComparison.OrdinalIgnoreCase));
            if (existingPosition != null)
            {
                var totalCost = existingPosition.TotalCostBasis + tradeAmount;
                var totalQty = existingPosition.Quantity + request.Quantity;
                existingPosition.AveragePurchasePrice = Math.Round(totalCost / totalQty, 4);
                existingPosition.Quantity = totalQty;
                existingPosition.CurrentMarketPrice = executionPrice;
                existingPosition.UpdatedAtUtc = DateTime.UtcNow;
            }
            else
            {
                portfolio.Positions.Add(new Position
                {
                    Id = Guid.NewGuid(),
                    PortfolioId = portfolio.Id,
                    Symbol = request.Symbol.ToUpperInvariant(),
                    Quantity = request.Quantity,
                    AveragePurchasePrice = executionPrice,
                    CurrentMarketPrice = executionPrice,
                    AssetClass = request.AssetClass,
                    UpdatedAtUtc = DateTime.UtcNow
                });
            }
        }
        else // Sell
        {
            var existingPosition = portfolio.Positions.FirstOrDefault(p => p.Symbol.Equals(request.Symbol, StringComparison.OrdinalIgnoreCase));
            if (existingPosition == null || existingPosition.Quantity < request.Quantity)
            {
                throw new InvalidOperationException("Insufficient asset quantity to execute sell order.");
            }

            portfolio.CashBalance += (tradeAmount - fee);
            existingPosition.Quantity -= request.Quantity;
            existingPosition.CurrentMarketPrice = executionPrice;
            existingPosition.UpdatedAtUtc = DateTime.UtcNow;

            if (existingPosition.Quantity == 0)
            {
                portfolio.Positions.Remove(existingPosition);
            }
        }

        var trade = new TradeExecution
        {
            Id = Guid.NewGuid(),
            PortfolioId = portfolio.Id,
            Symbol = request.Symbol.ToUpperInvariant(),
            Side = request.Side,
            Type = request.Type,
            Quantity = request.Quantity,
            Price = executionPrice,
            Fee = fee,
            ExecutedAtUtc = DateTime.UtcNow
        };
        portfolio.Trades.Add(trade);
        portfolio.LastUpdatedUtc = DateTime.UtcNow;

        // Save transactional Outbox Message for OrderExecutedEvent
        var executedEvent = new OrderExecutedEvent(
            trade.Id,
            portfolio.Id,
            portfolio.UserId,
            trade.Symbol,
            trade.Side,
            trade.Type,
            trade.Quantity,
            trade.Price,
            trade.Fee,
            trade.ExecutedAtUtc
        );

        _dbContext.OutboxMessages.Add(new OutboxMessage
        {
            Id = Guid.NewGuid(),
            OccurredOnUtc = DateTime.UtcNow,
            Type = typeof(OrderExecutedEvent).AssemblyQualifiedName ?? nameof(OrderExecutedEvent),
            Content = JsonSerializer.Serialize(executedEvent)
        });

        await _dbContext.SaveChangesAsync(cancellationToken);

        return MapToDto(portfolio);
    }

    public async Task<PortfolioDto> Handle(RebalancePortfolioCommand request, CancellationToken cancellationToken)
    {
        var portfolio = await _dbContext.Portfolios
            .Include(p => p.Positions)
            .FirstOrDefaultAsync(p => p.Id == request.PortfolioId, cancellationToken);

        if (portfolio == null)
        {
            throw new KeyNotFoundException($"Portfolio with ID {request.PortfolioId} not found.");
        }

        portfolio.LastUpdatedUtc = DateTime.UtcNow;

        var totalEquity = portfolio.TotalEquity;
        var weights = new Dictionary<string, decimal>();
        var quantities = new Dictionary<string, decimal>();

        foreach (var pos in portfolio.Positions)
        {
            var weight = totalEquity > 0 ? Math.Round(pos.TotalMarketValue / totalEquity, 4) : 0m;
            weights[pos.Symbol] = weight;
            quantities[pos.Symbol] = pos.Quantity;
        }

        var rebalancedEvent = new PortfolioRebalancedEvent(
            portfolio.Id,
            portfolio.UserId,
            portfolio.Name,
            totalEquity,
            weights,
            quantities,
            DateTime.UtcNow
        );

        _dbContext.OutboxMessages.Add(new OutboxMessage
        {
            Id = Guid.NewGuid(),
            OccurredOnUtc = DateTime.UtcNow,
            Type = typeof(PortfolioRebalancedEvent).AssemblyQualifiedName ?? nameof(PortfolioRebalancedEvent),
            Content = JsonSerializer.Serialize(rebalancedEvent)
        });

        await _dbContext.SaveChangesAsync(cancellationToken);

        return MapToDto(portfolio);
    }

    private static PortfolioDto MapToDto(Entities.Portfolio portfolio)
    {
        var positions = portfolio.Positions.Select(p => new PositionDto(
            p.Id,
            p.PortfolioId,
            p.Symbol,
            p.Quantity,
            p.AveragePurchasePrice,
            p.CurrentMarketPrice,
            p.TotalMarketValue,
            p.UnrealizedPnL,
            p.UnrealizedPnLPercentage,
            p.AssetClass
        )).ToList();

        return new PortfolioDto(
            portfolio.Id,
            portfolio.UserId,
            portfolio.Name,
            portfolio.BaseCurrency,
            portfolio.TotalEquity,
            portfolio.CashBalance,
            portfolio.TotalUnrealizedPnL,
            positions,
            portfolio.CreatedAtUtc,
            portfolio.LastUpdatedUtc
        );
    }
}
