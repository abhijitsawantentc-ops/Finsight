using MarketData.Service.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shared.Contracts.Dtos;

namespace MarketData.Service.BackgroundServices;

public class MarketDataIngestionWorker : BackgroundService
{
    private readonly IMarketDataChannel _channel;
    private readonly ILogger<MarketDataIngestionWorker> _logger;

    private readonly Dictionary<string, decimal> _tickers = new()
    {
        { "AAPL", 228.50m },
        { "MSFT", 445.20m },
        { "GOOGL", 182.10m },
        { "AMZN", 195.40m },
        { "NVDA", 132.80m },
        { "TSLA", 252.60m },
        { "BTC-USD", 67500.00m },
        { "ETH-USD", 3450.00m }
    };

    private readonly Random _random = new();

    public MarketDataIngestionWorker(
        IMarketDataChannel channel,
        ILogger<MarketDataIngestionWorker> logger)
    {
        _channel = channel;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("MarketDataIngestionWorker started. Generating real-time market ticks...");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                foreach (var symbol in _tickers.Keys.ToList())
                {
                    var currentPrice = _tickers[symbol];

                    // Simulate slight brownian motion percentage return (-0.4% to +0.4%)
                    var percentageChange = (decimal)(_random.NextDouble() * 0.008 - 0.004);
                    var newPrice = Math.Round(currentPrice * (1 + percentageChange), 2);
                    _tickers[symbol] = newPrice;

                    var spread = Math.Max(0.01m, Math.Round(newPrice * 0.0005m, 2));
                    var bid = Math.Round(newPrice - (spread / 2m), 2);
                    var ask = Math.Round(newPrice + (spread / 2m), 2);
                    var volume = (long)_random.Next(500, 25000);

                    var quote = new MarketQuoteDto(
                        Symbol: symbol,
                        Price: newPrice,
                        Bid: bid,
                        Ask: ask,
                        Volume: volume,
                        Currency: "USD",
                        TimestampUtc: DateTime.UtcNow
                    );

                    await _channel.Writer.WriteAsync(quote, stoppingToken);
                }

                // Ingest interval: 500ms
                await Task.Delay(500, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception encountered in MarketDataIngestionWorker");
                await Task.Delay(1000, stoppingToken);
            }
        }

        _logger.LogInformation("MarketDataIngestionWorker shutting down.");
    }
}
