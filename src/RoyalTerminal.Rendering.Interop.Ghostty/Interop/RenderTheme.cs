// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Rendering.Interop.Ghostty - Neutral render theme snapshot.

namespace RoyalTerminal.Rendering.Interop.Ghostty;

/// <summary>
/// Neutral renderer theme payload for interop surfaces.
/// </summary>
public readonly struct RenderTheme
{
    /// <summary>
    /// Built-in light theme.
    /// </summary>
    public static RenderTheme Light { get; } = new(
        defaultForegroundArgb: 0xFF111111u,
        defaultBackgroundArgb: 0xFFF5F5F5u,
        cursorArgb: 0xFF111111u);

    /// <summary>
    /// Built-in dark theme.
    /// </summary>
    public static RenderTheme Dark { get; } = new(
        defaultForegroundArgb: 0xFFD4D4D4u,
        defaultBackgroundArgb: 0xFF1E1E1Eu,
        cursorArgb: 0xFFD4D4D4u);

    /// <summary>
    /// Default text foreground color (ARGB).
    /// </summary>
    public uint DefaultForegroundArgb { get; }

    /// <summary>
    /// Default text background color (ARGB).
    /// </summary>
    public uint DefaultBackgroundArgb { get; }

    /// <summary>
    /// Cursor color (ARGB).
    /// </summary>
    public uint CursorArgb { get; }

    /// <summary>
    /// Creates a neutral renderer theme.
    /// </summary>
    public RenderTheme(uint defaultForegroundArgb, uint defaultBackgroundArgb, uint cursorArgb)
    {
        DefaultForegroundArgb = EnsureOpaque(defaultForegroundArgb);
        DefaultBackgroundArgb = EnsureOpaque(defaultBackgroundArgb);
        CursorArgb = EnsureOpaque(cursorArgb);
    }

    private static uint EnsureOpaque(uint argb)
    {
        return (argb & 0x00FFFFFFu) | 0xFF000000u;
    }
}
