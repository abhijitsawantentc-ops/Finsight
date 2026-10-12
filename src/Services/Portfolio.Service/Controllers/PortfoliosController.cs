using MediatR;
using Microsoft.AspNetCore.Mvc;
using Portfolio.Service.Features.Portfolios.Commands;
using Portfolio.Service.Features.Portfolios.Queries;
using Shared.Contracts.Dtos;

namespace Portfolio.Service.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PortfoliosController : ControllerBase
{
    private readonly IMediator _mediator;

    public PortfoliosController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPost]
    public async Task<ActionResult<PortfolioDto>> CreatePortfolio(
        [FromHeader(Name = "X-User-Id")] Guid? headerUserId,
        [FromBody] CreatePortfolioRequest request,
        CancellationToken cancellationToken)
    {
        var userId = headerUserId ?? Guid.NewGuid();
        var command = new CreatePortfolioCommand(userId, request.Name, request.BaseCurrency, request.InitialDeposit);
        var result = await _mediator.Send(command, cancellationToken);
        return CreatedAtAction(nameof(GetPortfolioById), new { id = result.PortfolioId }, result);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PortfolioDto>> GetPortfolioById(Guid id, CancellationToken cancellationToken)
    {
        var query = new GetPortfolioByIdQuery(id);
        var result = await _mediator.Send(query, cancellationToken);
        if (result == null)
        {
            return NotFound();
        }

        return Ok(result);
    }

    [HttpGet("user/{userId:guid}")]
    public async Task<ActionResult<IReadOnlyList<PortfolioDto>>> GetUserPortfolios(Guid userId, CancellationToken cancellationToken)
    {
        var query = new GetUserPortfoliosQuery(userId);
        var result = await _mediator.Send(query, cancellationToken);
        return Ok(result);
    }

    [HttpPost("{id:guid}/trade")]
    public async Task<ActionResult<PortfolioDto>> ExecuteTrade(
        Guid id,
        [FromHeader(Name = "X-User-Id")] Guid? headerUserId,
        [FromBody] ExecuteTradeRequest request,
        CancellationToken cancellationToken)
    {
        var userId = headerUserId ?? Guid.NewGuid();
        var command = new ExecuteTradeCommand(
            id,
            userId,
            request.Symbol,
            request.Side,
            request.Type,
            request.Quantity,
            request.LimitPrice,
            request.AssetClass
        );

        var result = await _mediator.Send(command, cancellationToken);
        return Ok(result);
    }

    [HttpPost("{id:guid}/rebalance")]
    public async Task<ActionResult<PortfolioDto>> Rebalance(
        Guid id,
        [FromHeader(Name = "X-User-Id")] Guid? headerUserId,
        CancellationToken cancellationToken)
    {
        var userId = headerUserId ?? Guid.NewGuid();
        var command = new RebalancePortfolioCommand(id, userId);
        var result = await _mediator.Send(command, cancellationToken);
        return Ok(result);
    }
}
