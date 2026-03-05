// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia - Avalonia composition draw handler.

using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.Composition;
using Avalonia.Skia;
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
    private bool _pendingRender;

    /// <summary>
    /// Message types for communicating with the handler from the UI thread.
    /// </summary>
    public record UpdateMessage(SkiaTerminalRenderer Renderer, TerminalScreen Screen);
    public readonly record struct InvalidateMessage();
    public readonly record struct ResizeMessage();

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

            var leaseFeature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
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
                canvas.Clear(new SKColor(screen.DefaultBackground));
                renderer.RenderFull(canvas, screen);
                canvas.Restore();
            }
        }
        catch
        {
            // Swallow render errors during shutdown/teardown
        }
        finally
        {
            _pendingRender = false;
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
}
