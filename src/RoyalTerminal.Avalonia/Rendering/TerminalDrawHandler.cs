// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia - Avalonia composition draw handler.

using System.Diagnostics;
using Avalonia;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.Composition;
using Avalonia.Skia;
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
    private GRContext? _terminalSurfaceGrContext;
    private bool _terminalSurfaceGpuBacked;
    private float _terminalSurfaceScaleX = 1f;
    private float _terminalSurfaceScaleY = 1f;
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

            Rect renderBounds = GetRenderBounds();
            RenderTargetScale renderScale = GetCanvasScale(
                renderBounds,
                canvas.LocalClipBounds,
                canvas.TotalMatrix);
            (float logicalWidth, float logicalHeight) = GetRenderTargetLogicalSize(
                renderBounds,
                canvas.LocalClipBounds);
            (int width, int height) = GetRenderTargetPixelSize(
                renderBounds,
                canvas.LocalClipBounds,
                renderScale.X,
                renderScale.Y);
            SKRect logicalDestinationRect = new(0, 0, width / renderScale.X, height / renderScale.Y);
            SKRect logicalClipRect = new(0, 0, logicalWidth, logicalHeight);
            SKImage? terminalFrame = null;
            SKColor background = default;
            GRContext? grContext = lease.GrContext;

            lock (screen.SyncRoot)
            {
                background = new SKColor(screen.DefaultBackground);
                if (!EnsureTerminalSurface(width, height, renderScale, grContext))
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

                    terminalCanvas.Save();
                    terminalCanvas.Scale(renderScale.X, renderScale.Y);
                    try
                    {
                        if (!fullRedraw)
                        {
                            ClearDirtyViewportRows(terminalCanvas, renderer, screen, background, logicalWidth);
                        }

                        renderer.Render(terminalCanvas, screen, forceFullRedraw: fullRedraw);
                    }
                    finally
                    {
                        terminalCanvas.Restore();
                    }

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
                canvas.ClipRect(logicalClipRect, SKClipOperation.Intersect, antialias: false);
                TerminalShaderPostProcessor? shaderPostProcessor = _shaderPostProcessor;
                if (shaderPostProcessor is not null && shaderPostProcessor.HasShaders)
                {
                    RenderWithShaders(
                        canvas,
                        renderer,
                        screen,
                        shaderPostProcessor,
                        terminalFrame,
                        width,
                        height,
                        logicalDestinationRect,
                        renderScale,
                        grContext);
                }
                else
                {
                    canvas.Clear(background);
                    canvas.DrawImage(terminalFrame, logicalDestinationRect);
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
        _terminalSurfaceGrContext = null;
        _terminalSurfaceGpuBacked = false;
        _terminalSurfaceScaleX = 1f;
        _terminalSurfaceScaleY = 1f;
        _cachedFrameValid = false;
        _terminalFrameDirty = true;
        _forceFullRedrawRequested = true;
        _invalidateViewportRequested = true;
    }

    private bool EnsureTerminalSurface(
        int width,
        int height,
        RenderTargetScale scale,
        GRContext? grContext)
    {
        GRContext? activeGrContext = TerminalShaderPostProcessor.CanUseGpuRenderSurface(grContext)
            ? grContext
            : null;
        if (_terminalSurface is not null &&
            _terminalSurfaceInfo.Width == width &&
            _terminalSurfaceInfo.Height == height &&
            ReferenceEquals(_terminalSurfaceGrContext, activeGrContext) &&
            Math.Abs(_terminalSurfaceScaleX - scale.X) < 0.001f &&
            Math.Abs(_terminalSurfaceScaleY - scale.Y) < 0.001f)
        {
            return true;
        }

        _terminalSurface?.Dispose();
        _terminalSurfaceInfo = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        _terminalSurface = TerminalShaderPostProcessor.CreateRenderSurface(
            _terminalSurfaceInfo,
            activeGrContext,
            out bool isGpuBacked);
        _terminalSurfaceGrContext = activeGrContext;
        _terminalSurfaceGpuBacked = isGpuBacked;
        _terminalSurfaceScaleX = scale.X;
        _terminalSurfaceScaleY = scale.Y;
        _cachedFrameValid = false;
        _terminalFrameDirty = true;
        _forceFullRedrawRequested = true;
        _invalidateViewportRequested = true;
        return _terminalSurface is not null;
    }

    internal static (int Width, int Height) GetRenderTargetPixelSize(
        Rect renderBounds,
        SKRect localClipBounds,
        float scaleX = 1f,
        float scaleY = 1f)
    {
        (float width, float height) = GetRenderTargetLogicalSize(renderBounds, localClipBounds);
        return (
            Math.Max(1, (int)Math.Ceiling(width * NormalizeScale(scaleX))),
            Math.Max(1, (int)Math.Ceiling(height * NormalizeScale(scaleY))));
    }

    internal static (float Width, float Height) GetRenderTargetLogicalSize(
        Rect renderBounds,
        SKRect localClipBounds)
    {
        // LocalClipBounds can be Avalonia's current damage clip. The retained
        // terminal framebuffer must follow the full visual bounds or a partial
        // render pass can cache a clipped terminal image.
        double width = GetPositiveFiniteOrFallback(renderBounds.Width, localClipBounds.Width);
        double height = GetPositiveFiniteOrFallback(renderBounds.Height, localClipBounds.Height);
        return (
            Math.Max(1f, (float)width),
            Math.Max(1f, (float)height));
    }

    internal static RenderTargetScale GetCanvasScale(SKMatrix matrix)
    {
        float scaleX = MathF.Sqrt((matrix.ScaleX * matrix.ScaleX) + (matrix.SkewY * matrix.SkewY));
        float scaleY = MathF.Sqrt((matrix.SkewX * matrix.SkewX) + (matrix.ScaleY * matrix.ScaleY));
        return new RenderTargetScale(NormalizeScale(scaleX), NormalizeScale(scaleY));
    }

    internal static RenderTargetScale GetCanvasScale(
        Rect renderBounds,
        SKRect localClipBounds,
        SKMatrix matrix)
    {
        RenderTargetScale matrixScale = GetCanvasScale(matrix);
        float inferredScaleX = InferScale(renderBounds.Width, localClipBounds.Width);
        float inferredScaleY = InferScale(renderBounds.Height, localClipBounds.Height);
        float scaleX = MathF.Max(matrixScale.X, inferredScaleX);
        float scaleY = MathF.Max(matrixScale.Y, inferredScaleY);

        if (scaleX > 1.001f && scaleY <= 1.001f)
        {
            scaleY = scaleX;
        }
        else if (scaleY > 1.001f && scaleX <= 1.001f)
        {
            scaleX = scaleY;
        }

        return new RenderTargetScale(scaleX, scaleY);
    }

    private static double GetPositiveFiniteOrFallback(double value, double fallback)
    {
        if (double.IsFinite(value) && value > 0)
        {
            return value;
        }

        if (double.IsFinite(fallback) && fallback > 0)
        {
            return fallback;
        }

        return 1d;
    }

    private static float NormalizeScale(float scale)
    {
        return float.IsFinite(scale) && scale > 0f
            ? scale
            : 1f;
    }

    private static float InferScale(double logicalSize, float clipSize)
    {
        if (!double.IsFinite(logicalSize) ||
            logicalSize <= 0d ||
            !float.IsFinite(clipSize) ||
            clipSize <= 0f)
        {
            return 1f;
        }

        float scale = clipSize / (float)logicalSize;
        return scale > 1.001f ? scale : 1f;
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
        float canvasWidth)
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
        int height,
        SKRect logicalDestinationRect,
        RenderTargetScale renderScale,
        GRContext? grContext)
    {
        TerminalShaderFrameContext frameContext = CreateFrameContext(
            renderer,
            screen,
            width,
            height,
            renderScale);
        SKImageInfo shaderSurfaceInfo = new(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        GRContext? activeGrContext = _terminalSurfaceGpuBacked &&
                                     TerminalShaderPostProcessor.CanUseGpuRenderSurface(grContext)
            ? grContext
            : null;
        using SKSurface? shaderSurface = TerminalShaderPostProcessor.CreateRenderSurface(
            shaderSurfaceInfo,
            activeGrContext);
        if (shaderSurface is null)
        {
            destinationCanvas.Clear(new SKColor(screen.DefaultBackground));
            destinationCanvas.DrawImage(terminalFrame, logicalDestinationRect);
            return;
        }

        SKRect pixelDestinationRect = new(0, 0, width, height);
        if (!shaderPostProcessor.TryApply(
                activeGrContext,
                shaderSurface.Canvas,
                terminalFrame,
                pixelDestinationRect,
                frameContext))
        {
            destinationCanvas.Clear(new SKColor(screen.DefaultBackground));
            destinationCanvas.DrawImage(terminalFrame, logicalDestinationRect);
            return;
        }

        using SKImage? shaderFrame = shaderSurface.Snapshot();
        destinationCanvas.Clear(new SKColor(screen.DefaultBackground));
        if (shaderFrame is null)
        {
            destinationCanvas.DrawImage(terminalFrame, logicalDestinationRect);
            return;
        }

        destinationCanvas.DrawImage(shaderFrame, logicalDestinationRect);
    }

    private TerminalShaderFrameContext CreateFrameContext(
        SkiaTerminalRenderer renderer,
        TerminalScreen screen,
        int width,
        int height,
        RenderTargetScale renderScale)
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

        SKRect cursorRect = GetCursorRect(renderer, renderScale);
        TerminalShaderFrameContext context = new(
            width,
            height,
            time,
            timeDelta,
            _shaderFrame,
            scale: MathF.Max(renderScale.X, renderScale.Y),
            new SKColor(screen.DefaultBackground),
            new SKColor(screen.DefaultForeground),
            renderer.CursorColor,
            cursorRect,
            renderer.CursorStyle,
            renderer.CursorVisible);
        _shaderFrame++;
        return context;
    }

    private static SKRect GetCursorRect(SkiaTerminalRenderer renderer, RenderTargetScale renderScale)
    {
        float left = renderer.CursorColumn * renderer.CellWidth * renderScale.X;
        float top = renderer.CursorRow * renderer.CellHeight * renderScale.Y;
        float cellWidth = renderer.CellWidth * renderScale.X;
        float cellHeight = renderer.CellHeight * renderScale.Y;
        return renderer.CursorStyle switch
        {
            CursorStyle.Bar => new SKRect(left, top, left + Math.Max(1f, cellWidth * 0.12f), top + cellHeight),
            CursorStyle.Underline => new SKRect(
                left,
                top + Math.Max(0f, cellHeight - Math.Max(1f, cellHeight * 0.14f)),
                left + cellWidth,
                top + cellHeight),
            _ => new SKRect(left, top, left + cellWidth, top + cellHeight),
        };
    }

    internal readonly record struct RenderTargetScale(float X, float Y);

    private readonly record struct TerminalCursorRenderSnapshot(
        int Column,
        int Row,
        bool Visible,
        CursorStyle Style,
        SKColor Color,
        SKColor TextColor)
    {
        public static TerminalCursorRenderSnapshot From(SkiaTerminalRenderer renderer)
            => new(
                renderer.CursorColumn,
                renderer.CursorRow,
                renderer.CursorVisible,
                renderer.CursorStyle,
                renderer.CursorColor,
                renderer.CursorTextColor);
    }
}
