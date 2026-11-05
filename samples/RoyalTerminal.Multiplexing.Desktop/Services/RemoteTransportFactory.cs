using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Transport.Multiplexing;
namespace RoyalTerminal.Multiplexing.Desktop.Services;
internal sealed class RemoteTransportFactory(IMultiplexingSocketConnector connector) : ITerminalTransportFactory
{
    private MultiplexingTerminalTransportProvider _provider = new(connector);
    private bool _started;
    public event Action<MultiplexingTerminalTransport>? Created;
    internal void ConfigureBeforeFirstAttach(IMultiplexingSocketConnector value)
    {
        if (_started) throw new InvalidOperationException("Do not change transports under an existing terminal.");
        _provider = new(value);
    }
    public ITerminalTransport Create(ITerminalTransportOptions options)
    {
        if (!_provider.CanHandle(options)) throw new ArgumentException("A remote block attachment is required.");
        _started = true; var transport = (MultiplexingTerminalTransport)_provider.Create(); Created?.Invoke(transport); return transport;
    }
}
