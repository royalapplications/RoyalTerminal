// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Tmds.DBus.Protocol;

namespace RoyalTerminal.Avalonia.App.Services.Notifications;

// Explicit wire serialization: no dynamic proxy, reflection or shell process.
// Pin calls/signals to one unique daemon owner so reused IDs after a daemon
// restart can never activate or close another connection's notifications.
internal sealed class FreedesktopNotificationConnection : IFreedesktopNotificationConnection
{
    private const string Service = "org.freedesktop.Notifications", Path = "/org/freedesktop/Notifications";
    private readonly DBusConnection _connection;
    private readonly List<IDisposable> _subscriptions = new();
    private string _owner = Service;
    private int _disconnected;
    public event Action<uint, string>? ActionInvoked;
    public event Action<uint>? Closed;
    public event Action? Disconnected;

    internal FreedesktopNotificationConnection(string? address = null)
        => _connection = new(new DBusConnectionOptions(address ?? DBusAddress.Session ?? throw new InvalidOperationException("No session bus.")) { AutoConnect = false });

    public async Task<string[]> ConnectAsync(CancellationToken cancellationToken)
    {
        await BoundedAsync(_connection.ConnectAsync().AsTask(), cancellationToken).ConfigureAwait(false);
        // Calling the well-known name activates the desktop service when needed.
        await CapabilitiesAsync(cancellationToken).ConfigureAwait(false);
        _owner = await BoundedAsync(_connection.CallMethodAsync(OwnerMessage(), static (message, _) => message.GetBodyReader().ReadString()), cancellationToken).ConfigureAwait(false);
        _subscriptions.Add(await BoundedAsync(_connection.AddMatchAsync(new MatchRule
        { Type = MessageType.Signal, Sender = _owner, Path = Path, Interface = Service, Member = "ActionInvoked" },
            static (message, _) => { Reader reader = message.GetBodyReader(); return (reader.ReadUInt32(), reader.ReadString()); },
            notification =>
            {
                if (notification.Type == NotificationType.Value) SafeSignal(() => ActionInvoked?.Invoke(notification.Value.Item1, notification.Value.Item2));
                else SignalDisconnected();
            }, emitOnCapturedContext: false, flags: ObserverFlags.EmitOnConnectionClosed).AsTask(), cancellationToken).ConfigureAwait(false));
        _subscriptions.Add(await BoundedAsync(_connection.AddMatchAsync(new MatchRule
        { Type = MessageType.Signal, Sender = _owner, Path = Path, Interface = Service, Member = "NotificationClosed" },
            static (message, _) => { Reader reader = message.GetBodyReader(); uint id = reader.ReadUInt32(); _ = reader.ReadUInt32(); return id; },
            notification =>
            {
                if (notification.Type == NotificationType.Value) SafeSignal(() => Closed?.Invoke(notification.Value));
                else SignalDisconnected();
            }, emitOnCapturedContext: false, flags: ObserverFlags.EmitOnConnectionClosed).AsTask(), cancellationToken).ConfigureAwait(false));
        _subscriptions.Add(await BoundedAsync(_connection.AddMatchAsync(new MatchRule
        { Type = MessageType.Signal, Sender = "org.freedesktop.DBus", Path = "/org/freedesktop/DBus", Interface = "org.freedesktop.DBus", Member = "NameOwnerChanged", Arg0 = Service },
            static (message, _) => { Reader reader = message.GetBodyReader(); _ = reader.ReadString(); _ = reader.ReadString(); return reader.ReadString(); },
            notification => { if (notification.Type != NotificationType.Value || notification.Value != _owner) SignalDisconnected(); },
            emitOnCapturedContext: false, flags: ObserverFlags.EmitOnConnectionClosed).AsTask(), cancellationToken).ConfigureAwait(false));
        // The unique owner may have disappeared between lookup and subscription.
        return await CapabilitiesAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task<uint> NotifyAsync(FreedesktopNotification notification, CancellationToken cancellationToken)
        => BoundedAsync(_connection.CallMethodAsync(NotifyMessage(notification), static (message, _) => message.GetBodyReader().ReadUInt32()), cancellationToken);

    public Task CloseAsync(uint id, CancellationToken cancellationToken)
    {
        using MessageWriter writer = _connection.GetMessageWriter();
        writer.WriteMethodCallHeader(_owner, Path, Service, "CloseNotification", "u");
        writer.WriteUInt32(id);
        return BoundedAsync(_connection.CallMethodAsync(writer.CreateMessage()), cancellationToken);
    }

    private Task<string[]> CapabilitiesAsync(CancellationToken token)
    {
        using MessageWriter writer = _connection.GetMessageWriter();
        writer.WriteMethodCallHeader(_owner, Path, Service, "GetCapabilities");
        return BoundedAsync(_connection.CallMethodAsync(writer.CreateMessage(), static (message, _) => message.GetBodyReader().ReadArrayOfString()), token);
    }

    private MessageBuffer OwnerMessage()
    {
        using MessageWriter writer = _connection.GetMessageWriter();
        writer.WriteMethodCallHeader("org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus", "GetNameOwner", "s");
        writer.WriteString(Service);
        return writer.CreateMessage();
    }

    private MessageBuffer NotifyMessage(FreedesktopNotification notification)
    {
        MessageWriter writer = _connection.GetMessageWriter();
        try
        {
            writer.WriteMethodCallHeader(_owner, Path, Service, "Notify", "susssasa{sv}i");
            WriteNotification(ref writer, notification);
            return writer.CreateMessage();
        }
        finally { writer.Dispose(); }
    }

    internal static void WriteNotification(ref MessageWriter writer, FreedesktopNotification notification)
    {
        writer.WriteString(notification.Application);
        writer.WriteUInt32(notification.ReplacesId);
        writer.WriteString(notification.Icon);
        writer.WriteString(notification.Title);
        writer.WriteString(notification.Body);
        writer.WriteArray(notification.Actions);
        ArrayStart hints = writer.WriteDictionaryStart();
        writer.WriteDictionaryEntryStart(); writer.WriteString("urgency"); writer.WriteVariantByte(notification.Urgency);
        writer.WriteDictionaryEntryStart(); writer.WriteString("suppress-sound"); writer.WriteVariantBool(notification.Silent);
        if (notification.Category.Length > 0)
        { writer.WriteDictionaryEntryStart(); writer.WriteString("category"); writer.WriteVariantString(notification.Category); }
        if (notification.Sound.Length > 0)
        { writer.WriteDictionaryEntryStart(); writer.WriteString("sound-name"); writer.WriteVariantString(notification.Sound); }
        if (notification.Image is { } image)
        {
            writer.WriteDictionaryEntryStart(); writer.WriteString("image-data"); writer.WriteSignature("(iiibiiay)");
            writer.WriteStructureStart();
            writer.WriteInt32(image.Width); writer.WriteInt32(image.Height); writer.WriteInt32(image.Width * 4);
            writer.WriteBool(true); writer.WriteInt32(8); writer.WriteInt32(4); writer.WriteArray(image.Rgba);
        }
        writer.WriteDictionaryEnd(hints);
        writer.WriteInt32(notification.Expiry);
    }

    private async Task<T> BoundedAsync<T>(Task<T> operation, CancellationToken token)
    {
        try { return await operation.WaitAsync(TimeSpan.FromSeconds(5), token).ConfigureAwait(false); }
        catch (TimeoutException) { Abort(); await ObserveAsync(operation).ConfigureAwait(false); throw; }
        catch (OperationCanceledException) { Abort(); await ObserveAsync(operation).ConfigureAwait(false); throw; }
    }

    private async Task BoundedAsync(Task operation, CancellationToken token)
    {
        try { await operation.WaitAsync(TimeSpan.FromSeconds(5), token).ConfigureAwait(false); }
        catch (TimeoutException) { Abort(); await ObserveAsync(operation).ConfigureAwait(false); throw; }
        catch (OperationCanceledException) { Abort(); await ObserveAsync(operation).ConfigureAwait(false); throw; }
    }

    private static async Task ObserveAsync(Task operation) { try { await operation.ConfigureAwait(false); } catch (Exception) { } }
    private static void SafeSignal(Action callback) { try { callback(); } catch (Exception) { } }
    private void SignalDisconnected() { if (Interlocked.Exchange(ref _disconnected, 1) == 0) SafeSignal(() => Disconnected?.Invoke()); }
    private void Abort() { SignalDisconnected(); _connection.Dispose(); }

    public void Dispose()
    {
        Interlocked.Exchange(ref _disconnected, 1);
        foreach (IDisposable subscription in _subscriptions) subscription.Dispose();
        _subscriptions.Clear();
        _connection.Dispose();
    }
}
