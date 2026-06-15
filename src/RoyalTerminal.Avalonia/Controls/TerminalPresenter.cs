// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia - Avalonia composition presenter for terminal rendering.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Rendering.Composition;
using Avalonia.Media;
using Avalonia.Threading;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Shaders;

namespace RoyalTerminal.Avalonia.Controls;

/// <summary>
/// Avalonia control that hosts the terminal rendering surface using
/// Composition API with a custom visual handler.
/// Provides the bridge between Avalonia's visual tree and SkiaSharp rendering.
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

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        InitializeComposition();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _compositionVisual = null;
        _compositionCommitPending = false;
        _compositionCommitQueued = false;
    }

    private void InitializeComposition()
    {
        var compositionVisual = ElementComposition.GetElementVisual(this);
        if (compositionVisual is null) return;

        var compositor = compositionVisual.Compositor;
        _compositionVisual = compositor.CreateCustomVisual(new TerminalDrawHandler());
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

        // Composition visuals are rendered out-of-band; draw a transparent rect
        // so Avalonia hit-testing has a concrete surface for pointer targeting.
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
    /// Retries composition initialization if the visual isn't ready yet.
    /// </summary>
    public void SendUpdate()
    {
        if (_compositionVisual is null)
        {
            InitializeComposition();
            if (_compositionVisual is null) return;
        }

        if (_renderer is null || _screen is null) return;
        _compositionVisual.SendHandlerMessage(
            new TerminalDrawHandler.UpdateMessage(_renderer, _screen));
        SendShaderUpdate();
        RequestCompositionCommit();
    }

    /// <summary>
    /// Requests a re-render of dirty rows.
    /// Retries composition initialization if the visual isn't ready yet.
    /// </summary>
    public void Invalidate(bool fullRedraw = false, bool dirtyRowsOnly = false)
    {
        if (_compositionVisual is null)
        {
            InitializeComposition();
            if (_compositionVisual is null) return;
        }

        if (fullRedraw && (_renderer is null || _screen is null))
        {
            SendUpdate();
            return;
        }

        _compositionVisual.SendHandlerMessage(
            new TerminalDrawHandler.InvalidateMessage(fullRedraw, dirtyRowsOnly));
        RequestCompositionCommit();
    }

    /// <summary>
    /// Notifies the handler about a size change.
    /// </summary>
    public void NotifyResize(Size newSize)
    {
        _compositionVisual?.SendHandlerMessage(
            new TerminalDrawHandler.ResizeMessage());
        RequestCompositionCommit();
        UpdateVisualSize();
    }

    private void RequestCompositionCommit()
    {
        CompositionCustomVisual? compositionVisual = _compositionVisual;
        if (compositionVisual is null || _compositionCommitPending || _compositionCommitQueued)
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

        _compositionVisual.SendHandlerMessage(
            new TerminalDrawHandler.ShaderStateMessage(
                _shaderSources,
                _shaderAnimationEnabled));
    }

    private void CompleteCompositionCommit()
    {
        _compositionCommitPending = false;
    }
}
