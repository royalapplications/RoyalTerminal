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
    /// <inheritdoc />
    public IVtProcessor Create(TerminalScreen screen, bool? useNativeVtProcessor)
    {
        if (useNativeVtProcessor == false)
        {
            return new BasicVtProcessor(screen);
        }

        if (GhosttyVtProcessor.IsAvailable())
        {
            try
            {
                return new GhosttyVtProcessor(screen);
            }
            catch when (useNativeVtProcessor != true)
            {
                // Fall back to managed VT processor when native is optional.
            }
        }
        else if (useNativeVtProcessor == true)
        {
            throw new InvalidOperationException(
                "Native VT processor was requested but libghostty-terminal is not available.");
        }

        return new BasicVtProcessor(screen);
    }
}
