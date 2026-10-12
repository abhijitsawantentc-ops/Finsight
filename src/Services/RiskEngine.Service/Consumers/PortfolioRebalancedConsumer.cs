using MassTransit;
using Microsoft.Extensions.Logging;
using RiskEngine.Service.Calculators;
using Shared.Contracts.Enums;
using Shared.Contracts.Events;

namespace RiskEngine.Service.Consumers;

public class PortfolioRebalancedConsumer : IConsumer<PortfolioRebalancedEvent>
{
    private readonly IQuantitativeRiskCalculator _riskCalculator;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogger<PortfolioRebalancedConsumer> _logger;

    public PortfolioRebalancedConsumer(
        IQuantitativeRiskCalculator riskCalculator,
        IPublishEndpoint publishEndpoint,
        ILogger<PortfolioRebalancedConsumer> logger)
    {
        _riskCalculator = riskCalculator;
        _publishEndpoint = publishEndpoint;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<PortfolioRebalancedEvent> context)
    {
        var message = context.Message;
        _logger.LogInformation("Processing risk calculation for rebalanced portfolio {PortfolioId} ({PortfolioName})",
            message.PortfolioId, message.PortfolioName);

        var metrics = _riskCalculator.CalculateRiskMetrics(
            message.PortfolioId,
            message.TotalValuation,
            message.AssetWeights
        );

        // 1. Publish calculated risk metrics
        var calculatedEvent = new PortfolioRiskCalculatedEvent(
            PortfolioId: message.PortfolioId,
            UserId: message.UserId,
            TotalValuation: message.TotalValuation,
            ValueAtRisk95: metrics.ValueAtRisk95,
            ValueAtRisk99: metrics.ValueAtRisk99,
            ConditionalVaR95: metrics.ConditionalVaR95,
            SharpeRatio: metrics.SharpeRatio,
            SortinoRatio: metrics.SortinoRatio,
            Beta: metrics.Beta,
            AnnualizedVolatility: metrics.AnnualizedVolatility,
            MaxDrawdownPercent: metrics.MaxDrawdownPercent,
            CalculatedAtUtc: DateTime.UtcNow
        );

        await _publishEndpoint.Publish(calculatedEvent, context.CancellationToken);

        // 2. Check risk threshold limits (e.g. MaxDrawdown > 25% or Volatility > 30%)
        if (metrics.MaxDrawdownPercent > 25.0m)
        {
            var riskAlert = new RiskThresholdExceededEvent(
                PortfolioId: message.PortfolioId,
                UserId: message.UserId,
                PortfolioName: message.PortfolioName,
                MetricType: RiskMetricType.MaxDrawdown,
                CurrentValue: metrics.MaxDrawdownPercent,
                ThresholdValue: 25.0m,
                Severity: RiskSeverity.Critical,
                ViolationDetails: $"Maximum Drawdown projected at {metrics.MaxDrawdownPercent}%, exceeding upper risk limit of 25.0%.",
                TriggeredAtUtc: DateTime.UtcNow
            );

            await _publishEndpoint.Publish(riskAlert, context.CancellationToken);
            _logger.LogWarning("Risk limit exceeded for Portfolio {PortfolioId}: MDD={MDD}%",
                message.PortfolioId, metrics.MaxDrawdownPercent);
        }
    }
}
