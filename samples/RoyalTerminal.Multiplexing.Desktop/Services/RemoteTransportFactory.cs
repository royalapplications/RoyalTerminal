using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Transport.Multiplexing;

namespace RoyalTerminal.Multiplexing.Desktop.Services;

internal sealed class RemoteTransportFactory(IMultiplexingSocketConnector connector) : ITerminalTransportFactory
{
    private readonly MultiplexingTerminalTransportProvider _provider = new(connector);
    public event Action<MultiplexingTerminalTransport>? Created;
    public ITerminalTransport Create(ITerminalTransportOptions options)
    {
        if (!_provider.CanHandle(options)) { throw new ArgumentException("A remote block attachment is required.", nameof(options)); }
        MultiplexingTerminalTransport transport = (MultiplexingTerminalTransport)_provider.Create();
        Created?.Invoke(transport);
        return transport;
    }
}
