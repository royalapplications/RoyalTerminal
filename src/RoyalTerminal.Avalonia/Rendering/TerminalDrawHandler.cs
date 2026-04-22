// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia - Avalonia composition draw handler.

using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.Composition;
using Avalonia.Skia;
using System.Diagnostics;
using SkiaSharp;

namespace RoyalTerminal.Avalonia.Rendering;

/// <summary>
/// Custom visual draw handler for rendering the terminal using SkiaSharp
/// within Avalonia's composition system.
/// Always performs full redraws because the composition canvas is immediate-mode
/// (cleared each frame). Thread-safe: acquires TerminalScreen.SyncRoot during rendering.
/// </summary>
public class TerminalDrawHandler : CompositionCustomVisualHandler
{
    private SkiaTerminalRenderer? _renderer;
    private TerminalScreen? _screen;
    private TerminalShaderPostProcessor? _shaderPostProcessor;
    private bool _shaderAnimationEnabled;
    private bool _pendingRender;
    private long _shaderStartTimestamp;
    private long _lastShaderTimestamp;
    private int _shaderFrame;

    /// <summary>
    /// Message types for communicating with the handler from the UI thread.
    /// </summary>
    public record UpdateMessage(SkiaTerminalRenderer Renderer, TerminalScreen Screen);
    public readonly record struct InvalidateMessage();
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
                RequestRender();
                break;

            case InvalidateMessage:
                RequestRender();
                break;

            case ResizeMessage:
                RequestRender();
                break;

            case ShaderStateMessage shaderState:
                SetShaderState(shaderState.Sources, shaderState.AnimationEnabled);
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

            lock (screen.SyncRoot)
            {
                canvas.Save();
                try
                {
                    TerminalShaderPostProcessor? shaderPostProcessor = _shaderPostProcessor;
                    if (shaderPostProcessor is not null && shaderPostProcessor.HasShaders)
                    {
                        RenderWithShaders(canvas, renderer, screen, shaderPostProcessor);
                    }
                    else
                    {
                        canvas.Clear(new SKColor(screen.DefaultBackground));
                        renderer.RenderFull(canvas, screen);
                    }
                }
                finally
                {
                    canvas.Restore();
                }
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

    private void RequestRender()
    {
        if (_pendingRender)
        {
            return;
        }

        _pendingRender = true;
        RegisterForNextAnimationFrameUpdate();
    }

    private void SetShaderState(IReadOnlyList<TerminalShaderSource>? sources, bool animationEnabled)
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
        TerminalShaderPostProcessor shaderPostProcessor)
    {
        SKRect clip = destinationCanvas.LocalClipBounds;
        int width = Math.Max(1, (int)Math.Ceiling(clip.Width));
        int height = Math.Max(1, (int)Math.Ceiling(clip.Height));
        SKImageInfo imageInfo = new(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);

        using SKSurface? terminalSurface = SKSurface.Create(imageInfo);
        if (terminalSurface is null)
        {
            destinationCanvas.Clear(new SKColor(screen.DefaultBackground));
            renderer.RenderFull(destinationCanvas, screen);
            return;
        }

        SKCanvas terminalCanvas = terminalSurface.Canvas;
        terminalCanvas.Clear(new SKColor(screen.DefaultBackground));
        renderer.RenderFull(terminalCanvas, screen);

        using SKImage? terminalFrame = terminalSurface.Snapshot();
        if (terminalFrame is null)
        {
            destinationCanvas.Clear(new SKColor(screen.DefaultBackground));
            renderer.RenderFull(destinationCanvas, screen);
            return;
        }

        TerminalShaderFrameContext frameContext = CreateFrameContext(renderer, screen, width, height);
        SKRect destinationRect = new(0, 0, width, height);
        if (!shaderPostProcessor.TryApply(destinationCanvas, terminalFrame, destinationRect, frameContext))
        {
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
}
