using System.Text.Json;
using FluentAssertions;
using Shared.Contracts.Enums;
using Shared.Contracts.Events;
using Xunit;

namespace Shared.Contracts.Tests;

public class ContractSerializationTests
{
    [Fact]
    public void MarketDataTickedEvent_ShouldSerializeAndDeserializeAccurately()
    {
        // Arrange
        var originalEvent = new MarketDataTickedEvent(
            Symbol: "NVDA",
            Price: 135.50m,
            Bid: 135.48m,
            Ask: 135.52m,
            Volume: 1250000,
            Currency: "USD",
            TimestampUtc: DateTime.UtcNow
        );

        // Act
        var json = JsonSerializer.Serialize(originalEvent);
        var deserialized = JsonSerializer.Deserialize<MarketDataTickedEvent>(json);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Symbol.Should().Be("NVDA");
        deserialized.Price.Should().Be(135.50m);
        deserialized.Bid.Should().Be(135.48m);
        deserialized.Ask.Should().Be(135.52m);
        deserialized.Volume.Should().Be(1250000);
        deserialized.Currency.Should().Be("USD");
    }

    [Fact]
    public void OrderExecutedEvent_ShouldMaintainRecordImmutabilityAndEquality()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var portfolioId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var timestamp = DateTime.UtcNow;

        var order1 = new OrderExecutedEvent(
            orderId, portfolioId, userId, "AAPL", OrderSide.Buy, OrderType.Market, 100m, 230.50m, 1.50m, timestamp
        );

        var order2 = new OrderExecutedEvent(
            orderId, portfolioId, userId, "AAPL", OrderSide.Buy, OrderType.Market, 100m, 230.50m, 1.50m, timestamp
        );

        // Assert
        order1.Should().Be(order2);
        (order1 == order2).Should().BeTrue();
    }

    [Fact]
    public void PortfolioRebalancedEvent_ShouldCorrectlySerializeAssetWeights()
    {
        // Arrange
        var weights = new Dictionary<string, decimal>
        {
            { "AAPL", 0.40m },
            { "MSFT", 0.35m },
            { "GOOGL", 0.25m }
        };

        var quantities = new Dictionary<string, decimal>
        {
            { "AAPL", 150m },
            { "MSFT", 80m },
            { "GOOGL", 120m }
        };

        var evt = new PortfolioRebalancedEvent(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Alpha Growth Strategy",
            500000m,
            weights,
            quantities,
            DateTime.UtcNow
        );

        // Act
        var json = JsonSerializer.Serialize(evt);
        var deserialized = JsonSerializer.Deserialize<PortfolioRebalancedEvent>(json);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.TotalValuation.Should().Be(500000m);
        deserialized.AssetWeights["AAPL"].Should().Be(0.40m);
        deserialized.AssetWeights["MSFT"].Should().Be(0.35m);
        deserialized.AssetWeights["GOOGL"].Should().Be(0.25m);
        deserialized.PositionQuantities["AAPL"].Should().Be(150m);
    }

    [Fact]
    public void RiskThresholdExceededEvent_ShouldPreserveCriticalSeverity()
    {
        // Arrange
        var alert = new RiskThresholdExceededEvent(
            PortfolioId: Guid.NewGuid(),
            UserId: Guid.NewGuid(),
            PortfolioName: "Tech Momentum",
            MetricType: RiskMetricType.MaxDrawdown,
            CurrentValue: 28.5m,
            ThresholdValue: 25.0m,
            Severity: RiskSeverity.Critical,
            ViolationDetails: "Drawdown breached critical threshold of 25%",
            TriggeredAtUtc: DateTime.UtcNow
        );

        // Act
        var json = JsonSerializer.Serialize(alert);
        var deserialized = JsonSerializer.Deserialize<RiskThresholdExceededEvent>(json);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Severity.Should().Be(RiskSeverity.Critical);
        deserialized.MetricType.Should().Be(RiskMetricType.MaxDrawdown);
        deserialized.CurrentValue.Should().Be(28.5m);
    }
}
