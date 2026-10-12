using Microsoft.AspNetCore.Mvc;
using RiskEngine.Service.Calculators;
using Shared.Contracts.Dtos;

namespace RiskEngine.Service.Controllers;

[ApiController]
[Route("api/[controller]")]
public class RiskController : ControllerBase
{
    private readonly IQuantitativeRiskCalculator _riskCalculator;

    public RiskController(IQuantitativeRiskCalculator riskCalculator)
    {
        _riskCalculator = riskCalculator;
    }

    [HttpPost("{portfolioId:guid}/evaluate")]
    public ActionResult<RiskMetricsDto> EvaluateRisk(
        Guid portfolioId,
        [FromQuery] decimal valuation,
        [FromBody] Dictionary<string, decimal> assetWeights)
    {
        var metrics = _riskCalculator.CalculateRiskMetrics(portfolioId, valuation, assetWeights);
        return Ok(metrics);
    }

    [HttpPost("monte-carlo")]
    public ActionResult<MonteCarloSimulationResultDto> RunSimulation(
        [FromBody] MonteCarloSimulationRequest request,
        [FromQuery] decimal currentValuation = 100000m)
    {
        var result = _riskCalculator.RunMonteCarloSimulation(
            request.PortfolioId,
            currentValuation,
            request.SimulationsCount > 0 ? request.SimulationsCount : 1000,
            request.ForecastDays > 0 ? request.ForecastDays : 30,
            request.ConfidenceLevel > 0 ? request.ConfidenceLevel : 0.95m
        );

        return Ok(result);
    }
}
