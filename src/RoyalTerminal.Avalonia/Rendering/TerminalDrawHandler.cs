// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia - Avalonia composition draw handler.

using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.Composition;
using Avalonia.Skia;
using System.Diagnostics;
using RoyalTerminal.Shaders;
using SkiaSharp;

namespace RoyalTerminal.Avalonia.Rendering;

/// <summary>
/// Custom visual draw handler for rendering the terminal using SkiaSharp
/// within Avalonia's composition system.
/// Maintains a retained terminal framebuffer so composition/shader frames can
/// reuse previous terminal pixels and redraw only dirty rows.
/// </summary>
public class TerminalDrawHandler : CompositionCustomVisualHandler
{
    private SkiaTerminalRenderer? _renderer;
    private TerminalScreen? _screen;
    private TerminalShaderPostProcessor? _shaderPostProcessor;
    private SKSurface? _terminalSurface;
    private SKImageInfo _terminalSurfaceInfo;
    private bool _cachedFrameValid;
    private bool _terminalFrameDirty = true;
    private bool _forceFullRedrawRequested = true;
    private bool _invalidateViewportRequested = true;
    private bool _shaderAnimationEnabled;
    private bool _pendingRender;
    private long _shaderStartTimestamp;
    private long _lastShaderTimestamp;
    private int _shaderFrame;
    private TerminalCursorRenderSnapshot _lastCursorSnapshot;
    private readonly SKPaint _clearPaint = new()
    {
        IsAntialias = false,
        Style = SKPaintStyle.Fill,
    };

    /// <summary>
    /// Message types for communicating with the handler from the UI thread.
    /// </summary>
    public record UpdateMessage(SkiaTerminalRenderer Renderer, TerminalScreen Screen);
    public readonly record struct InvalidateMessage(bool FullRedraw = false, bool DirtyRowsOnly = false);
    public readonly record struct ResizeMessage();
    public readonly record struct ShaderStateMessage(
        IReadOnlyList<TerminalShaderSource>? Sources,
        bool AnimationEnabled);

    public override void OnMessage(object message)
    {
        switch (message)
        {
            case UpdateMessage update:
                _renderer = update.Renderer;
                _screen = update.Screen;
                RequestTerminalFrame(fullRedraw: true, invalidateViewport: true);
                break;

            case InvalidateMessage invalidate:
                RequestTerminalFrame(
                    invalidate.FullRedraw,
                    invalidateViewport: !invalidate.DirtyRowsOnly);
                break;

            case ResizeMessage:
                ResetCachedFrame();
                RequestTerminalFrame(fullRedraw: true, invalidateViewport: true);
                break;

            case ShaderStateMessage shaderState:
                SetShaderState(
                    shaderState.Sources,
                    shaderState.AnimationEnabled);
                RequestRender();
                break;
        }
    }

    public override void OnAnimationFrameUpdate()
    {
        if (_pendingRender)
            Invalidate();
    }

