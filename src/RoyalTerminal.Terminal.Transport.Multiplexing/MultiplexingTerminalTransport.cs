// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading.Channels;

namespace RoyalTerminal.Terminal.Transport.Multiplexing;

/// <summary>
/// Attaches a terminal to a durable block over an ordered, bounded WebSocket stream.
/// Detach never terminates the server process. Reconnection is explicit so hosts must
/// deliberately preserve VT state or request complete replay from zero.
/// </summary>
public sealed class MultiplexingTerminalTransport : ITerminalTransport, IAsyncDisposable
{
    private readonly IMultiplexingSocketConnector _connector;
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private Connection? _connection;
    private int _disposed;
    private long _lastSequence;

    /// <summary>Creates a transport using the system WebSocket implementation.</summary>
    public MultiplexingTerminalTransport() : this(new MultiplexingSocketConnector()) { }

    /// <summary>Creates a transport with an explicitly supplied network boundary.</summary>
    public MultiplexingTerminalTransport(IMultiplexingSocketConnector connector)
        => _connector = connector ?? throw new ArgumentNullException(nameof(connector));

    /// <inheritdoc />
    public event Action<byte[], int>? DataReceived;

    /// <inheritdoc />
    public event Action<int>? ProcessExited;

    /// <summary>
    /// Raised synchronously in stream order before subsequent output. The terminal host must
    /// apply this geometry to its VT processor before returning; render dispatch may be deferred.
    /// Ignoring this callback makes historical replay across resizes incorrect.
    /// </summary>
    public event Action<TerminalSessionDimensions>? AuthoritativeDimensionsChanged;

    /// <summary>
    /// Raised when an attachment ends unexpectedly. Null means an orderly socket close.
    /// The server process may still be running; this is deliberately separate from ProcessExited.
    /// </summary>
    public event Action<Exception?>? ConnectionLost;

    /// <inheritdoc />
    public bool IsRunning => _connection is { } connection && !connection.Lifetime.IsCancellationRequested
        && connection.Ready.Task.IsCompletedSuccessfully;

    /// <summary>Gets whether this attachment owns input and resize privileges.</summary>
    public bool HasControl => IsRunning && _connection is { HasControl: true };

    /// <summary>Gets the last event applied to the terminal, suitable for resuming preserved VT state.</summary>
    public long LastSequence => Interlocked.Read(ref _lastSequence);

    /// <summary>Gets the current attachment identity, or null before a successful handshake.</summary>
    public Guid? AttachmentId => _connection?.AttachmentId;

    /// <inheritdoc />
    public async ValueTask StartAsync(ITerminalTransportOptions options, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (options is not MultiplexingTransportOptions multiplexing)
        {
            throw new ArgumentException("Multiplexing transport options are required.", nameof(options));
        }

        Validate(multiplexing);
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        Connection? connection = null;
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            if (_connection is not null)
            {
                throw new InvalidOperationException("Stop the previous attachment before starting another one.");
            }

            using CancellationTokenSource handshake = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            handshake.CancelAfter(multiplexing.HandshakeTimeout);
            WebSocket socket = await _connector.ConnectAsync(MultiplexingEndpoint.Stream(multiplexing),
                multiplexing.AccessToken, handshake.Token).ConfigureAwait(false);
            connection = new Connection(socket, multiplexing);
            _connection = connection;
            Interlocked.Exchange(ref _lastSequence, multiplexing.AfterSequence);
            connection.ReaderTask = ReadLoopAsync(connection);
            connection.WriterTask = WriteLoopAsync(connection);
            await connection.Ready.Task.WaitAsync(handshake.Token).ConfigureAwait(false);
        }
        catch
        {
            if (connection is not null)
            {
                _connection = null;
                await StopConnectionAsync(connection).ConfigureAwait(false);
            }

            throw;
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    /// <inheritdoc />
    public void SendInput(ReadOnlySpan<byte> utf8)
    {
        if (utf8.IsEmpty || !IsRunning)
        {
            return;
        }

        Connection connection = _connection!;
        EnsureController(connection);
        if (utf8.Length > connection.Options.MaximumInputBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(utf8), "Input exceeds the configured per-operation limit.");
        }

        Enqueue(connection, new MultiplexingClientMessage("input", Convert.ToBase64String(utf8)));
    }

    /// <inheritdoc />
    public void Resize(TerminalSessionDimensions dimensions)
    {
        if (!IsRunning)
        {
            return;
        }

        ValidateDimensions(dimensions);
        Connection connection = _connection!;
        EnsureController(connection);
        Enqueue(connection, new MultiplexingClientMessage("resize", Columns: dimensions.Columns,
            Rows: dimensions.Rows, WidthPixels: dimensions.WidthPixels, HeightPixels: dimensions.HeightPixels));
    }

