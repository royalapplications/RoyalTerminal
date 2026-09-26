// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Terminal;

public sealed partial class BasicVtProcessor
{
    private ITerminalNotificationHost? _notificationHost;
    private TerminalNotificationProtocol? _notifications;

    /// <inheritdoc />
    public ITerminalNotificationHost? NotificationHost
    {
        get => _notificationHost;
        set
        {
            if (ReferenceEquals(_notificationHost, value)) return;
            _notifications?.Dispose();
            _notifications = null;
            _notificationHost = value;
        }
    }

    private void HandleNotification(ReadOnlySpan<byte> source, bool bell)
    {
        if (_notificationHost is not { } host) return;
        int separator = source.IndexOf((byte)';');
        if (separator < 0) return;
        (_notifications ??= new(host, _options.TimeProvider, bytes => ResponseCallback?.Invoke(bytes)))
            .Handle(source[..separator], source[(separator + 1)..], bell);
    }

    private void ClearSessionNotifications()
    {
        _notifications?.Dispose();
        _notifications = null;
    }
}