    public override void OnRender(ImmediateDrawingContext context)
    {
        try
        {
            var renderer = _renderer;
            var screen = _screen;
            if (renderer is null || screen is null)
            {
                return;
            }

            var leaseFeature = context.TryGetFeature(typeof(ISkiaSharpApiLeaseFeature)) as ISkiaSharpApiLeaseFeature;
            if (leaseFeature is null)
            {
                return;
            }

            using var lease = leaseFeature.Lease();
            var canvas = lease.SkCanvas;
            if (canvas is null)
            {
                return;
            }

            SKRect clip = canvas.LocalClipBounds;
            int width = Math.Max(1, (int)Math.Ceiling(clip.Width));
            int height = Math.Max(1, (int)Math.Ceiling(clip.Height));
            SKImage? terminalFrame = null;
            SKColor background = default;

            lock (screen.SyncRoot)
            {
                background = new SKColor(screen.DefaultBackground);
                if (!EnsureTerminalSurface(width, height))
                {
                    canvas.Clear(background);
                    renderer.RenderFull(canvas, screen);
                    return;
                }

                bool fullRedraw = !_cachedFrameValid || _forceFullRedrawRequested;
                if (_invalidateViewportRequested)
                {
                    screen.InvalidateViewport();
                }

                bool cursorChanged = MarkCursorRowsDirtyIfNeeded(renderer, screen);
                bool hasDirtyRows = fullRedraw || _terminalFrameDirty || cursorChanged || HasDirtyViewportRows(screen);
                if (hasDirtyRows)
                {
                    SKCanvas terminalCanvas = _terminalSurface!.Canvas;
                    if (fullRedraw)
                    {
                        terminalCanvas.Clear(background);
                    }
                    else
                    {
                        ClearDirtyViewportRows(terminalCanvas, renderer, screen, background, width);
                    }

                    renderer.Render(terminalCanvas, screen, forceFullRedraw: fullRedraw);
                    terminalCanvas.Flush();
                    _cachedFrameValid = true;
                    _terminalFrameDirty = false;
                    _forceFullRedrawRequested = false;
                    _invalidateViewportRequested = false;
                }

                _lastCursorSnapshot = TerminalCursorRenderSnapshot.From(renderer);
                terminalFrame = _terminalSurface!.Snapshot();
            }

            if (terminalFrame is null)
            {
                canvas.Clear(background);
                return;
            }

            try
            {
                canvas.Save();
                TerminalShaderPostProcessor? shaderPostProcessor = _shaderPostProcessor;
                if (shaderPostProcessor is not null && shaderPostProcessor.HasShaders)
                {
                    RenderWithShaders(canvas, renderer, screen, shaderPostProcessor, terminalFrame, width, height);
                }
                else
                {
                    SKRect destinationRect = new(0, 0, width, height);
                    canvas.Clear(background);
                    canvas.DrawImage(terminalFrame, destinationRect);
                }
            }
            finally
            {
                canvas.Restore();
                terminalFrame.Dispose();
            }
        }
        catch
        {
            // Swallow render errors during shutdown/teardown
        }
        finally
        {
            _pendingRender = ShouldContinueShaderAnimation();
            if (_pendingRender)
            {
                RegisterForNextAnimationFrameUpdate();
            }
        }
    }

    private void RequestTerminalFrame(bool fullRedraw, bool invalidateViewport)
    {
        _terminalFrameDirty |= fullRedraw || invalidateViewport;
        _forceFullRedrawRequested |= fullRedraw;
        _invalidateViewportRequested |= invalidateViewport;
        RequestRender();
    }

    private void RequestRender()
    {
        _pendingRender = true;
        RegisterForNextAnimationFrameUpdate();
    }

    private void ResetCachedFrame()
    {
        _terminalSurface?.Dispose();
        _terminalSurface = null;
        _terminalSurfaceInfo = default;
        _cachedFrameValid = false;
        _terminalFrameDirty = true;
        _forceFullRedrawRequested = true;
        _invalidateViewportRequested = true;
    }

    private bool EnsureTerminalSurface(int width, int height)
    {
        if (_terminalSurface is not null &&
            _terminalSurfaceInfo.Width == width &&
            _terminalSurfaceInfo.Height == height)
        {
            return true;
        }

        _terminalSurface?.Dispose();
        _terminalSurfaceInfo = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        _terminalSurface = SKSurface.Create(_terminalSurfaceInfo);
        _cachedFrameValid = false;
        _terminalFrameDirty = true;
        _forceFullRedrawRequested = true;
        _invalidateViewportRequested = true;
        return _terminalSurface is not null;
    }

    private bool MarkCursorRowsDirtyIfNeeded(SkiaTerminalRenderer renderer, TerminalScreen screen)
    {
        TerminalCursorRenderSnapshot current = TerminalCursorRenderSnapshot.From(renderer);
        if (!_cachedFrameValid || current.Equals(_lastCursorSnapshot))
        {
            return false;
        }

        bool dirtied = false;
        if (_lastCursorSnapshot.Visible)
        {
            dirtied |= TryDirtyViewportRow(screen, _lastCursorSnapshot.Row);
        }

        if (current.Visible)
        {
            dirtied |= TryDirtyViewportRow(screen, current.Row);
        }

        return dirtied;
    }

    private static bool TryDirtyViewportRow(TerminalScreen screen, int row)
    {
        if ((uint)row >= (uint)screen.ViewportRows)
        {
            return false;
        }

        screen.GetViewportRow(row).IsDirty = true;
        return true;
    }

    private static bool HasDirtyViewportRows(TerminalScreen screen)
    {
        for (int row = 0; row < screen.ViewportRows; row++)
        {
            if (screen.GetViewportRow(row).IsDirty)
            {
                return true;
            }
        }

        return false;
    }