    /// <summary>Waits until all previously queued messages have been sent to the socket.</summary>
    /// <remarks>This is a transport flush, not a guarantee that a remote shell executed the input.</remarks>
    public async ValueTask FlushAsync(CancellationToken cancellationToken = default)
    {
        Connection? connection = _connection;
        if (connection is null || !IsRunning)
        {
            return;
        }

        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!connection.Outbound.Writer.TryWrite(new OutboundMessage(null, completion)))
        {
            throw new IOException("The bounded multiplexer send queue is full or closed.");
        }

        await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Detaches this client and releases its control lease without closing the block.</summary>
    public async ValueTask StopAsync()
    {
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            Connection? connection = _connection;
            _connection = null;
            if (connection is not null)
            {
                await StopConnectionAsync(connection).ConfigureAwait(false);
            }
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _ = StopAsync();
        }
    }

    /// <summary>Detaches and waits for all connection resources to be released.</summary>
    public async ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref _disposed, 1);
        await StopAsync().ConfigureAwait(false);
    }

    private async Task ReadLoopAsync(Connection connection)
    {
        Exception? fault = null;
        try
        {
            while (!connection.Lifetime.IsCancellationRequested)
            {
                MultiplexingServerMessage? message = await ReceiveAsync(connection).ConfigureAwait(false);
                if (message is null)
                {
                    break;
                }

                Apply(connection, message);
            }
        }
        catch (OperationCanceledException) when (connection.Lifetime.IsCancellationRequested) { }
        catch (WebSocketException) when (connection.ExplicitStop) { }
        catch (Exception exception)
        {
            fault = exception;
        }
        finally
        {
            fault ??= connection.Fault;
            connection.Ready.TrySetException(fault ?? new IOException("The socket closed before replay completed."));
            connection.HasControl = false;
            connection.Lifetime.Cancel();
            connection.Outbound.Writer.TryComplete();
            connection.Socket.Abort();
            if (!connection.ExplicitStop)
            {
                ConnectionLost?.Invoke(fault);
            }
        }
    }

    private void Apply(Connection connection, MultiplexingServerMessage message)
    {
        if (message.Type == "error" || message.Kind == "error")
        {
            throw new MultiplexingProtocolException(message.Code ?? "RemoteError", message.Message ?? "The host rejected the operation.");
        }

        if (message.Type == "attached")
        {
            if (connection.AttachmentId is not null || message.AttachmentId is null || message.AttachmentId == Guid.Empty)
            {
                throw new MultiplexingProtocolException("InvalidHandshake", "The attachment handshake is missing an identity or was repeated.");
            }

            connection.AttachmentId = message.AttachmentId;
            connection.HasControl = message.HasControl;
            return;
        }

        if (connection.AttachmentId is null)
        {
            throw new MultiplexingProtocolException("InvalidHandshake", "Terminal events arrived before attachment confirmation.");
        }

        if (message.Type == "ready")
        {
            if (message.Sequence != LastSequence || message.Sequence == 0)
            {
                throw new MultiplexingProtocolException("SequenceGap", "The replay fence does not match the last applied event.");
            }

            connection.Ready.TrySetResult();
            return;
        }

        long previous = LastSequence;
        if (message.Sequence <= 0)
        {
            throw new MultiplexingProtocolException("InvalidSequence", "Stream event sequences must be positive.");
        }

        if (message.Sequence <= previous)
        {
            return;
        }

        if (message.Sequence != checked(previous + 1))
        {
            throw new MultiplexingProtocolException("SequenceGap", "The stream skipped terminal events; replay cannot continue safely.");
        }

        if (previous == 0 && message.Kind != "resize")
        {
            throw new MultiplexingProtocolException("MissingGeometry", "Fresh replay must start with authoritative terminal dimensions.");
        }

        switch (message.Kind)
        {
            case "output":
                byte[] data = Convert.FromBase64String(message.Data
                    ?? throw new MultiplexingProtocolException("InvalidOutput", "Output has no byte payload."));
                DataReceived?.Invoke(data, data.Length);
                break;
            case "resize":
                TerminalSessionDimensions dimensions = message.Dimensions
                    ?? throw new MultiplexingProtocolException("InvalidResize", "Resize has no dimensions.");
                ValidateDimensions(dimensions);
                AuthoritativeDimensionsChanged?.Invoke(dimensions);
                break;
            case "exit":
                if (Interlocked.Exchange(ref connection.ExitRaised, 1) == 0)
                {
                    ProcessExited?.Invoke(message.ExitCode ?? -1);
                }
                break;
            case "closed":
                break;
            default:
                throw new MultiplexingProtocolException("UnknownEvent", "The host sent an unsupported stream event.");
        }

        Interlocked.Exchange(ref _lastSequence, message.Sequence);
    }

    private static async Task<MultiplexingServerMessage?> ReceiveAsync(Connection connection)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(Math.Min(8192, connection.Options.MaximumMessageBytes));
        try
        {
            using MemoryStream message = new();
            while (true)
            {
                ValueWebSocketReceiveResult result = await connection.Socket.ReceiveAsync(buffer.AsMemory(),
                    connection.Lifetime.Token).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return null;
                }

                if (result.MessageType != WebSocketMessageType.Text
                    || message.Length + result.Count > connection.Options.MaximumMessageBytes)
                {
                    throw new MultiplexingProtocolException("InvalidMessage", "Expected a bounded UTF-8 JSON text message.");
                }

                message.Write(buffer, 0, result.Count);
                if (result.EndOfMessage)
                {
                    return JsonSerializer.Deserialize(message.GetBuffer().AsSpan(0, checked((int)message.Length)),
                        MultiplexingClientJsonContext.Default.MultiplexingServerMessage)
                        ?? throw new MultiplexingProtocolException("InvalidMessage", "The host sent a null message.");
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static async Task WriteLoopAsync(Connection connection)
    {
        try
        {
            await connection.Ready.Task.WaitAsync(connection.Lifetime.Token).ConfigureAwait(false);
            await foreach (OutboundMessage message in connection.Outbound.Reader.ReadAllAsync(connection.Lifetime.Token).ConfigureAwait(false))
            {
                if (message.Payload is { } payload)
                {
                    await connection.Socket.SendAsync(payload.AsMemory(), WebSocketMessageType.Text, true,
                        connection.Lifetime.Token).ConfigureAwait(false);
                }

                message.Completion?.TrySetResult();
            }
        }
        catch (Exception exception)
        {
            if (!connection.Lifetime.IsCancellationRequested)
            {
                Interlocked.CompareExchange(ref connection.Fault, exception, null);
                connection.Lifetime.Cancel();
                connection.Socket.Abort();
            }
        }
        finally
        {
            while (connection.Outbound.Reader.TryRead(out OutboundMessage? pending))
            {
                pending.Completion?.TrySetException(new IOException("The attachment closed before the send queue was flushed."));
            }
        }
    }

    private static void Enqueue(Connection connection, MultiplexingClientMessage message)
    {
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(message, MultiplexingClientJsonContext.Default.MultiplexingClientMessage);
        if (!connection.Outbound.Writer.TryWrite(new OutboundMessage(json, null)))
        {
            throw new IOException("The bounded multiplexer send queue is full or closed; input was not accepted.");
        }
    }

    private static void EnsureController(Connection connection)
    {
        if (!connection.HasControl)
        {
            throw new InvalidOperationException("This attachment is an observer and cannot send input or resize the terminal.");
        }
    }

    private static async ValueTask StopConnectionAsync(Connection connection)
    {
        connection.ExplicitStop = true;
        connection.Outbound.Writer.TryComplete();
        connection.Lifetime.Cancel();
        connection.Socket.Abort();
        try
        {
            await Task.WhenAll(connection.ReaderTask, connection.WriterTask).ConfigureAwait(false);
        }
        finally
        {
            connection.Socket.Dispose();
            connection.Lifetime.Dispose();
        }
    }

    private static void Validate(MultiplexingTransportOptions options)
    {
        _ = MultiplexingEndpoint.Normalize(options.Endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.AccessToken);
        if (options.SessionId == Guid.Empty || options.BlockId == Guid.Empty || options.AfterSequence < 0
            || options.MaximumMessageBytes is < 1024 or > 16 * 1024 * 1024
            || options.MaximumInputBytes is < 1 or > 1024 * 1024
            || options.OutboundQueueCapacity is < 1 or > 4096
            || options.HandshakeTimeout <= TimeSpan.Zero || options.HandshakeTimeout > TimeSpan.FromMinutes(5))
        {
            throw new ArgumentException("Invalid multiplexer identity, cursor, or resource limit.", nameof(options));
        }

        ValidateDimensions(options.Dimensions);
    }

    private static void ValidateDimensions(TerminalSessionDimensions dimensions)
    {
        if (dimensions.Columns <= 0 || dimensions.Rows <= 0 || dimensions.WidthPixels < 0 || dimensions.HeightPixels < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dimensions), "Grid dimensions must be positive and pixel dimensions nonnegative.");
        }
    }

    private sealed record OutboundMessage(byte[]? Payload, TaskCompletionSource? Completion);

    private sealed class Connection(WebSocket socket, MultiplexingTransportOptions options)
    {
        public WebSocket Socket { get; } = socket;
        public MultiplexingTransportOptions Options { get; } = options;
        public CancellationTokenSource Lifetime { get; } = new();
        public Channel<OutboundMessage> Outbound { get; } = Channel.CreateBounded<OutboundMessage>(new BoundedChannelOptions(options.OutboundQueueCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false,
        });
        public TaskCompletionSource Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task ReaderTask { get; set; } = Task.CompletedTask;
        public Task WriterTask { get; set; } = Task.CompletedTask;
        public Guid? AttachmentId { get; set; }
        public volatile bool HasControl;
        public volatile bool ExplicitStop;
        public int ExitRaised;
        public Exception? Fault;
    }
}
