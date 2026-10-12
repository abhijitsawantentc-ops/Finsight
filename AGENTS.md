# Antigravity Project Workspace Rules: Algorithmic Portfolio Tracker & Risk Engine

## Architecture Guidelines
- **Solution Type**: .NET 9 Event-Driven Clean Architecture Microservices.
- **Solution File**: `PortfolioTracker.sln`
- **Building Blocks**:
  - `Shared.Contracts`: Immutable record events (`MarketDataTickedEvent`, `PortfolioRebalancedEvent`, `PortfolioRiskCalculatedEvent`, `RiskThresholdExceededEvent`, etc.) and domain DTOs.
  - `Shared.Infrastructure`: Cross-cutting concerns (OpenTelemetry instrumentation, MassTransit RabbitMQ configuration, Serilog Seq logging, CorrelationId and Global Exception middleware).
- **Services**:
  - `ApiGateway`: YARP Reverse proxy routing to internal services on port `8080`.
  - `Identity.Service`: ASP.NET Identity, JWT token generation, PostgreSQL EF Core.
  - `MarketData.Service`: High-frequency tick ingestion via bounded `System.Threading.Channels` and Redis cache.
  - `Portfolio.Service`: CQRS with MediatR, transactional EF Core Outbox pattern.
  - `RiskEngine.Service`: High-performance quantitative computations via `MathNet.Numerics` (VaR, CVaR, Sharpe, Monte Carlo).
  - `Notification.Service`: SignalR hub for real-time WebSocket client streaming.

## Coding Standards
- Target Framework: `net9.0`, C# 13 with Nullable Reference Types enabled.
- Always use asynchronous APIs (`async`/`await`) with `CancellationToken`.
- Maintain transactional consistency with Outbox pattern for inter-service messaging.
