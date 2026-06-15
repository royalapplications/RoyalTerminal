// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia - Native VT provider backed by Ghostty terminal C API.

using RoyalTerminal.Avalonia.Rendering;

namespace RoyalTerminal.Terminal;

/// <summary>
/// Native VT provider that creates <see cref="GhosttyVtProcessor"/> instances.
/// </summary>
public sealed class GhosttyVtProcessorProvider : INativeVtProcessorProvider
{
    /// <summary>
    /// Preloads the native Ghostty VT implementation and its first-use processor state.
    /// </summary>
    public static void Prewarm()
    {
        var screen = new TerminalScreen(columns: 80, viewportRows: 24, scrollbackLimit: 1);
        using var processor = new GhosttyVtProcessor(screen);
        processor.Process(ReadOnlySpan<byte>.Empty);
    }

    /// <inheritdoc />
    public bool IsAvailable => GhosttyVtProcessor.IsAvailable();

    /// <inheritdoc />
    public IVtProcessor Create(TerminalScreen screen)
    {
        return new GhosttyVtProcessor(screen);
    }
}
