// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Terminal - Composite transport factory.

namespace RoyalTerminal.Terminal;

/// <summary>
/// Default transport factory that resolves providers by transport id and option compatibility.
/// </summary>
public sealed class CompositeTerminalTransportFactory : ITerminalTransportFactory
{
    private readonly IReadOnlyList<ITerminalTransportProvider> _providers;

    /// <summary>
    /// Initializes a composite factory from provider instances.
    /// </summary>
    public CompositeTerminalTransportFactory(IReadOnlyList<ITerminalTransportProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);

        if (providers.Count == 0)
        {
            throw new ArgumentException("At least one transport provider must be registered.", nameof(providers));
        }

        _providers = providers;
    }

    /// <inheritdoc />
    public ITerminalTransport Create(ITerminalTransportOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        for (int i = 0; i < _providers.Count; i++)
        {
            ITerminalTransportProvider provider = _providers[i];
            if (!string.Equals(provider.TransportId, options.TransportId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!provider.CanHandle(options))
            {
                continue;
            }

            return provider.Create();
        }

        throw new InvalidOperationException(
            $"No terminal transport provider can handle transport id '{options.TransportId}'.");
    }
}