    private void ClearDirtyViewportRows(
        SKCanvas canvas,
        SkiaTerminalRenderer renderer,
        TerminalScreen screen,
        SKColor background,
        int canvasWidth)
    {
        _clearPaint.Color = background;
        float rowWidth = Math.Max(canvasWidth, screen.Columns * renderer.CellWidth);
        float rowHeight = Math.Max(1f, renderer.CellHeight);
        for (int row = 0; row < screen.ViewportRows; row++)
        {
            if (!screen.GetViewportRow(row).IsDirty)
            {
                continue;
            }

            float y = row * renderer.CellHeight;
            canvas.DrawRect(0, y, rowWidth, rowHeight, _clearPaint);
        }
    }

    private void SetShaderState(
        IReadOnlyList<TerminalShaderSource>? sources,
        bool animationEnabled)
    {
        _shaderPostProcessor?.Dispose();
        _shaderPostProcessor = TerminalShaderPostProcessor.Create(sources);
        _shaderAnimationEnabled = animationEnabled;
        _shaderStartTimestamp = 0;
        _lastShaderTimestamp = 0;
        _shaderFrame = 0;
    }

    private bool ShouldContinueShaderAnimation()
    {
        TerminalShaderPostProcessor? shaderPostProcessor = _shaderPostProcessor;
        return _shaderAnimationEnabled &&
               shaderPostProcessor is not null &&
               shaderPostProcessor.HasShaders &&
               shaderPostProcessor.RequiresContinuousAnimation;
    }

    private void RenderWithShaders(
        SKCanvas destinationCanvas,
        SkiaTerminalRenderer renderer,
        TerminalScreen screen,
        TerminalShaderPostProcessor shaderPostProcessor,
        SKImage terminalFrame,
        int width,
        int height)
    {
        TerminalShaderFrameContext frameContext = CreateFrameContext(renderer, screen, width, height);
        SKRect destinationRect = new(0, 0, width, height);
        if (!shaderPostProcessor.TryApply(destinationCanvas, terminalFrame, destinationRect, frameContext))
        {
            destinationCanvas.Clear(new SKColor(screen.DefaultBackground));
            destinationCanvas.DrawImage(terminalFrame, destinationRect);
        }
    }

    private TerminalShaderFrameContext CreateFrameContext(
        SkiaTerminalRenderer renderer,
        TerminalScreen screen,
        int width,
        int height)
    {
        long now = Stopwatch.GetTimestamp();
        if (_shaderStartTimestamp == 0)
        {
            _shaderStartTimestamp = now;
            _lastShaderTimestamp = now;
        }

        float time = (float)Stopwatch.GetElapsedTime(_shaderStartTimestamp, now).TotalSeconds;
        float timeDelta = (float)Stopwatch.GetElapsedTime(_lastShaderTimestamp, now).TotalSeconds;
        _lastShaderTimestamp = now;

        SKRect cursorRect = GetCursorRect(renderer);
        TerminalShaderFrameContext context = new(
            width,
            height,
            time,
            timeDelta,
            _shaderFrame,
            scale: 1f,
            new SKColor(screen.DefaultBackground),
            new SKColor(screen.DefaultForeground),
            renderer.CursorColor,
            cursorRect,
            renderer.CursorStyle,
            renderer.CursorVisible);
        _shaderFrame++;
        return context;
    }

    private static SKRect GetCursorRect(SkiaTerminalRenderer renderer)
    {
        float left = renderer.CursorColumn * renderer.CellWidth;
        float top = renderer.CursorRow * renderer.CellHeight;
        return renderer.CursorStyle switch
        {
            CursorStyle.Bar => new SKRect(left, top, left + Math.Max(1f, renderer.CellWidth * 0.12f), top + renderer.CellHeight),
            CursorStyle.Underline => new SKRect(
                left,
                top + Math.Max(0f, renderer.CellHeight - Math.Max(1f, renderer.CellHeight * 0.14f)),
                left + renderer.CellWidth,
                top + renderer.CellHeight),
            _ => new SKRect(left, top, left + renderer.CellWidth, top + renderer.CellHeight),
        };
    }

    private readonly record struct TerminalCursorRenderSnapshot(
        int Column,
        int Row,
        bool Visible,
        CursorStyle Style,
        SKColor Color)
    {
        public static TerminalCursorRenderSnapshot From(SkiaTerminalRenderer renderer)
            => new(
                renderer.CursorColumn,
                renderer.CursorRow,
                renderer.CursorVisible,
                renderer.CursorStyle,
                renderer.CursorColor);
    }
}
