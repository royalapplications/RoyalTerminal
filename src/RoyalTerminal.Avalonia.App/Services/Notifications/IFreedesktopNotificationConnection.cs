// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Avalonia.App.Services.Notifications;

internal sealed record FreedesktopNotification(string Application, uint ReplacesId, string Icon,
    string Title, string Body, string[] Actions, byte Urgency, int Expiry,
    string Category, string Sound, bool Silent, NotificationImage? Image);

internal sealed record NotificationImage(int Width, int Height, byte[] Rgba);

internal interface IFreedesktopNotificationConnection : IDisposable
{
    event Action<uint, string>? ActionInvoked;
    event Action<uint>? Closed;
    event Action? Disconnected;
    Task<string[]> ConnectAsync(CancellationToken cancellationToken);
    Task<uint> NotifyAsync(FreedesktopNotification notification, CancellationToken cancellationToken);
    Task CloseAsync(uint id, CancellationToken cancellationToken);
}
