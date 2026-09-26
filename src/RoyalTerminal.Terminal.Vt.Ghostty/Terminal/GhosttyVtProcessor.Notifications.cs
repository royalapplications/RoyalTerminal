// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Terminal;

public sealed partial class GhosttyVtProcessor
{
    private ITerminalNotificationHost? _notificationHost;
    private TerminalNotificationProtocol? _notifications;

    /// <inheritdoc />
    public ITerminalNotificationHost? NotificationHost
    {
        get => _notificationHost;
        set
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (ReferenceEquals(_notificationHost, value)) return;
            _notifications?.Dispose();
            _notifications = null;
            _notificationHost = value;
        }
    }

    private unsafe void OnNativeNotification(nint terminal, nint userdata, byte* metadata, nuint metadataLength,
        byte* payload, nuint payloadLength, byte kind)
    {
        try
        {
            if (kind == 2) { _notifications?.ResetParser(); return; }
            if (_disposed || kind > 1 || _notificationHost is not { } host || metadataLength > 8192 || payloadLength > 4096) return;
            if ((metadataLength > 0 && metadata == null) || (payloadLength > 0 && payload == null)) return;
            (_notifications ??= new(host, _timeProvider, bytes => ResponseCallback?.Invoke(bytes)))
                .Handle(new(metadata, (int)metadataLength), new(payload, (int)payloadLength), kind == 1);
        }
        catch (Exception)
        {
            // Neither user-host failures nor response callback exceptions may
            // unwind across the native parser's callback boundary.
        }
    }

    private void ClearSessionNotifications()
    {
        _notifications?.Dispose();
        _notifications = null;
    }
}
