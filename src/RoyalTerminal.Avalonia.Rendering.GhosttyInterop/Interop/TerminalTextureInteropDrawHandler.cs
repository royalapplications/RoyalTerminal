// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia.Rendering.GhosttyInterop - Composition draw handler for texture interop mode.

using Avalonia;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.Composition;
using Avalonia.Skia;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Rendering.Interop.Ghostty.Skia;
using SkiaSharp;

namespace RoyalTerminal.Avalonia.Rendering.GhosttyInterop.Interop;

/// <summary>
/// Composition draw handler that renders frames via <see cref="SkiaInteropRenderer"/>.
/// </summary>
public sealed class TerminalTextureInteropDrawHandler : CompositionCustomVisualHandler
{
    private SkiaInteropRenderer? _renderer;
    private IAvaloniaSkiaRenderTargetProvider? _renderTargetProvider;
    private SkiaTerminalRenderer? _overlayRenderer;
    private TerminalScreen? _overlayScreen;
    private PixelSize _pixelSize;
    private bool _pendingRender;

    /// <summary>
    /// Message sent when render dependencies or target size change.
    /// </summary>
    public readonly record struct UpdateMessage(
        SkiaInteropRenderer Renderer,
        IAvaloniaSkiaRenderTargetProvider RenderTargetProvider,
        PixelSize PixelSize,
        SkiaTerminalRenderer? OverlayRenderer = null,
        TerminalScreen? OverlayScreen = null);

    /// <summary>
    /// Message sent when only target size changed.
    /// </summary>
    public readonly record struct ResizeMessage(PixelSize PixelSize);

    /// <summary>
    /// Message sent to request a new frame.
    /// </summary>
    public readonly record struct InvalidateMessage();

    /// <inheritdoc />
    public override void OnMessage(object message)
    {
        switch (message)
        {
            case UpdateMessage update:
                _renderer = update.Renderer;
                _renderTargetProvider = update.RenderTargetProvider;
                _overlayRenderer = update.OverlayRenderer;
                _overlayScreen = update.OverlayScreen;
                _pixelSize = update.PixelSize;
                _pendingRender = true;
                RegisterForNextAnimationFrameUpdate();
                break;

            case ResizeMessage resize:
                _pixelSize = resize.PixelSize;
                _pendingRender = true;
                RegisterForNextAnimationFrameUpdate();
                break;

            case InvalidateMessage:
                _pendingRender = true;
                RegisterForNextAnimationFrameUpdate();
                break;
        }
    }

    /// <inheritdoc />
    public override void OnAnimationFrameUpdate()
    {
        if (_pendingRender)
        {
            Invalidate();
        }
    }

    /// <inheritdoc />
    public override void OnRender(ImmediateDrawingContext context)
    {
        bool requiresRedraw = false;

        try
        {
            SkiaInteropRenderer? renderer = _renderer;
            IAvaloniaSkiaRenderTargetProvider? renderTargetProvider = _renderTargetProvider;
            if (renderer is null || renderTargetProvider is null)
            {
                return;
            }

            ISkiaSharpApiLeaseFeature? leaseFeature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
            if (leaseFeature is null)
            {
                return;
            }

            using ISkiaSharpApiLease lease = leaseFeature.Lease();
            SKCanvas canvas = lease.SkCanvas;

            PixelSize targetPixelSize = _pixelSize;
            if (targetPixelSize.Width <= 0 || targetPixelSize.Height <= 0)
            {
                targetPixelSize = GetRenderTargetPixelSize(
                    GetRenderBounds(),
                    canvas.LocalClipBounds);
            }

            SkiaInteropRenderRequest request = renderTargetProvider.CreateRenderRequest(lease, targetPixelSize);
            SkiaInteropRenderResult renderResult = renderer.Render(canvas, request);
            requiresRedraw = renderResult.FrameResult.RequiresRedraw;

            SkiaTerminalRenderer? overlayRenderer = _overlayRenderer;
            TerminalScreen? overlayScreen = _overlayScreen;
            if (overlayRenderer is not null && overlayScreen is not null)
            {
                lock (overlayScreen.SyncRoot)
                {
                    overlayRenderer.RenderFull(canvas, overlayScreen);
                }
            }
        }
        catch
        {
            // Swallow render exceptions during teardown/lost context transitions.
        }
        finally
        {
            _pendingRender = requiresRedraw;
            if (requiresRedraw)
            {
                RegisterForNextAnimationFrameUpdate();
            }
        }
    }

    internal static PixelSize GetRenderTargetPixelSize(
        Rect renderBounds,
        SKRect localClipBounds)
    {
        double width = GetPositiveFiniteOrFallback(renderBounds.Width, localClipBounds.Width);
        double height = GetPositiveFiniteOrFallback(renderBounds.Height, localClipBounds.Height);
        return new PixelSize(
            Math.Max(1, (int)Math.Ceiling(width)),
            Math.Max(1, (int)Math.Ceiling(height)));
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
}
