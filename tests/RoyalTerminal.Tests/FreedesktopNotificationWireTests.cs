// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using RoyalTerminal.Avalonia.App.Services.Notifications;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class FreedesktopNotificationWireTests
{
    [Fact]
    public async Task RealConnectionWritesStandardNotifySignatureHintsAndPinsUniqueOwner()
    {
        await using Peer peer = new();
        using FreedesktopNotificationConnection connection = new(peer.Address);
        string[] capabilities = await connection.ConnectAsync(default).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(new[] { "body", "actions", "sound" }, capabilities);
        uint id = await connection.NotifyAsync(new("RoyalTerminal", 0, "dialog-information", "literal <title>", "escaped &lt;body&gt;",
            ["default", "Open", "1", "Yes"], 2, 1234, "job", "dialog-warning", true,
            new NotificationImage(1, 1, new byte[] { 10, 20, 30, 128 }), "/usr/share/sounds/freedesktop/stereo/dialog-warning.oga"), default).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(42u, id);
        await connection.CloseAsync(id, default).WaitAsync(TimeSpan.FromSeconds(5));
        WireMessage notification = await peer.Notification.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(":1.9", notification.Headers[6]);
        Assert.Equal("susssasa{sv}i", notification.Headers[8]);
        Cursor body = new(notification.Body);
        Assert.Equal("RoyalTerminal", body.String());
        Assert.Equal(0u, body.UInt());
        Assert.Equal("dialog-information", body.String());
        Assert.Equal("literal <title>", body.String());
        Assert.Equal("escaped &lt;body&gt;", body.String());
        int actionLength = checked((int)body.UInt());
        int actionEnd = body.Position + actionLength;
        Assert.Equal("default", body.String()); Assert.Equal("Open", body.String());
        Assert.Equal("1", body.String()); Assert.Equal("Yes", body.String());
        Assert.Equal(actionEnd, body.Position);
        int hintsLength = checked((int)body.UInt()); body.Align(8);
        int hintsEnd = body.Position + hintsLength;
        body.Align(8); Assert.Equal("urgency", body.String()); Assert.Equal("y", body.Signature()); Assert.Equal(2, body.Byte());
        body.Align(8); Assert.Equal("suppress-sound", body.String()); Assert.Equal("b", body.Signature()); Assert.Equal(1u, body.UInt());
        body.Align(8); Assert.Equal("category", body.String()); Assert.Equal("s", body.Signature()); Assert.Equal("job", body.String());
        body.Align(8); Assert.Equal("sound-name", body.String()); Assert.Equal("s", body.Signature()); Assert.Equal("dialog-warning", body.String());
        body.Align(8); Assert.Equal("sound-file", body.String()); Assert.Equal("s", body.Signature()); Assert.Equal("/usr/share/sounds/freedesktop/stereo/dialog-warning.oga", body.String());
        body.Align(8); Assert.Equal("image-data", body.String()); Assert.Equal("(iiibiiay)", body.Signature()); body.Align(8);
        Assert.Equal(1u, body.UInt()); Assert.Equal(1u, body.UInt()); Assert.Equal(4u, body.UInt());
        Assert.Equal(1u, body.UInt()); Assert.Equal(8u, body.UInt()); Assert.Equal(4u, body.UInt());
        Assert.Equal(4u, body.UInt());
        Assert.Equal(new byte[] { 10, 20, 30, 128 }, body.Bytes(4));
        Assert.Equal(hintsEnd, body.Position);
        Assert.Equal(1234u, body.UInt());
        Assert.Equal(42u, await peer.Closed.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    // An isolated loopback D-Bus peer, not the user's desktop/session bus. It
    // exercises the actual library transport and serializer without reflection.
    private sealed class Peer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _run;
        internal readonly TaskCompletionSource<WireMessage> Notification = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource<uint> Closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal string Address { get; }
        internal Peer()
        {
            _listener.Start();
            Address = $"tcp:host=127.0.0.1,port={((IPEndPoint)_listener.LocalEndpoint).Port}";
            _run = RunAsync();
        }
        private async Task RunAsync()
        {
            try
            {
                using TcpClient client = await _listener.AcceptTcpClientAsync(_stop.Token);
                using NetworkStream stream = client.GetStream();
                while (true)
                {
                    string line = await ReadLineAsync(stream, _stop.Token);
                    if (line == "BEGIN") break;
                    string reply = line.TrimStart('\0').StartsWith("AUTH", StringComparison.Ordinal)
                        ? "OK 0123456789abcdef0123456789abcdef\r\n" : "ERROR unsupported\r\n";
                    await stream.WriteAsync(Encoding.ASCII.GetBytes(reply), _stop.Token);
                }
                uint serial = 1;
                while (!_stop.IsCancellationRequested)
                {
                    WireMessage message = await ReadMessageAsync(stream, _stop.Token);
                    string member = message.Headers.GetValueOrDefault((byte)3, "");
                    string signature = "";
                    byte[] body = [];
                    switch (member)
                    {
                        case "Hello": signature = "s"; body = StringBody(":1.23"); break;
                        case "GetNameOwner": signature = "s"; body = StringBody(":1.9"); break;
                        case "GetCapabilities":
                            signature = "as";
                            using (MemoryStream values = new())
                            {
                                using BinaryWriter writer = new(values, Encoding.UTF8, leaveOpen: true);
                                writer.Write(0u);
                                WriteString(writer, "body"); WriteString(writer, "actions"); WriteString(writer, "sound");
                                body = values.ToArray();
                                BinaryPrimitives.WriteUInt32LittleEndian(body, (uint)body.Length - 4);
                            }
                            break;
                        case "AddMatch": case "RemoveMatch": break;
                        case "Notify": signature = "u"; body = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(body, 42); Notification.TrySetResult(message); break;
                        case "CloseNotification": Closed.TrySetResult(new Cursor(message.Body).UInt()); break;
                        default: throw new InvalidOperationException("Unexpected D-Bus method: " + member);
                    }
                    await stream.WriteAsync(Reply(message.Serial, serial++, signature, body), _stop.Token);
                }
            }
            catch (Exception exception) when (exception is OperationCanceledException or EndOfStreamException or IOException or ObjectDisposedException) { }
            catch (Exception exception) { Notification.TrySetException(exception); Closed.TrySetException(exception); throw; }
        }

        public async ValueTask DisposeAsync()
        {
            _stop.Cancel(); _listener.Stop();
            await _run.WaitAsync(TimeSpan.FromSeconds(5));
            _stop.Dispose();
        }
    }

    private sealed record WireMessage(uint Serial, Dictionary<byte, string> Headers, byte[] Body);

    private static async Task<string> ReadLineAsync(Stream stream, CancellationToken token)
    {
        byte[] one = new byte[1];
        StringBuilder line = new();
        while (line.Length < 4096)
        {
            await stream.ReadExactlyAsync(one, token);
            if (one[0] == '\n') return line.ToString().TrimEnd('\r');
            line.Append((char)one[0]);
        }
        throw new IOException("Oversized authentication line.");
    }

    private static async Task<WireMessage> ReadMessageAsync(Stream stream, CancellationToken token)
    {
        byte[] header = new byte[16];
        await stream.ReadExactlyAsync(header, token);
        Assert.Equal((byte)'l', header[0]);
        int bodyLength = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4)));
        uint serial = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(8));
        int headerLength = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(12)));
        Assert.InRange(bodyLength + headerLength, 0, 8 * 1024 * 1024);
        byte[] fields = new byte[(headerLength + 7) & ~7];
        await stream.ReadExactlyAsync(fields, token);
        Cursor cursor = new(fields);
        Dictionary<byte, string> headers = new();
        while (cursor.Position < headerLength)
        {
            cursor.Align(8);
            byte key = cursor.Byte();
            string type = cursor.Signature();
            headers[key] = type switch { "s" or "o" => cursor.String(), "g" => cursor.Signature(), "u" => cursor.UInt().ToString(), _ => throw new IOException("Unexpected header type.") };
        }
        byte[] body = new byte[bodyLength];
        await stream.ReadExactlyAsync(body, token);
        return new(serial, headers, body);
    }

    private static byte[] Reply(uint replySerial, uint serial, string signature, byte[] body)
    {
        using MemoryStream bytes = new();
        using BinaryWriter writer = new(bytes, Encoding.UTF8, leaveOpen: true);
        writer.Write((byte)'l'); writer.Write((byte)2); writer.Write((byte)0); writer.Write((byte)1);
        writer.Write((uint)body.Length); writer.Write(serial); writer.Write(0u);
        writer.Write((byte)5); WriteSignature(writer, "u"); Pad(writer, 4); writer.Write(replySerial);
        if (signature.Length > 0) { Pad(writer, 8); writer.Write((byte)8); WriteSignature(writer, "g"); WriteSignature(writer, signature); }
        uint fieldsLength = (uint)bytes.Length - 16;
        Pad(writer, 8); writer.Write(body);
        byte[] result = bytes.ToArray(); BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(12), fieldsLength);
        return result;
    }

    private static byte[] StringBody(string value)
    {
        using MemoryStream stream = new(); using BinaryWriter writer = new(stream);
        WriteString(writer, value); return stream.ToArray();
    }
    private static void WriteString(BinaryWriter writer, string value) { Pad(writer, 4); byte[] bytes = Encoding.UTF8.GetBytes(value); writer.Write((uint)bytes.Length); writer.Write(bytes); writer.Write((byte)0); }
    private static void WriteSignature(BinaryWriter writer, string value) { writer.Write((byte)value.Length); writer.Write(Encoding.ASCII.GetBytes(value)); writer.Write((byte)0); }
    private static void Pad(BinaryWriter writer, int alignment) { while (writer.BaseStream.Position % alignment != 0) writer.Write((byte)0); }

    private sealed class Cursor(byte[] bytes)
    {
        internal int Position;
        internal void Align(int alignment) => Position = (Position + alignment - 1) & ~(alignment - 1);
        internal byte Byte() => bytes[Position++];
        internal byte[] Bytes(int count) { byte[] result = bytes.AsSpan(Position, count).ToArray(); Position += count; return result; }
        internal uint UInt() { Align(4); uint value = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(Position)); Position += 4; return value; }
        internal string String() { int length = checked((int)UInt()); string value = Encoding.UTF8.GetString(bytes, Position, length); Position += length + 1; return value; }
        internal string Signature() { int length = Byte(); string value = Encoding.ASCII.GetString(bytes, Position, length); Position += length + 1; return value; }
    }
}
