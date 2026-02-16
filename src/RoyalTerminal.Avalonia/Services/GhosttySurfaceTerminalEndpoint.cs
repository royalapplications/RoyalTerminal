// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia - Ghostty surface adapter for terminal session services.

using RoyalTerminal.Avalonia.Terminal;
using RoyalTerminal.GhosttySharp;

namespace RoyalTerminal.Avalonia.Services;

/// <summary>
/// Adapts <see cref="GhosttySurface"/> to <see cref="ITerminalSurface"/>.
/// </summary>
public sealed class GhosttySurfaceTerminalEndpoint : ITerminalSurface
{
    /// <summary>
    /// Creates a new endpoint wrapper for a Ghostty surface.
    /// </summary>
    public GhosttySurfaceTerminalEndpoint(GhosttySurface surface)
    {
        Surface = surface ?? throw new ArgumentNullException(nameof(surface));
    }

    /// <summary>
    /// Gets the wrapped Ghostty surface.
    /// </summary>
    public GhosttySurface Surface { get; }

    /// <inheritdoc />
    public object NativeHandle => Surface;

    /// <inheritdoc />
    public void SendInput(string text)
    {
        Surface.SendText(text);
    }

    /// <inheritdoc />
    public void SendInput(ReadOnlySpan<byte> data)
    {
        Surface.SendText(data);
    }
}
