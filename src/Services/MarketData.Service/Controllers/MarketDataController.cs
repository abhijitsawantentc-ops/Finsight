using MarketData.Service.Services;
using Microsoft.AspNetCore.Mvc;
using Shared.Contracts.Dtos;

namespace MarketData.Service.Controllers;

[ApiController]
[Route("api/market-data")]
public class MarketDataController : ControllerBase
{
    private readonly IRedisMarketCacheService _cacheService;

    public MarketDataController(IRedisMarketCacheService cacheService)
    {
        _cacheService = cacheService;
    }

    [HttpGet("{symbol}/quote")]
    public async Task<ActionResult<MarketQuoteDto>> GetQuote(string symbol, CancellationToken cancellationToken)
    {
        var quote = await _cacheService.GetQuoteAsync(symbol, cancellationToken);
        if (quote == null)
        {
            return NotFound(new { message = $"No market quote currently available for symbol: {symbol}" });
        }

        return Ok(quote);
    }

    [HttpGet("tickers")]
    public async Task<ActionResult<IReadOnlyList<string>>> GetAvailableTickers(CancellationToken cancellationToken)
    {
        var tickers = await _cacheService.GetAllSymbolsAsync(cancellationToken);
        return Ok(tickers);
    }
}
