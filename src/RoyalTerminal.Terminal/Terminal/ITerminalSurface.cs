// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia - Surface abstraction used by terminal session services.

namespace RoyalTerminal.Avalonia.Terminal;

/// <summary>
/// Opaque terminal surface endpoint used by session services.
/// Implementations can wrap backend-specific surfaces (e.g. Ghostty).
/// </summary>
public interface ITerminalSurface
{
    /// <summary>
    /// Gets the backend-native surface object.
    /// </summary>
    object NativeHandle { get; }

    /// <summary>
    /// Sends UTF-16 text input to the surface.
    /// </summary>
    void SendInput(string text);

    /// <summary>
    /// Sends UTF-8 byte input to the surface.
    /// </summary>
    void SendInput(ReadOnlySpan<byte> data);
}
