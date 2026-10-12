using MediatR;
using Microsoft.EntityFrameworkCore;
using Portfolio.Service.Data;
using Shared.Contracts.Dtos;

namespace Portfolio.Service.Features.Portfolios.Queries;

public record GetPortfolioByIdQuery(Guid PortfolioId) : IRequest<PortfolioDto?>;

public record GetUserPortfoliosQuery(Guid UserId) : IRequest<IReadOnlyList<PortfolioDto>>;

public class PortfolioQueryHandlers :
    IRequestHandler<GetPortfolioByIdQuery, PortfolioDto?>,
    IRequestHandler<GetUserPortfoliosQuery, IReadOnlyList<PortfolioDto>>
{
    private readonly PortfolioDbContext _dbContext;

    public PortfolioQueryHandlers(PortfolioDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<PortfolioDto?> Handle(GetPortfolioByIdQuery request, CancellationToken cancellationToken)
    {
        var portfolio = await _dbContext.Portfolios
            .Include(p => p.Positions)
            .FirstOrDefaultAsync(p => p.Id == request.PortfolioId, cancellationToken);

        return portfolio != null ? MapToDto(portfolio) : null;
    }

    public async Task<IReadOnlyList<PortfolioDto>> Handle(GetUserPortfoliosQuery request, CancellationToken cancellationToken)
    {
        var portfolios = await _dbContext.Portfolios
            .Include(p => p.Positions)
            .Where(p => p.UserId == request.UserId)
            .ToListAsync(cancellationToken);

        return portfolios.Select(MapToDto).ToList();
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
