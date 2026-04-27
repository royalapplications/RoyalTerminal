// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Terminal — Default VT processor factory.

using RoyalTerminal.Avalonia.Rendering;

namespace RoyalTerminal.Terminal;

/// <summary>
/// Default factory that creates the best available VT processor implementation.
/// </summary>
public sealed class DefaultVtProcessorFactory : IVtProcessorFactory
{
    private readonly INativeVtProcessorProvider[] _nativeProviders;
    private readonly BasicVtProcessorOptions _managedOptions;

    /// <summary>
    /// Creates a default factory with no native providers.
    /// </summary>
    public DefaultVtProcessorFactory()
        : this(Array.Empty<INativeVtProcessorProvider>(), null)
    {
    }

    /// <summary>
    /// Creates a default factory with managed VT options and no native providers.
    /// </summary>
    public DefaultVtProcessorFactory(BasicVtProcessorOptions managedOptions)
        : this(Array.Empty<INativeVtProcessorProvider>(), managedOptions)
    {
    }

    /// <summary>
    /// Creates a default factory with explicit native providers.
    /// </summary>
    public DefaultVtProcessorFactory(IEnumerable<INativeVtProcessorProvider> nativeProviders)
        : this(nativeProviders, null)
    {
    }

    /// <summary>
    /// Creates a default factory with explicit native providers and managed VT options.
    /// </summary>
    public DefaultVtProcessorFactory(
        IEnumerable<INativeVtProcessorProvider> nativeProviders,
        BasicVtProcessorOptions? managedOptions)
    {
        ArgumentNullException.ThrowIfNull(nativeProviders);
        _nativeProviders = nativeProviders.ToArray();
        _managedOptions = managedOptions ?? BasicVtProcessorOptions.Default;
    }

    /// <inheritdoc />
    public IVtProcessor Create(TerminalScreen screen, VtProcessorPreference preference)
    {
        if (preference == VtProcessorPreference.Managed)
        {
            return new BasicVtProcessor(screen, _managedOptions);
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
            catch when (preference != VtProcessorPreference.Native)
            {
                // Fall back to managed VT processor when native is optional.
            }
        }

        if (preference == VtProcessorPreference.Native)
        {
            throw new InvalidOperationException(
                "Native VT processor was requested but no native VT provider is available.");
        }

        return new BasicVtProcessor(screen, _managedOptions);
    }
}
