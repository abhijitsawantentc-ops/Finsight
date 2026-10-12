using Shared.Contracts.Enums;

namespace Shared.Contracts.Events;

public record MarketDataTickedEvent(
    string Symbol,
    decimal Price,
    decimal Bid,
    decimal Ask,
    long Volume,
    string Currency,
    DateTime TimestampUtc
);

public record OrderExecutedEvent(
    Guid OrderId,
    Guid PortfolioId,
    Guid UserId,
    string Symbol,
    OrderSide Side,
    OrderType Type,
    decimal Quantity,
    decimal ExecutionPrice,
    decimal Fee,
    DateTime ExecutedAtUtc
);

public record PortfolioRebalancedEvent(
    Guid PortfolioId,
    Guid UserId,
    string PortfolioName,
    decimal TotalValuation,
    IReadOnlyDictionary<string, decimal> AssetWeights,
    IReadOnlyDictionary<string, decimal> PositionQuantities,
    DateTime RebalancedAtUtc
);

public record PortfolioRiskCalculatedEvent(
    Guid PortfolioId,
    Guid UserId,
    decimal TotalValuation,
    decimal ValueAtRisk95,
    decimal ValueAtRisk99,
    decimal ConditionalVaR95,
    decimal SharpeRatio,
    decimal SortinoRatio,
    decimal Beta,
    decimal AnnualizedVolatility,
    decimal MaxDrawdownPercent,
    DateTime CalculatedAtUtc
);

public record RiskThresholdExceededEvent(
    Guid PortfolioId,
    Guid UserId,
    string PortfolioName,
    RiskMetricType MetricType,
    decimal CurrentValue,
    decimal ThresholdValue,
    RiskSeverity Severity,
    string ViolationDetails,
    DateTime TriggeredAtUtc
);

public record UserCreatedEvent(
    Guid UserId,
    string Email,
    string FirstName,
    string LastName,
    string Role,
    DateTime CreatedAtUtc
);
