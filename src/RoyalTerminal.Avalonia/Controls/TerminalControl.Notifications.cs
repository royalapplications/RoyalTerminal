// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Avalonia;
using Avalonia.Threading;
using RoyalTerminal.Terminal;

namespace RoyalTerminal.Avalonia.Controls;

public partial class TerminalControl
{
    /// <summary>Optional nonblocking desktop notification host, shared by both VT engines.</summary>
    public static readonly DirectProperty<TerminalControl, ITerminalNotificationHost?> NotificationHostProperty =
        AvaloniaProperty.RegisterDirect<TerminalControl, ITerminalNotificationHost?>(
            nameof(NotificationHost), control => control.NotificationHost, (control, value) => control.NotificationHost = value);

    private ITerminalNotificationHost? _notificationHost;
    private bool _notificationHostDetached;

    /// <summary>
    /// Host for OSC 99 notifications. Null disables notifications without advertising
    /// support. Configure on the UI thread. The embedding application owns host lifetime;
    /// detachment, session stop and processor replacement close the surface's notifications.
    /// </summary>
    public ITerminalNotificationHost? NotificationHost
    {
        get => _notificationHost;
        set
        {
            Dispatcher.UIThread.VerifyAccess();
            if (SetAndRaise(NotificationHostProperty, ref _notificationHost, value))
                BindNotificationHost(_vtProcessor, value);
        }
    }

    private void BindNotificationHost(IVtProcessor? processor, ITerminalNotificationHost? host)
    {
        if (_screen is null || processor is not ITerminalNotificationSource source) return;
        lock (_screen.SyncRoot) source.NotificationHost = _notificationHostDetached ? null : host;
    }

    private void ResetNotificationHostSession()
    {
        BindNotificationHost(_vtProcessor, null);
        BindNotificationHost(_vtProcessor, _notificationHost);
    }
}
