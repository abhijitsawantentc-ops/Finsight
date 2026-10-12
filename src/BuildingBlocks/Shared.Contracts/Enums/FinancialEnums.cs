namespace Shared.Contracts.Enums;

public enum OrderSide
{
    Buy = 1,
    Sell = 2
}

public enum OrderType
{
    Market = 1,
    Limit = 2,
    StopLoss = 3,
    StopLimit = 4
}

public enum RiskSeverity
{
    Info = 0,
    Low = 1,
    Medium = 2,
    High = 3,
    Critical = 4
}

public enum AssetClass
{
    Equities = 1,
    FixedIncome = 2,
    Commodities = 3,
    Crypto = 4,
    Forex = 5
}

public enum TimeInForce
{
    Gtc = 1, // Good 'Til Cancelled
    Ioc = 2, // Immediate or Cancel
    Fok = 3  // Fill or Kill
}

public enum RiskMetricType
{
    ValueAtRisk95 = 1,
    ValueAtRisk99 = 2,
    ConditionalVaR = 3,
    MaxDrawdown = 4,
    SharpeRatio = 5,
    SortinoRatio = 6,
    Beta = 7,
    PortfolioVolatility = 8
}
