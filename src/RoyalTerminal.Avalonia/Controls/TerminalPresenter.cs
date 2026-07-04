// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Rendering.Composition;
using Avalonia.Media;
using Avalonia.Threading;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Avalonia.Interop;
using RoyalTerminal.Rendering.Contracts;
using RoyalTerminal.Shaders;
using SkiaSharp;

namespace RoyalTerminal.Avalonia.Controls;

/// <summary>
/// Host control that switches between standard Skia Sharp composition drawing
/// and direct-to-texture interop rendering depending on the selected backend mode.
/// </summary>
public class TerminalPresenter : Control
{
    private readonly Action _completeCompositionCommit;
    private CompositionCustomVisual? _compositionVisual;
    private SkiaTerminalRenderer? _renderer;
    private TerminalScreen? _screen;
    private IReadOnlyList<TerminalShaderSource>? _shaderSources;
    private bool _shaderAnimationEnabled;
    private bool _compositionCommitPending;
    private bool _compositionCommitQueued;
    private bool _compositionCommitRequestedWhilePending;

    private TerminalRendererType _rendererType = TerminalRendererType.Skia;
    private SkiaInteropRenderer? _interopRenderer;
    private IRenderSurface? _interopRenderSurface;
    private IAvaloniaSkiaRenderTargetProvider? _interopRenderTargetProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="TerminalPresenter"/> class.
    /// </summary>
    public TerminalPresenter()
    {
        _completeCompositionCommit = CompleteCompositionCommit;
    }

    /// <summary>
    /// Gets the composition visual used for rendering.
    /// </summary>
    public CompositionCustomVisual? CompositionVisual => _compositionVisual;

    /// <summary>
    /// Gets or sets the preferred rendering engine.
    /// </summary>
    public TerminalRendererType RendererType
    {
        get => _rendererType;
        set
        {
            if (_rendererType != value)
            {
                _rendererType = value;
                RecreateCompositionVisual();
            }
        }
    }

    /// <summary>
    /// Gets or sets the active native interop surface.
    /// </summary>
    public IRenderSurface? InteropRenderSurface
    {
        get => _interopRenderSurface;
        set
        {
            if (!ReferenceEquals(_interopRenderSurface, value))
            {
                _interopRenderSurface = value;
                _interopRenderer = null;
                Invalidate(fullRedraw: true);
            }
        }
    }

