using MathNet.Numerics.Distributions;
using MathNet.Numerics.Statistics;
using Shared.Contracts.Dtos;

namespace RiskEngine.Service.Calculators;

public interface IQuantitativeRiskCalculator
{
    RiskMetricsDto CalculateRiskMetrics(
        Guid portfolioId,
        decimal totalValuation,
        IReadOnlyDictionary<string, decimal> assetWeights);

    MonteCarloSimulationResultDto RunMonteCarloSimulation(
        Guid portfolioId,
        decimal currentValuation,
        int simulationsCount = 1000,
        int forecastDays = 30,
        decimal confidenceLevel = 0.95m);
}

public class QuantitativeRiskCalculator : IQuantitativeRiskCalculator
{
    private const double RiskFreeRateAnnual = 0.045; // 4.5% US Treasury benchmark
    private const double DefaultDailyVolatility = 0.016; // ~25% annualized
    private const double DefaultDailyReturn = 0.0004; // ~10% annualized

    public RiskMetricsDto CalculateRiskMetrics(
        Guid portfolioId,
        decimal totalValuation,
        IReadOnlyDictionary<string, decimal> assetWeights)
    {
        var portfolioValuation = (double)totalValuation;
        if (portfolioValuation <= 0)
        {
            return new RiskMetricsDto(
                portfolioId,
                0m, 0m, 0m, 0m, 0m, 1.0m, 0m, 0m,
                DateTime.UtcNow
            );
        }

        // Calculate weighted volatility and return based on asset diversification
        var assetCount = Math.Max(1, assetWeights.Count);
        var diversificationBenefit = 1.0 / Math.Sqrt(assetCount);
        var annualizedVolatility = (DefaultDailyVolatility * Math.Sqrt(252)) * (0.6 + 0.4 * diversificationBenefit);
        var annualizedReturn = (DefaultDailyReturn * 252) * 1.1;

        // Value at Risk (Parametric)
        var normalDist = new Normal(0, 1);
        var z95 = normalDist.InverseCumulativeDistribution(0.95); // ~1.645
        var z99 = normalDist.InverseCumulativeDistribution(0.99); // ~2.326

        // 1-Day & 10-Day VaR scaled
        var dailyVol = annualizedVolatility / Math.Sqrt(252);
        var var95Absolute = portfolioValuation * (z95 * dailyVol * Math.Sqrt(10));
        var var99Absolute = portfolioValuation * (z99 * dailyVol * Math.Sqrt(10));

        // Conditional VaR (Expected Shortfall)
        var phi95 = normalDist.Density(z95);
        var cvar95Absolute = portfolioValuation * (dailyVol * Math.Sqrt(10) * (phi95 / (1.0 - 0.95)));

        // Sharpe Ratio: (Rp - Rf) / Volatility
        var sharpeRatio = annualizedVolatility > 0
            ? (annualizedReturn - RiskFreeRateAnnual) / annualizedVolatility
            : 0;

        // Sortino Ratio: (Rp - Rf) / Downside Deviation
        var downsideVol = annualizedVolatility * 0.7; // downside risk approximation
        var sortinoRatio = downsideVol > 0
            ? (annualizedReturn - RiskFreeRateAnnual) / downsideVol
            : 0;

        // Max Drawdown approximation based on volatility
        var maxDrawdownPct = Math.Min(0.60, annualizedVolatility * 1.8);
        var beta = 0.95 + (0.1 * (assetCount % 3));

        return new RiskMetricsDto(
            PortfolioId: portfolioId,
            ValueAtRisk95: Math.Round((decimal)var95Absolute, 2),
            ValueAtRisk99: Math.Round((decimal)var99Absolute, 2),
            ConditionalVaR95: Math.Round((decimal)cvar95Absolute, 2),
            SharpeRatio: Math.Round((decimal)sharpeRatio, 4),
            SortinoRatio: Math.Round((decimal)sortinoRatio, 4),
            Beta: Math.Round((decimal)beta, 4),
            AnnualizedVolatility: Math.Round((decimal)(annualizedVolatility * 100), 2),
            MaxDrawdownPercent: Math.Round((decimal)(maxDrawdownPct * 100), 2),
            CalculatedAtUtc: DateTime.UtcNow
        );
    }

    public MonteCarloSimulationResultDto RunMonteCarloSimulation(
        Guid portfolioId,
        decimal currentValuation,
        int simulationsCount = 1000,
        int forecastDays = 30,
        decimal confidenceLevel = 0.95m)
    {
        var initialValue = (double)currentValuation;
        var dt = 1.0 / 252.0; // 1 trading day
        var mu = 0.12; // 12% drift
        var sigma = 0.22; // 22% annualized volatility
        var normalDist = new Normal(0, 1);

        var finalValues = new double[simulationsCount];

        for (var i = 0; i < simulationsCount; i++)
        {
            var currentS = initialValue;
            for (var d = 0; d < forecastDays; d++)
            {
                var z = normalDist.Sample();
                var drift = (mu - 0.5 * sigma * sigma) * dt;
                var diffusion = sigma * Math.Sqrt(dt) * z;
                currentS *= Math.Exp(drift + diffusion);
            }
            finalValues[i] = currentS;
        }

        Array.Sort(finalValues);

        var worstIndex = (int)(simulationsCount * (1.0 - (double)confidenceLevel));
        var expectedValue = finalValues.Average();
        var worstCase = finalValues[Math.Clamp(worstIndex, 0, simulationsCount - 1)];
        var bestCase = finalValues[simulationsCount - 1];

        // Percentiles 5, 25, 50, 75, 95
        var p5 = (decimal)finalValues.Percentile(5);
        var p25 = (decimal)finalValues.Percentile(25);
        var p50 = (decimal)finalValues.Percentile(50);
        var p75 = (decimal)finalValues.Percentile(75);
        var p95 = (decimal)finalValues.Percentile(95);

        return new MonteCarloSimulationResultDto(
            PortfolioId: portfolioId,
            SimulationsCount: simulationsCount,
            ForecastDays: forecastDays,
            InitialPortfolioValue: Math.Round(currentValuation, 2),
            ExpectedFinalValue: Math.Round((decimal)expectedValue, 2),
            WorstCaseValue: Math.Round((decimal)worstCase, 2),
            BestCaseValue: Math.Round((decimal)bestCase, 2),
            Percentiles5To95: new List<decimal> { p5, p25, p50, p75, p95 }
        );
    }
}
