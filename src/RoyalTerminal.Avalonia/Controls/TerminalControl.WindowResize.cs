// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Avalonia;
using Avalonia.Threading;
using RoyalTerminal.Avalonia.Services;
using RoyalTerminal.Terminal;

namespace RoyalTerminal.Avalonia.Controls;

public partial class TerminalControl
{
    /// <summary>Explicit opt-in to CSI 8 t host window resizing; false by default.</summary>
    public static readonly DirectProperty<TerminalControl, bool> AllowVtWindowResizeProperty =
        AvaloniaProperty.RegisterDirect<TerminalControl, bool>(nameof(AllowVtWindowResize),
            control => control.AllowVtWindowResize, (control, value) => control.AllowVtWindowResize = value);

    /// <summary>Embedding window-resize policy host. The embedding application owns it.</summary>
    public static readonly DirectProperty<TerminalControl, ITerminalWindowResizeHost?> WindowResizeHostProperty =
        AvaloniaProperty.RegisterDirect<TerminalControl, ITerminalWindowResizeHost?>(nameof(WindowResizeHost),
            control => control.WindowResizeHost, (control, value) => control.WindowResizeHost = value);

    private bool _allowVtWindowResize;
    private ITerminalWindowResizeHost? _windowResizeHost;
    private bool _windowResizeDetached = true;
    private TerminalWindowResizeQueue? _windowResizeQueue;

    /// <summary>Allows terminal resize requests to reach the host. Configure on the UI thread.</summary>
    public bool AllowVtWindowResize
    {
        get => _allowVtWindowResize;
        set
        {
            Dispatcher.UIThread.VerifyAccess();
            if (SetAndRaise(AllowVtWindowResizeProperty, ref _allowVtWindowResize, value)) BindWindowResizeHost(_vtProcessor);
        }
    }

    /// <summary>Host for permitted requests, delivered on the UI thread. Null disables delivery.</summary>
    public ITerminalWindowResizeHost? WindowResizeHost
    {
        get => _windowResizeHost;
        set
        {
            Dispatcher.UIThread.VerifyAccess();
            if (SetAndRaise(WindowResizeHostProperty, ref _windowResizeHost, value)) BindWindowResizeHost(_vtProcessor);
        }
    }

    private void BindWindowResizeHost(IVtProcessor? processor)
    {
        if (_screen is null || processor is not ITerminalWindowResizeSource source) return;
        lock (_screen.SyncRoot)
        {
            _windowResizeQueue?.Reset();
            if (!_allowVtWindowResize || _windowResizeHost is null || _windowResizeDetached)
            {
                source.WindowResizeCallback = null;
                return;
            }
            _windowResizeQueue ??= new(action => Dispatcher.UIThread.Post(action), ApplyWindowResizeRequest);
            source.WindowResizeCallback = _windowResizeQueue.Enqueue;
        }
    }

    private void ApplyWindowResizeRequest(TerminalWindowResizeRequest request)
    {
        if (!_windowResizeDetached && _allowVtWindowResize) _windowResizeHost?.RequestResize(request);
    }
}
