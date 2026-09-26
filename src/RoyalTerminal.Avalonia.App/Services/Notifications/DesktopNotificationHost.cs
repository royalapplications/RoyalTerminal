// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Concurrent;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using RoyalTerminal.Avalonia.Controls;
using RoyalTerminal.Terminal;

namespace RoyalTerminal.Avalonia.App.Services.Notifications;

// Per-pane UI facade. Cached state is the only state read under the VT lock.
// All actual focus and visual-tree access stays on the UI thread.
internal sealed class DesktopNotificationHost : ITerminalNotificationHost, ITerminalNotificationFocusLifetime, IDisposable
{
    private readonly DesktopNotificationService _service;
    private readonly Window _window;
    private readonly TerminalControl _control;
    private readonly Action _focus;
    private readonly List<Visual> _visuals = new();
    private readonly ConcurrentDictionary<Guid, byte> _owned = new();
    private int _focused, _visible, _disposed, _focusGeneration;

    internal DesktopNotificationHost(DesktopNotificationService service, Window window, TerminalControl control, Action focus)
    {
        Dispatcher.UIThread.VerifyAccess();
        _service = service; _window = window; _control = control; _focus = focus;
        _window.PropertyChanged += VisualChanged;
        _control.GotFocus += GotFocus;
        _control.LostFocus += LostFocus;
        _control.AttachedToVisualTree += Attached;
        _control.DetachedFromVisualTree += Detached;
        ObserveAncestors();
    }

    public TerminalNotificationCapabilities Capabilities
    {
        get
        {
            if (Volatile.Read(ref _disposed) != 0) return TerminalNotificationCapabilities.None;
            TerminalNotificationCapabilities capabilities = _service.Capabilities;
            return (capabilities & TerminalNotificationCapabilities.Activation) != 0
                ? capabilities | TerminalNotificationCapabilities.Focus : capabilities;
        }
    }
    public bool IsFocused => Volatile.Read(ref _focused) != 0;
    public bool IsVisible => Volatile.Read(ref _visible) != 0;

    public bool Show(TerminalNotificationRequest request, Action<TerminalNotificationFeedback> feedback)
    {
        if (Volatile.Read(ref _disposed) != 0 || !_owned.TryAdd(request.Token, 0)) return false;
        bool accepted = _service.Show(request, value =>
        {
            _owned.TryRemove(request.Token, out _);
            if (Volatile.Read(ref _disposed) == 0) feedback(value);
        });
        if (!accepted) _owned.TryRemove(request.Token, out _);
        else if (request.ReplacesToken is Guid old) _owned.TryRemove(old, out _);
        if (Volatile.Read(ref _disposed) != 0) { Close(request.Token); return false; }
        return accepted;
    }

    public void Close(Guid token) { _owned.TryRemove(token, out _); _service.Close(token); }
    public bool IsAlive(Guid token) => Volatile.Read(ref _disposed) == 0 && _service.IsAlive(token);
    public void CancelPendingFocus() => Interlocked.Increment(ref _focusGeneration);
    public void Focus()
    {
        int generation = Volatile.Read(ref _focusGeneration);
        Dispatcher.UIThread.Post(() =>
        {
            if (Volatile.Read(ref _disposed) != 0 || generation != Volatile.Read(ref _focusGeneration) || _control.GetVisualRoot() != _window) return;
            _focus();
        }, DispatcherPriority.Input);
    }

    private void GotFocus(object? sender, GotFocusEventArgs args) => UpdateState();
    private void LostFocus(object? sender, RoutedEventArgs args) => UpdateState();
    private void Attached(object? sender, VisualTreeAttachmentEventArgs args) => ObserveAncestors();
    private void Detached(object? sender, VisualTreeAttachmentEventArgs args) { CancelPendingFocus(); UnobserveAncestors(); UpdateState(); }
    private void VisualChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property == Visual.IsVisibleProperty || args.Property == InputElement.IsKeyboardFocusWithinProperty ||
            args.Property == Window.IsActiveProperty || args.Property == Window.WindowStateProperty) UpdateState();
    }

    private void ObserveAncestors()
    {
        UnobserveAncestors();
        for (Visual? visual = _control; visual is not null && visual != _window; visual = visual.GetVisualParent())
        { _visuals.Add(visual); visual.PropertyChanged += VisualChanged; }
        UpdateState();
    }

    private void UnobserveAncestors()
    {
        foreach (Visual visual in _visuals) visual.PropertyChanged -= VisualChanged;
        _visuals.Clear();
    }

    private void UpdateState()
    {
        bool visible = Volatile.Read(ref _disposed) == 0 && _control.GetVisualRoot() == _window && _window.IsActive && _window.IsVisible && _window.WindowState != WindowState.Minimized;
        foreach (Visual visual in _visuals) visible &= visual.IsVisible;
        Volatile.Write(ref _visible, visible ? 1 : 0);
        Volatile.Write(ref _focused, visible && _control.IsKeyboardFocusWithin ? 1 : 0);
    }

    public void Dispose()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        CancelPendingFocus();
        _window.PropertyChanged -= VisualChanged;
        _control.GotFocus -= GotFocus; _control.LostFocus -= LostFocus;
        _control.AttachedToVisualTree -= Attached; _control.DetachedFromVisualTree -= Detached;
        UnobserveAncestors();
        foreach (Guid token in _owned.Keys) _service.Close(token);
        _owned.Clear();
        Volatile.Write(ref _visible, 0); Volatile.Write(ref _focused, 0);
    }
}
