// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Diagnostics.CodeAnalysis;
using System.Threading.Channels;
using RoyalTerminal.Terminal;

namespace RoyalTerminal.Multiplexing;

/// <summary>A bounded subscription to a server-owned block. Disposal detaches without stopping it.</summary>
public sealed class MultiplexerAttachment : IDisposable
{
    private readonly MultiplexerBlock _block;
    private readonly Channel<MultiplexerEvent> _channel;
    private readonly int _maxPendingBytes;
    private readonly ColdReplayArchive.Cursor? _prefix;
    private long _pendingBytes;
    private int _completed;

    internal MultiplexerAttachment(MultiplexerBlock block, MultiplexerRole role, long replayThroughSequence,
        MultiplexerOptions options, ColdReplayArchive.Cursor? prefix = null)
    {
        _block = block; Role = role; ReplayThroughSequence = replayThroughSequence; _prefix = prefix;
        _maxPendingBytes = options.MaxPendingBytesPerAttachment;
        _channel = Channel.CreateBounded<MultiplexerEvent>(new BoundedChannelOptions(options.MaxPendingEventsPerAttachment)
        { AllowSynchronousContinuations = false, FullMode = BoundedChannelFullMode.Wait, SingleReader = false, SingleWriter = true });
        Events = new AccountingReader(this, _channel.Reader, prefix);
    }
    /// <summary>Stable identity of this attachment.</summary>
    public Guid Id { get; } = Guid.NewGuid();
    /// <summary>Owning session identity.</summary>
    public Guid SessionId => _block.SessionId;
    /// <summary>Attached block identity.</summary>
    public Guid BlockId => _block.Id;
    /// <summary>Host-authorized role; role assignment is not an authentication mechanism.</summary>
    public MultiplexerRole Role { get; }
    /// <summary>Last event included in the atomic attach snapshot. Events after this cursor are live.</summary>
    public long ReplayThroughSequence { get; }
    /// <summary>Whether this attachment currently owns input and resize authority.</summary>
    public bool HasControl => Role == MultiplexerRole.Controller && IsConnected;
    /// <summary>Whether new operations may be submitted.</summary>
    public bool IsConnected => Volatile.Read(ref _completed) == 0;
    /// <summary>Cold replay, hot replay and live events in sequence. A slow reader fails explicitly.</summary>
    public ChannelReader<MultiplexerEvent> Events { get; }
    /// <summary>Sends bytes once through the exclusive controller; input is never automatically replayed.</summary>
    public void SendInput(ReadOnlySpan<byte> bytes) => _block.SendInput(this, bytes);
    /// <summary>Changes shared terminal geometry through the exclusive controller.</summary>
    public void Resize(TerminalSessionDimensions dimensions) => _block.Resize(this, dimensions);
    /// <summary>Discards unread history without inflating it and releases this subscription.</summary>
    public void Dispose()
    {
        _prefix?.Discard(); _block.Detach(this);
        while (Events.TryRead(out _)) { }
    }
    internal bool TryPublish(MultiplexerEvent item)
    {
        if (!IsConnected) return false;
        int bytes = item.Data?.Length ?? 0;
        if (Interlocked.Add(ref _pendingBytes, bytes) > _maxPendingBytes) { Interlocked.Add(ref _pendingBytes, -bytes); return false; }
        MultiplexerEvent owned = item.Data is { } data ? item with { Data = (byte[])data.Clone() } : item;
        if (_channel.Writer.TryWrite(owned)) return true;
        Interlocked.Add(ref _pendingBytes, -bytes); return false;
    }
    internal void Complete(Exception? error = null)
    {
        if (Interlocked.Exchange(ref _completed, 1) == 0) _channel.Writer.TryComplete(error);
    }
    private sealed class AccountingReader(MultiplexerAttachment attachment, ChannelReader<MultiplexerEvent> inner, ColdReplayArchive.Cursor? prefix) : ChannelReader<MultiplexerEvent>
    {
        private int _released;
        public override Task Completion { get; } = prefix is null ? inner.Completion : Task.WhenAll(prefix.Completion, inner.Completion);
        public override bool TryRead([MaybeNullWhen(false)] out MultiplexerEvent item)
        {
            if (prefix is not null && prefix.TryRead(out item))
            {
                if (!prefix.Available && Interlocked.Exchange(ref _released, 1) == 0) attachment._block.ReleaseColdReader(attachment.Id);
                return true;
            }
            if (!inner.TryRead(out item)) return false;
            Interlocked.Add(ref attachment._pendingBytes, -(item.Data?.Length ?? 0)); return true;
        }
        public override ValueTask<bool> WaitToReadAsync(CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested) return ValueTask.FromCanceled<bool>(cancellationToken);
            return prefix?.Available == true ? new(true) : inner.WaitToReadAsync(cancellationToken);
        }
    }
}
