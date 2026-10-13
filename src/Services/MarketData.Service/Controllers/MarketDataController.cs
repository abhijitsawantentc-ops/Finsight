using MarketData.Service.Exceptions;
using MarketData.Service.Services.Caching;
using MarketData.Service.Services.Orchestration;
using Microsoft.AspNetCore.Mvc;
using Shared.Contracts.Dtos;

namespace MarketData.Service.Controllers;

[ApiController]
[Route("api/market-data")]
public class MarketDataController : ControllerBase
{
    private readonly IMarketDataOrchestrator _orchestrator;
    private readonly ICacheService _cacheService;

    public MarketDataController(
        IMarketDataOrchestrator orchestrator,
        ICacheService cacheService)
    {
        _orchestrator = orchestrator;
        _cacheService = cacheService;
    }

    [HttpGet("{symbol}/quote")]
    public async Task<ActionResult<MarketQuoteDto>> GetQuote(string symbol, CancellationToken cancellationToken)
    {
        try
        {
            var quote = await _orchestrator.GetQuoteAsync(symbol, cancellationToken);
            return Ok(quote);
        }
        catch (MarketDataUnavailableException ex)
        {
            return StatusCode(503, new
            {
                message = ex.Message,
                symbol = ex.Symbol,
                errors = ex.ProviderErrors
            });
        }
    }

    [HttpGet("{symbol}/stale")]
    public async Task<ActionResult<MarketQuoteDto>> GetStaleQuote(string symbol, CancellationToken cancellationToken)
    {
        var staleQuote = await _cacheService.GetStaleQuoteAsync(symbol, cancellationToken);
        if (staleQuote == null)
        {
            return NotFound(new { message = $"No stale fallback quote available for symbol: {symbol}" });
        }

        return Ok(staleQuote);
    }

    [HttpGet("tickers")]
    public async Task<ActionResult<IReadOnlyList<string>>> GetAvailableTickers(CancellationToken cancellationToken)
    {
        var tickers = await _cacheService.GetAllSymbolsAsync(cancellationToken);
        return Ok(tickers);
    }

    [HttpGet("status")]
    public async Task<ActionResult<MarketDataOrchestratorStatus>> GetStatus(CancellationToken cancellationToken)
    {
        var status = await _orchestrator.GetStatusAsync(cancellationToken);
        return Ok(status);
    }
}
