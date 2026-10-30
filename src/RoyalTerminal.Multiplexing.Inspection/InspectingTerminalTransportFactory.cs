// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Diagnostics.CodeAnalysis;
using RoyalTerminal.Terminal;

namespace RoyalTerminal.Multiplexing.Inspection;

/// <summary>Decorates transports and resolves their inspection state by the original options object's identity.</summary>
/// <remarks>Use a distinct options instance for each live block. The broker owns transport lifetime;
/// disposing a transport removes its registration. Equal-valued options remain independent.</remarks>
public sealed class InspectingTerminalTransportFactory : ITerminalTransportFactory
{
    private readonly object _gate = new();
    private readonly ITerminalTransportFactory _inner;
    private readonly TerminalInspectionOptions _options;
    private readonly Dictionary<ITerminalTransportOptions, InspectingTerminalTransport> _transports =
        new(ReferenceEqualityComparer.Instance);

    /// <summary>Creates a factory without taking ownership of the wrapped factory.</summary>
    public InspectingTerminalTransportFactory(ITerminalTransportFactory inner, TerminalInspectionOptions? options = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _options = options ?? new();
        _options.Validate();
    }

    /// <inheritdoc />
    public ITerminalTransport Create(ITerminalTransportOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options.ValidateDimensions(options.Dimensions);
        lock (_gate)
        {
            if (_transports.ContainsKey(options))
                throw new InvalidOperationException("Each live inspected transport requires a distinct options instance.");
            ITerminalTransport inner = _inner.Create(options);
            InspectingTerminalTransport transport;
            try { transport = new(inner, options.Dimensions, _options); }
            catch { inner.Dispose(); throw; }
            transport.Disposed += () => Unregister(options, transport);
            _transports.Add(options, transport);
            return transport;
        }
    }

    /// <summary>Finds a live wrapper using reference equality, for binding inspection to a broker block after creation.</summary>
    public bool TryGetInspection(ITerminalTransportOptions options,
        [NotNullWhen(true)] out InspectingTerminalTransport? inspection)
    {
        ArgumentNullException.ThrowIfNull(options);
        lock (_gate) return _transports.TryGetValue(options, out inspection);
    }

    private void Unregister(ITerminalTransportOptions options, InspectingTerminalTransport transport)
    {
        lock (_gate)
        {
            if (_transports.TryGetValue(options, out InspectingTerminalTransport? current) && ReferenceEquals(current, transport))
                _transports.Remove(options);
        }
    }
}
