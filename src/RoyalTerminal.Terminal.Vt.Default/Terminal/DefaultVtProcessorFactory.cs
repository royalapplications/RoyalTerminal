// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia — Default VT processor factory.

using RoyalTerminal.Avalonia.Rendering;

namespace RoyalTerminal.Avalonia.Terminal;

/// <summary>
/// Default factory that creates the best available VT processor implementation.
/// </summary>
public sealed class DefaultVtProcessorFactory : IVtProcessorFactory
{
    private readonly INativeVtProcessorProvider[] _nativeProviders;

    /// <summary>
    /// Creates a default factory with no native providers.
    /// </summary>
    public DefaultVtProcessorFactory()
        : this(Array.Empty<INativeVtProcessorProvider>())
    {
    }

    /// <summary>
    /// Creates a default factory with explicit native providers.
    /// </summary>
    public DefaultVtProcessorFactory(IEnumerable<INativeVtProcessorProvider> nativeProviders)
    {
        ArgumentNullException.ThrowIfNull(nativeProviders);
        _nativeProviders = nativeProviders.ToArray();
    }

    /// <inheritdoc />
    public IVtProcessor Create(TerminalScreen screen, bool? useNativeVtProcessor)
    {
        if (useNativeVtProcessor == false)
        {
            return new BasicVtProcessor(screen);
        }

        for (int i = 0; i < _nativeProviders.Length; i++)
        {
            INativeVtProcessorProvider provider = _nativeProviders[i];
            if (!provider.IsAvailable)
            {
                continue;
            }

            try
            {
                return provider.Create(screen);
            }
            catch when (useNativeVtProcessor != true)
            {
                // Fall back to managed VT processor when native is optional.
            }
        }

        if (useNativeVtProcessor == true)
        {
            throw new InvalidOperationException(
                "Native VT processor was requested but no native VT provider is available.");
        }

        return new BasicVtProcessor(screen);
    }
}