    /// <summary>
    /// Gets or sets the active interop render target provider.
    /// </summary>
    public IAvaloniaSkiaRenderTargetProvider? InteropRenderTargetProvider
    {
        get => _interopRenderTargetProvider;
        set
        {
            if (!ReferenceEquals(_interopRenderTargetProvider, value))
            {
                _interopRenderTargetProvider = value;
                Invalidate(fullRedraw: true);
            }
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        InitializeComposition();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        CleanupCompositionVisual();

        _compositionCommitPending = false;
        _compositionCommitQueued = false;
    }

    private void CleanupCompositionVisual()
    {
        if (_compositionVisual is not null)
        {
            if (_rendererType == TerminalRendererType.Skia)
            {
                _compositionVisual.SendHandlerMessage(new TerminalDrawHandler.DisposeMessage());
            }
            // TerminalTextureInteropDrawHandler is garbage-collected by Avalonia
            ElementComposition.SetElementChildVisual(this, null);
        }

        _compositionVisual = null;
    }

    private void RecreateCompositionVisual()
    {
        CleanupCompositionVisual();
        InitializeComposition();
    }

    private void InitializeComposition()
    {
        if (_compositionVisual is not null)
        {
            return;
        }

        var compositionVisual = ElementComposition.GetElementVisual(this);
        if (compositionVisual is null) return;

        var compositor = compositionVisual.Compositor;

        if (_rendererType == TerminalRendererType.Skia)
        {
            _compositionVisual = compositor.CreateCustomVisual(new TerminalDrawHandler());
        }
        else
        {
            var targetProvider = _interopRenderTargetProvider ?? new AvaloniaSkiaRenderTargetProvider(
                DefaultAvaloniaMetalTextureHandleProvider.Instance
            );

            var surface = _interopRenderSurface;
            if (surface is null && RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                try
                {
                    surface = new RoyalTerminal.Rendering.Interop.Swift.SwiftRenderSurface(RenderBackendKind.Metal);
                    _interopRenderSurface = surface;
                }
                catch
                {
                    // Catch gracefully and allow manual fallback settings
                }
            }

            if (surface is null)
            {
                // Fall back to standard SkiaSharp C# drawing visual if no native interop target is available
                _compositionVisual = compositor.CreateCustomVisual(new TerminalDrawHandler());
                ElementComposition.SetElementChildVisual(this, _compositionVisual);
                UpdateVisualSize();
                return;
            }

            _interopRenderer ??= CreateInteropRenderer(surface);
            _compositionVisual = compositor.CreateCustomVisual(new TerminalTextureInteropDrawHandler());
        }

        ElementComposition.SetElementChildVisual(this, _compositionVisual);

        UpdateVisualSize();

        if (_renderer is not null && _screen is not null)
            SendUpdate();
        SendShaderUpdate();
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (_compositionVisual is null)
            InitializeComposition();
        UpdateVisualSize();
        return base.ArrangeOverride(finalSize);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
    }

    private void UpdateVisualSize()
    {
        if (_compositionVisual is null) return;
        Vector nextSize = new(Bounds.Width, Bounds.Height);
        if (_compositionVisual.Size == nextSize)
        {
            return;
        }

        _compositionVisual.Size = nextSize;
    }

    /// <summary>
    /// Sets the renderer and screen to use for drawing.
    /// </summary>
    public void SetRenderState(SkiaTerminalRenderer renderer, TerminalScreen screen)
    {
        _renderer = renderer;
        _screen = screen;
        SendUpdate();
    }

    /// <summary>
    /// Sets the terminal framebuffer shader sources for this presenter.
    /// </summary>
    public void SetShaderState(
        IReadOnlyList<TerminalShaderSource>? shaderSources,
        bool animationEnabled)
    {
        _shaderSources = shaderSources;
        _shaderAnimationEnabled = animationEnabled;
        SendShaderUpdate();
    }

    /// <summary>
    /// Sends an update message to the composition handler.
    /// </summary>
    public void SendUpdate()
    {
        if (_compositionVisual is null)
        {
            InitializeComposition();
            if (_compositionVisual is null) return;
        }

        if (_rendererType == TerminalRendererType.Skia)
        {
            if (_renderer is null || _screen is null) return;
            _compositionVisual.SendHandlerMessage(
                new TerminalDrawHandler.UpdateMessage(_renderer, _screen));
        }
        else
        {
            if (_interopRenderSurface is null)
            {
                InitializeComposition();
                if (_interopRenderSurface is null) return;
            }

            if (_interopRenderer is null && _interopRenderSurface is not null)
            {
                _interopRenderer = CreateInteropRenderer(_interopRenderSurface);
            }

            if (_interopRenderer is null) return;

            var targetProvider = _interopRenderTargetProvider ?? new AvaloniaSkiaRenderTargetProvider(
                DefaultAvaloniaMetalTextureHandleProvider.Instance
            );

            double scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
            if (_interopRenderSurface is not null)
            {
                _interopRenderSurface.SetScale(scale, scale);
                _interopRenderSurface.SetSize((int)Math.Max(1, Bounds.Width * scale), (int)Math.Max(1, Bounds.Height * scale));
            }

            // Sync C# terminal screen state to Swift native rendering buffer
            if (_interopRenderSurface is RoyalTerminal.Rendering.Interop.Swift.SwiftRenderSurface swiftSurface && _screen is not null && _renderer is not null)
            {
                swiftSurface.SetFont(_renderer.FontFamily, _renderer.FontSize);
                swiftSurface.SetCellSize(_renderer.CellWidth * scale, _renderer.CellHeight * scale, _renderer.Baseline * scale);

                lock (_screen.SyncRoot)
                {
                    swiftSurface.UpdateScreenState(
                        _screen,
                        _renderer.CursorColumn,
                        _renderer.CursorRow,
                        _renderer.CursorVisible,
                        (int)_renderer.CursorStyle
                    );
                }

                SKColor cc = _renderer.CursorColor;
                uint cursorColorArgb = ((uint)cc.Alpha << 24) | ((uint)cc.Red << 16) | ((uint)cc.Green << 8) | cc.Blue;

                swiftSurface.SetTheme(
                    _screen.DefaultForeground,
                    _screen.DefaultBackground,
                    cursorColorArgb
                );
                swiftSurface.SetFocus(true);
            }

            var pixelSize = new PixelSize((int)Math.Max(1, Bounds.Width * scale), (int)Math.Max(1, Bounds.Height * scale));
            _compositionVisual.SendHandlerMessage(
                new TerminalTextureInteropDrawHandler.UpdateMessage(
                    _interopRenderer,
                    targetProvider,
                    pixelSize,
                    null,
                    null
                ));
        }

        SendShaderUpdate();
        RequestCompositionCommit();
    }

    /// <summary>
    /// Requests a re-render of dirty rows.
    /// </summary>
    public void Invalidate(bool fullRedraw = false, bool dirtyRowsOnly = false)
    {
        if (_compositionVisual is null)
        {
            InitializeComposition();
            if (_compositionVisual is null) return;
        }

        if (_rendererType == TerminalRendererType.Skia)
        {
            if (fullRedraw && (_renderer is null || _screen is null))
            {
                SendUpdate();
                return;
            }

            _compositionVisual.SendHandlerMessage(
                new TerminalDrawHandler.InvalidateMessage(fullRedraw, dirtyRowsOnly));
        }
        else
        {
            SendUpdate();
        }

        InvalidateVisual();
        RequestCompositionCommit();
    }

    /// <summary>
    /// Notifies the handler about a size change.
    /// </summary>
    public void NotifyResize(Size newSize)
    {
        if (_compositionVisual is not null)
        {
            if (_rendererType == TerminalRendererType.Skia)
            {
                _compositionVisual.SendHandlerMessage(new TerminalDrawHandler.ResizeMessage());
            }
            else
            {
                var pixelSize = new PixelSize((int)Math.Max(1, newSize.Width), (int)Math.Max(1, newSize.Height));
                _compositionVisual.SendHandlerMessage(new TerminalTextureInteropDrawHandler.ResizeMessage(pixelSize));
                _interopRenderSurface?.SetSize((int)newSize.Width, (int)newSize.Height);
            }
        }

        RequestCompositionCommit();
        UpdateVisualSize();
    }

    private void RequestCompositionCommit()
    {
        CompositionCustomVisual? compositionVisual = _compositionVisual;
        if (compositionVisual is null)
        {
            return;
        }

        if (_compositionCommitPending)
        {
            _compositionCommitRequestedWhilePending = true;
            return;
        }

        if (_compositionCommitQueued)
        {
            return;
        }

        _compositionCommitQueued = true;
        Dispatcher.UIThread.Post(
            () =>
            {
                _compositionCommitQueued = false;
                CompositionCustomVisual? queuedCompositionVisual = _compositionVisual;
                if (queuedCompositionVisual is null || _compositionCommitPending)
                {
                    if (_compositionCommitPending)
                    {
                        _compositionCommitRequestedWhilePending = true;
                    }

                    return;
                }

                _compositionCommitPending = true;
                queuedCompositionVisual.Compositor.RequestCompositionUpdate(_completeCompositionCommit);
            },
            DispatcherPriority.Background);
    }

    private void SendShaderUpdate()
    {
        if (_compositionVisual is null)
        {
            return;
        }

        // Shaders only apply on standard TerminalDrawHandler for now
        if (_rendererType == TerminalRendererType.Skia)
        {
            _compositionVisual.SendHandlerMessage(
                new TerminalDrawHandler.ShaderStateMessage(
                    _shaderSources,
                    _shaderAnimationEnabled));
        }
    }

    private void CompleteCompositionCommit()
    {
        _compositionCommitPending = false;
        if (_compositionCommitRequestedWhilePending)
        {
            _compositionCommitRequestedWhilePending = false;
            RequestCompositionCommit();
        }
    }

    private SkiaInteropRenderer CreateInteropRenderer(IRenderSurface surface)
    {
        if (surface is RoyalTerminal.Rendering.Interop.Swift.SwiftRenderSurface swiftSurface)
        {
            return new SkiaInteropRenderer(surface, new SwiftRgbaFallbackRenderer(swiftSurface));
        }
        return new SkiaInteropRenderer(surface);
    }

    private sealed class SwiftRgbaFallbackRenderer : ISkiaRgbaFallbackRenderer
    {
        private readonly RoyalTerminal.Rendering.Interop.Swift.SwiftRenderSurface _surface;

        public SwiftRgbaFallbackRenderer(RoyalTerminal.Rendering.Interop.Swift.SwiftRenderSurface surface)
        {
            _surface = surface;
        }

        public RenderFrameResult RenderToRgba(Span<byte> destination, int width, int height, int stride)
        {
            return _surface.RenderToRgba(destination, width, height, stride);
        }
    }
}
