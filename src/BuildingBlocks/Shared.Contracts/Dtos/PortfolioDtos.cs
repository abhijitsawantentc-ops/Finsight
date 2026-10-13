using Shared.Contracts.Enums;

namespace Shared.Contracts.Dtos;

public record MarketQuoteDto(
    string Symbol,
    decimal Price,
    decimal Bid,
    decimal Ask,
    long Volume,
    string Currency,
    DateTime TimestampUtc,
    bool IsStale = false,
    string? SourceProvider = null
);

public record PositionDto(
    Guid PositionId,
    Guid PortfolioId,
    string Symbol,
    decimal Quantity,
    decimal AveragePurchasePrice,
    decimal CurrentMarketPrice,
    decimal TotalMarketValue,
    decimal UnrealizedPnL,
    decimal UnrealizedPnLPercentage,
    AssetClass AssetClass
);

public record PortfolioDto(
    Guid PortfolioId,
    Guid UserId,
    string Name,
    string BaseCurrency,
    decimal TotalEquity,
    decimal CashBalance,
    decimal TotalUnrealizedPnL,
    IReadOnlyList<PositionDto> Positions,
    DateTime CreatedAtUtc,
    DateTime LastUpdatedUtc
);

public record CreatePortfolioRequest(
    string Name,
    string BaseCurrency,
    decimal InitialDeposit
);

public record ExecuteTradeRequest(
    string Symbol,
    OrderSide Side,
    OrderType Type,
    decimal Quantity,
    decimal? LimitPrice,
    AssetClass AssetClass
);

public record RiskMetricsDto(
    Guid PortfolioId,
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

public record MonteCarloSimulationRequest(
    Guid PortfolioId,
    int SimulationsCount,
    int ForecastDays,
    decimal ConfidenceLevel
);

public record MonteCarloSimulationResultDto(
    Guid PortfolioId,
    int SimulationsCount,
    int ForecastDays,
    decimal InitialPortfolioValue,
    decimal ExpectedFinalValue,
    decimal WorstCaseValue,
    decimal BestCaseValue,
    IReadOnlyList<decimal> Percentiles5To95
);

public record UserRegistrationRequest(
    string Email,
    string Password,
    string FirstName,
    string LastName
);

public record UserLoginRequest(
    string Email,
    string Password
);

public record AuthTokenResponse(
    string AccessToken,
    string RefreshToken,
    int ExpiresInSeconds,
    Guid UserId,
    string Email,
    string Role
);

public record NotificationAlertDto(
    Guid AlertId,
    Guid UserId,
    string Title,
    string Message,
    RiskSeverity Severity,
    DateTime TimestampUtc
);
