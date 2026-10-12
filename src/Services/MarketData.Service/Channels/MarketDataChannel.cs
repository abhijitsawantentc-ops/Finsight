using System.Threading.Channels;
using Shared.Contracts.Dtos;

namespace MarketData.Service.Channels;

public interface IMarketDataChannel
{
    ChannelWriter<MarketQuoteDto> Writer { get; }
    ChannelReader<MarketQuoteDto> Reader { get; }
}

public class MarketDataChannel : IMarketDataChannel
{
    private readonly Channel<MarketQuoteDto> _channel;

    public MarketDataChannel(int capacity = 10000)
    {
        var options = new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false
        };

        _channel = Channel.CreateBounded<MarketQuoteDto>(options);
    }

    public ChannelWriter<MarketQuoteDto> Writer => _channel.Writer;
    public ChannelReader<MarketQuoteDto> Reader => _channel.Reader;
}
