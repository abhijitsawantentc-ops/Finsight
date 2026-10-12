using Shared.Contracts.Enums;

namespace Portfolio.Service.Entities;

public class Portfolio
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string BaseCurrency { get; set; } = "USD";
    public decimal CashBalance { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime LastUpdatedUtc { get; set; } = DateTime.UtcNow;

    public List<Position> Positions { get; set; } = new();
    public List<TradeExecution> Trades { get; set; } = new();

    public decimal TotalEquity => CashBalance + Positions.Sum(p => p.TotalMarketValue);
    public decimal TotalUnrealizedPnL => Positions.Sum(p => p.UnrealizedPnL);
}

public class Position
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PortfolioId { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal AveragePurchasePrice { get; set; }
    public decimal CurrentMarketPrice { get; set; }
    public AssetClass AssetClass { get; set; } = AssetClass.Equities;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public decimal TotalMarketValue => Quantity * CurrentMarketPrice;
    public decimal TotalCostBasis => Quantity * AveragePurchasePrice;
    public decimal UnrealizedPnL => TotalMarketValue - TotalCostBasis;
    public decimal UnrealizedPnLPercentage => TotalCostBasis > 0 ? (UnrealizedPnL / TotalCostBasis) * 100m : 0m;
}

public class TradeExecution
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PortfolioId { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public OrderSide Side { get; set; }
    public OrderType Type { get; set; }
    public decimal Quantity { get; set; }
    public decimal Price { get; set; }
    public decimal TotalAmount => Quantity * Price;
    public decimal Fee { get; set; }
    public DateTime ExecutedAtUtc { get; set; } = DateTime.UtcNow;
}

public class OutboxMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime OccurredOnUtc { get; set; } = DateTime.UtcNow;
    public string Type { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime? ProcessedOnUtc { get; set; }
    public string? Error { get; set; }
}
