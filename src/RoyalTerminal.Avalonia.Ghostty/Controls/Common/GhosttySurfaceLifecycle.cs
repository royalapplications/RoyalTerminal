// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia.Controls - Shared Ghostty app/surface lifecycle wiring.

using Avalonia.Threading;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.GhosttySharp.Native;

namespace RoyalTerminal.Avalonia.Controls;

internal sealed class GhosttySurfaceLifecycle : IDisposable
{
    private readonly Func<GhosttySurface?> _surfaceAccessor;
    private readonly GhosttyActionDispatcher _actionDispatcher;
    private readonly GhosttyClipboardAdapter _clipboardAdapter;
    private readonly Action _tickRequested;
    private readonly Action _closeRequested;
    private GhosttyApp? _app;

    public GhosttySurfaceLifecycle(
        Func<GhosttySurface?> surfaceAccessor,
        GhosttyActionDispatcher actionDispatcher,
        GhosttyClipboardAdapter clipboardAdapter,
        Action tickRequested,
        Action closeRequested)
    {
        _surfaceAccessor = surfaceAccessor ?? throw new ArgumentNullException(nameof(surfaceAccessor));
        _actionDispatcher = actionDispatcher ?? throw new ArgumentNullException(nameof(actionDispatcher));
        _clipboardAdapter = clipboardAdapter ?? throw new ArgumentNullException(nameof(clipboardAdapter));
        _tickRequested = tickRequested ?? throw new ArgumentNullException(nameof(tickRequested));
        _closeRequested = closeRequested ?? throw new ArgumentNullException(nameof(closeRequested));
    }

    public void Attach(GhosttyApp app)
    {
        ArgumentNullException.ThrowIfNull(app);

        if (ReferenceEquals(_app, app))
        {
            return;
        }

        Detach();

        _app = app;
        _app.WakeupRequested += OnWakeupRequested;
        _app.ActionRequested += OnActionRequested;
        _app.ClipboardReadRequested += OnClipboardReadRequested;
        _app.ClipboardWriteRequested += OnClipboardWriteRequested;
        _app.SurfaceCloseRequested += OnSurfaceCloseRequested;
    }

    public void Detach()
    {
        if (_app is null)
        {
            return;
        }

        _app.WakeupRequested -= OnWakeupRequested;
        _app.ActionRequested -= OnActionRequested;
        _app.ClipboardReadRequested -= OnClipboardReadRequested;
        _app.ClipboardWriteRequested -= OnClipboardWriteRequested;
        _app.SurfaceCloseRequested -= OnSurfaceCloseRequested;
        _app = null;
    }

    private void OnWakeupRequested()
    {
        Dispatcher.UIThread.Post(_tickRequested);
    }

    private bool OnActionRequested(GhosttyTarget target, GhosttyAction action)
    {
        return _actionDispatcher.HandleAction(target, action);
    }

    private void OnClipboardReadRequested(GhosttyClipboard clipboard, nint state)
    {
        _clipboardAdapter.HandleReadRequest(_surfaceAccessor(), state);
    }

    private void OnClipboardWriteRequested(
        GhosttyClipboard clipboard,
        nint contentPtr,
        nuint len,
        bool confirm)
    {
        _clipboardAdapter.HandleWriteRequest(contentPtr, len);
    }

    private void OnSurfaceCloseRequested(bool processAlive)
    {
        Dispatcher.UIThread.Post(_closeRequested);
    }

    public void Dispose()
    {
        Detach();
    }
}
