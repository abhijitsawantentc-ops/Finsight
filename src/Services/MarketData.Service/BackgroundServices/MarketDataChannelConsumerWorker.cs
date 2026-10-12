using MarketData.Service.Channels;
using MarketData.Service.Services;
using MassTransit;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shared.Contracts.Events;

namespace MarketData.Service.BackgroundServices;

public class MarketDataChannelConsumerWorker : BackgroundService
{
    private readonly IMarketDataChannel _channel;
    private readonly IRedisMarketCacheService _cacheService;
    private readonly IBus _bus;
    private readonly ILogger<MarketDataChannelConsumerWorker> _logger;

    public MarketDataChannelConsumerWorker(
        IMarketDataChannel channel,
        IRedisMarketCacheService cacheService,
        IBus bus,
        ILogger<MarketDataChannelConsumerWorker> logger)
    {
        _channel = channel;
        _cacheService = cacheService;
        _bus = bus;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("MarketDataChannelConsumerWorker started reading from Channel...");

        while (!stoppingToken.IsCancellationRequested && await _channel.Reader.WaitToReadAsync(stoppingToken))
        {
            while (_channel.Reader.TryRead(out var quote))
            {
                try
                {
                    // 1. Cache latest quote in Redis
                    await _cacheService.SetQuoteAsync(quote, stoppingToken);

                    // 2. Broadcast tick event to MassTransit message bus
                    var tickedEvent = new MarketDataTickedEvent(
                        Symbol: quote.Symbol,
                        Price: quote.Price,
                        Bid: quote.Bid,
                        Ask: quote.Ask,
                        Volume: quote.Volume,
                        Currency: quote.Currency,
                        TimestampUtc: quote.TimestampUtc
                    );

                    await _bus.Publish(tickedEvent, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to publish or cache market tick for {Symbol}", quote.Symbol);
                }
            }
        }

        _logger.LogInformation("MarketDataChannelConsumerWorker stopping.");
    }
}
