// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Rendering.Skia - Terminal shader frame data.

using RoyalTerminal.Terminal;
using SkiaSharp;

namespace RoyalTerminal.Avalonia.Rendering;

/// <summary>
/// Per-frame values supplied to terminal framebuffer shaders.
/// </summary>
public readonly record struct TerminalShaderFrameContext
{
    /// <summary>
    /// Initializes a new shader frame context.
    /// </summary>
    public TerminalShaderFrameContext(
        int width,
        int height,
        float time,
        float timeDelta,
        int frame,
        float scale,
        SKColor backgroundColor,
        SKColor foregroundColor,
        SKColor cursorColor,
        SKRect cursorRect,
        CursorStyle cursorStyle,
        bool cursorVisible)
    {
        Width = Math.Max(1, width);
        Height = Math.Max(1, height);
        Time = Math.Max(0f, time);
        TimeDelta = Math.Max(0f, timeDelta);
        Frame = Math.Max(0, frame);
        Scale = scale > 0f && float.IsFinite(scale) ? scale : 1f;
        BackgroundColor = backgroundColor;
        ForegroundColor = foregroundColor;
        CursorColor = cursorColor;
        CursorRect = cursorRect;
        CursorStyle = cursorStyle;
        CursorVisible = cursorVisible;
    }

    /// <summary>Gets the framebuffer width in pixels.</summary>
    public int Width { get; }

    /// <summary>Gets the framebuffer height in pixels.</summary>
    public int Height { get; }

    /// <summary>Gets elapsed shader time in seconds.</summary>
    public float Time { get; }

    /// <summary>Gets elapsed time since the previous shader frame in seconds.</summary>
    public float TimeDelta { get; }

    /// <summary>Gets the shader frame index.</summary>
    public int Frame { get; }

    /// <summary>Gets the UI scale factor.</summary>
    public float Scale { get; }

    /// <summary>Gets the terminal background color.</summary>
    public SKColor BackgroundColor { get; }

    /// <summary>Gets the terminal foreground color.</summary>
    public SKColor ForegroundColor { get; }

    /// <summary>Gets the current cursor color.</summary>
    public SKColor CursorColor { get; }

    /// <summary>Gets the current cursor rectangle in framebuffer coordinates.</summary>
    public SKRect CursorRect { get; }

    /// <summary>Gets the current cursor style.</summary>
    public CursorStyle CursorStyle { get; }

    /// <summary>Gets whether the cursor is visible.</summary>
    public bool CursorVisible { get; }
}
