// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers;
using System.Net.WebSockets;
using System.Text.Json;
using RoyalTerminal.Multiplexing;
using RoyalTerminal.Terminal;

namespace RoyalTerminal.Multiplexing.Server;

internal sealed class BlockStreamEndpoint(MultiplexerBroker broker, AccessTokens tokens, ClientRegistry clients)
{
    private const int MaximumMessageBytes = 128 * 1024;

    public async Task HandleAsync(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest) throw new ApiException(400, "websocket_required", "Use a WebSocket connection.");
        Guid sessionId = ServerHttp.RouteId(context, "sessionId");
        Guid blockId = ServerHttp.RouteId(context, "blockId");
        if (!long.TryParse(context.Request.Query["after"].FirstOrDefault() ?? "0", out long after) || after < 0)
            throw new ArgumentException("after must be a nonnegative sequence number.");
        string role = context.Request.Query["role"].FirstOrDefault() ?? "observer";
        if (role is not ("observer" or "controller")) throw new ArgumentException("Invalid attachment role.");
        using WebSocket socket = await context.WebSockets.AcceptWebSocketAsync();
        using CancellationTokenSource lifetime = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        MultiplexerAttachment? attachment = null;
        try
        {
            AccessGrant? grant = null;
            string header = context.Request.Headers.Authorization.ToString();
            if (header.StartsWith("Bearer ", StringComparison.Ordinal)) grant = tokens.Resolve(header[7..]);
            if (grant is null)
            {
                // Browsers cannot set Authorization on WebSocket handshakes. Credentials stay out of URLs.
                using CancellationTokenSource handshake = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                handshake.CancelAfter(TimeSpan.FromSeconds(5));
                ClientFrame authentication = await ReceiveAsync(socket, handshake.Token);
                if (authentication.Type == "authenticate") grant = tokens.Resolve(authentication.Token);
            }
            if (grant is null) throw new ApiException(401, "unauthorized", "Authentication failed.");
            ServerHttp.RequireSession(grant, sessionId);
            if (role == "controller" && !grant.CanControl) throw new ApiException(403, "forbidden", "This share is read only.");
            if (grant.ExpiresUtc != DateTimeOffset.MaxValue)
                lifetime.CancelAfter(grant.ExpiresUtc - DateTimeOffset.UtcNow);
            attachment = broker.Attach(sessionId, blockId, role == "controller" ? MultiplexerRole.Controller : MultiplexerRole.Observer, after);
            clients.Add(attachment.Id, sessionId, blockId, grant, attachment.HasControl, lifetime);
            await SendAsync(socket, new() { Type = "attached", AttachmentId = attachment.Id, HasControl = attachment.HasControl }, lifetime.Token);
            TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Task output = SendEventsAsync(socket, attachment, after, ready, lifetime.Token);
            Task input = ReceiveInputAsync(socket, attachment, grant, ready.Task, lifetime.Token);
            Task completed = await Task.WhenAny(output, input);
            await lifetime.CancelAsync();
            try { await Task.WhenAll(output, input); }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested && completed.IsCompletedSuccessfully) { }
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            if (error is not (OperationCanceledException or WebSocketException))
            {
                (_, ApiError api) = ServerHttp.Classify(error);
                using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(2));
                try { await SendAsync(socket, new() { Type = "error", Code = api.Code, Message = api.Message }, timeout.Token); }
                catch (Exception sendingError) when (sendingError is OperationCanceledException or WebSocketException or ObjectDisposedException) { }
            }
        }
        finally
        {
            if (attachment is not null) { clients.Remove(attachment.Id); attachment.Dispose(); }
            await lifetime.CancelAsync();
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(2));
                try { await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Detached", timeout.Token); }
                catch (Exception error) when (error is OperationCanceledException or WebSocketException) { }
            }
        }
    }

    private static async Task SendEventsAsync(WebSocket socket, MultiplexerAttachment attachment, long after,
        TaskCompletionSource ready, CancellationToken cancellationToken)
    {
        long fence = attachment.ReplayThroughSequence;
        if (after == fence)
        {
            await SendAsync(socket, new() { Type = "ready", Sequence = fence }, cancellationToken);
            ready.TrySetResult();
        }
        try
        {
            await foreach (MultiplexerEvent item in attachment.Events.ReadAllAsync(cancellationToken))
            {
                await SendAsync(socket, new() { Sequence = item.Sequence, Kind = item.Kind.ToString().ToLowerInvariant(),
                    Data = item.Data, Dimensions = item.Dimensions, ExitCode = item.ExitCode, Message = item.Message }, cancellationToken);
                if (item.Sequence == fence && !ready.Task.IsCompleted)
                {
                    await SendAsync(socket, new() { Type = "ready", Sequence = fence }, cancellationToken);
                    ready.TrySetResult();
                }
            }
        }
        finally { ready.TrySetCanceled(cancellationToken.IsCancellationRequested ? cancellationToken : new CancellationToken(true)); }
    }

    private async Task ReceiveInputAsync(WebSocket socket, MultiplexerAttachment attachment, AccessGrant grant,
        Task ready, CancellationToken cancellationToken)
    {
        await ready.WaitAsync(cancellationToken);
        while (!cancellationToken.IsCancellationRequested)
        {
            ClientFrame frame = await ReceiveAsync(socket, cancellationToken);
            if (!tokens.IsActive(grant)) throw new ApiException(403, "expired", "The capability expired or was revoked.");
            switch (frame.Type)
            {
                case "input":
                    byte[] data = Convert.FromBase64String(frame.Data ?? throw new ArgumentException("Input data is required."));
                    if (data.Length > 64 * 1024) throw new ArgumentException("Input exceeds 64 KiB.");
                    attachment.SendInput(data);
                    break;
                case "resize":
                    attachment.Resize(new TerminalSessionDimensions(frame.Columns ?? 0, frame.Rows ?? 0,
                        frame.WidthPixels ?? 0, frame.HeightPixels ?? 0));
                    break;
                case "detach": return;
                default: throw new ArgumentException("Unknown client message type.");
            }
        }
    }

    private static ValueTask SendAsync(WebSocket socket, ServerFrame frame, CancellationToken cancellationToken)
        => socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(frame, ServerJsonContext.Default.ServerFrame).AsMemory(),
            WebSocketMessageType.Text, true, cancellationToken);

    private static async ValueTask<ClientFrame> ReceiveAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        ArrayBufferWriter<byte> message = new();
        while (true)
        {
            if (message.WrittenCount >= MaximumMessageBytes) throw new ArgumentException("WebSocket message exceeds 128 KiB.");
            Memory<byte> memory = message.GetMemory(Math.Min(4096, MaximumMessageBytes - message.WrittenCount));
            ValueWebSocketReceiveResult result = await socket.ReceiveAsync(memory[..Math.Min(memory.Length, MaximumMessageBytes - message.WrittenCount)], cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close) throw new OperationCanceledException("Client detached.", cancellationToken);
            if (result.MessageType != WebSocketMessageType.Text) throw new ArgumentException("Only JSON text frames are supported.");
            message.Advance(result.Count);
            if (!result.EndOfMessage) continue;
            return JsonSerializer.Deserialize(message.WrittenSpan, ServerJsonContext.Default.ClientFrame)
                ?? throw new ArgumentException("A JSON message object is required.");
        }
    }
}
