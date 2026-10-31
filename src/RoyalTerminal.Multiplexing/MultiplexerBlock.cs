// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Terminal;

namespace RoyalTerminal.Multiplexing;

internal sealed class MultiplexerBlock
{
    private readonly object _gate = new();
    private readonly ITerminalTransport _transport;
    private readonly IMultiplexerResizeSource? _resizeSource;
    private readonly MultiplexerOptions _options;
    private readonly Queue<MultiplexerEvent> _journal = new();
    private readonly ColdReplayArchive? _cold;
    private readonly Dictionary<Guid, MultiplexerAttachment> _attachments = new();
    private readonly HashSet<Guid> _coldReaders = [];
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly SemaphoreSlim _operations = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private string _label;
    private TerminalSessionDimensions _dimensions;
    private MultiplexerBlockState _state = MultiplexerBlockState.Starting;
    private Guid? _controllerId;
    private int? _exitCode;
    private long _sequence, _journalBytes;
    private Task? _closeTask;
    private bool _closing;

    internal MultiplexerBlock(Guid sessionId, Guid windowId, string label, ITerminalTransport transport, TerminalSessionDimensions dimensions, MultiplexerOptions options)
    {
        SessionId = sessionId; WindowId = windowId; _label = label; _transport = transport; _resizeSource = transport as IMultiplexerResizeSource;
        _dimensions = dimensions; _options = options;
        if (options.MaxColdReplayBytes > 0) _cold = new(options.MaxColdReplayBytes);
        _transport.DataReceived += OnData; _transport.ProcessExited += OnExit;
        if (_resizeSource is not null) _resizeSource.DimensionsChanged += OnDimensionsChanged;
        Publish(new(++_sequence, MultiplexerEventKind.Resize, Dimensions: dimensions));
    }
    internal Guid Id { get; } = Guid.NewGuid();
    internal Guid SessionId { get; }
    internal Guid WindowId { get; }

    internal async ValueTask StartAsync(ITerminalTransportOptions options, CancellationToken cancellationToken)
    {
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        await _lifecycle.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            linked.Token.ThrowIfCancellationRequested(); await _transport.StartAsync(options, linked.Token).ConfigureAwait(false); linked.Token.ThrowIfCancellationRequested();
            lock (_gate) { if (_state == MultiplexerBlockState.Starting) _state = MultiplexerBlockState.Running; }
        }
        finally { _lifecycle.Release(); }
    }

    internal MultiplexerBlockDescriptor Describe()
    {
        lock (_gate)
        {
            return new(Id, SessionId, WindowId, _label, _state, _dimensions, _exitCode, _sequence,
                _cold?.FirstSequence ?? (_journal.Count == 0 ? _sequence + 1 : _journal.Peek().Sequence), _attachments.Count, _controllerId);
        }
    }
    internal void Rename(string label) { lock (_gate) _label = label; }

    internal MultiplexerAttachment Attach(MultiplexerRole role, long lastSequence)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(lastSequence);
        if (!Enum.IsDefined(role)) throw new ArgumentOutOfRangeException(nameof(role));
        lock (_gate)
        {
            if (_closing || _state == MultiplexerBlockState.Closed) throw new MultiplexerException(MultiplexerErrorCode.Disconnected, "The block has been closed.");
            if (lastSequence > _sequence) throw new MultiplexerException(MultiplexerErrorCode.Conflict, "The replay cursor is ahead of this block.");
            long firstHot = _journal.Count == 0 ? _sequence + 1 : _journal.Peek().Sequence;
            if (lastSequence < (_cold?.FirstSequence ?? firstHot) - 1)
                throw new MultiplexerException(MultiplexerErrorCode.HistoryGap, "Requested events were evicted. A fresh terminal cannot reconstruct its state from a truncated VT stream.");
            if (_attachments.Count >= _options.MaxAttachmentsPerBlock || (lastSequence < firstHot - 1 && _coldReaders.Count >= _options.MaxAttachmentsPerBlock))
                throw new MultiplexerException(MultiplexerErrorCode.CapacityExceeded, "This block has reached its attachment limit.");
            if (role == MultiplexerRole.Controller && _controllerId is not null) throw new MultiplexerException(MultiplexerErrorCode.Conflict, "Another attachment controls this block.");
            ColdReplayArchive.Cursor? prefix = _cold?.SnapshotAfter(lastSequence, firstHot);
            MultiplexerAttachment attachment = new(this, role, _sequence, _options, prefix);
            foreach (MultiplexerEvent item in _journal)
            {
                if (item.Sequence > lastSequence && !attachment.TryPublish(item))
                {
                    prefix?.Discard(); attachment.Complete();
                    throw new MultiplexerException(MultiplexerErrorCode.CapacityExceeded, "Retained hot replay exceeds this attachment's configured queue budget.");
                }
            }
            if (prefix is not null) _coldReaders.Add(attachment.Id);
            if (_state == MultiplexerBlockState.Exited) { attachment.Complete(); return attachment; }
            _attachments.Add(attachment.Id, attachment);
            if (role == MultiplexerRole.Controller) _controllerId = attachment.Id;
            return attachment;
        }
    }

    internal void ReleaseColdReader(Guid id) { lock (_gate) _coldReaders.Remove(id); }
    internal void Detach(MultiplexerAttachment attachment)
    {
        lock (_gate)
        {
            _attachments.Remove(attachment.Id); _coldReaders.Remove(attachment.Id);
            if (_controllerId == attachment.Id) _controllerId = null;
            attachment.Complete();
        }
    }
    internal void SendInput(MultiplexerAttachment attachment, ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length > _options.MaxInputBytes) throw new MultiplexerException(MultiplexerErrorCode.CapacityExceeded, "Input exceeds the configured operation limit.");
        AcquireOperation(attachment);
        try { lock (_gate) RequireController(attachment); if (!bytes.IsEmpty) _transport.SendInput(bytes); }
        finally { _operations.Release(); }
    }
    internal void Resize(MultiplexerAttachment attachment, TerminalSessionDimensions dimensions)
    {
        ValidateDimensions(dimensions, _options); AcquireOperation(attachment);
        try
        {
            TerminalSessionDimensions previous;
            lock (_gate)
            {
                RequireController(attachment); if (_dimensions == dimensions) return; previous = _dimensions;
                if (_resizeSource is null) { _dimensions = dimensions; Publish(new(++_sequence, MultiplexerEventKind.Resize, Dimensions: dimensions)); }
            }
            try { _transport.Resize(dimensions); }
            catch
            {
                if (_resizeSource is null) lock (_gate) { _dimensions = previous; Publish(new(++_sequence, MultiplexerEventKind.Resize, Dimensions: previous, Message: "Transport rejected the requested resize; previous dimensions restored.")); }
                throw;
            }
        }
        finally { _operations.Release(); }
    }
    internal Task CloseAsync()
    {
        TaskCompletionSource completion;
        lock (_gate)
        {
            if (_closeTask is not null) return _closeTask;
            _closing = true; completion = new(TaskCreationOptions.RunContinuationsAsynchronously); _closeTask = completion.Task;
        }
        Exception? cancellationError = null;
        try { _lifetime.Cancel(); } catch (Exception error) { cancellationError = error; }
        _ = CompleteCloseAsync(completion, cancellationError); return completion.Task;
    }
    private async Task CompleteCloseAsync(TaskCompletionSource completion, Exception? cancellationError)
    {
        try { await CloseCoreAsync().ConfigureAwait(false); if (cancellationError is null) completion.TrySetResult(); else completion.TrySetException(cancellationError); }
        catch (Exception error) { completion.TrySetException(cancellationError is null ? error : new AggregateException(cancellationError, error)); }
    }
    private async Task CloseCoreAsync()
    {
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            await _operations.WaitAsync().ConfigureAwait(false);
            try { await _transport.StopAsync().ConfigureAwait(false); }
            finally
            {
                _transport.DataReceived -= OnData; _transport.ProcessExited -= OnExit;
                if (_resizeSource is not null) _resizeSource.DimensionsChanged -= OnDimensionsChanged;
                try { _transport.Dispose(); }
                finally
                {
                    lock (_gate)
                    {
                        _state = MultiplexerBlockState.Closed; Publish(new(++_sequence, MultiplexerEventKind.Closed));
                        foreach (MultiplexerAttachment a in _attachments.Values) a.Complete();
                        _attachments.Clear(); _controllerId = null; _cold?.Clear();
                    }
                    _operations.Release();
                }
            }
        }
        finally { _lifecycle.Release(); }
    }
    private void RequireController(MultiplexerAttachment attachment)
    {
        if (_closing || !attachment.IsConnected || !_attachments.ContainsKey(attachment.Id)) throw new MultiplexerException(MultiplexerErrorCode.Disconnected, "This attachment is disconnected.");
        if (attachment.Role != MultiplexerRole.Controller || _controllerId != attachment.Id) throw new MultiplexerException(MultiplexerErrorCode.Forbidden, "Observer attachments cannot send input or resize a terminal.");
        if (_state != MultiplexerBlockState.Running) throw new MultiplexerException(MultiplexerErrorCode.Disconnected, "The terminal process is not running.");
    }
    private void AcquireOperation(MultiplexerAttachment attachment)
    {
        lock (_gate) RequireController(attachment);
        try { _operations.Wait(_lifetime.Token); } catch (OperationCanceledException) { throw new MultiplexerException(MultiplexerErrorCode.Disconnected, "The block is closing."); }
    }
    private void OnData(byte[] data, int count)
    {
        if (count < 0 || count > data.Length) return;
        lock (_gate)
        {
            if (_state is MultiplexerBlockState.Closed or MultiplexerBlockState.Exited) return;
            for (int offset = 0; offset < count;)
            {
                int length = Math.Min(count - offset, _options.MaxEventBytes); byte[] owned = data.AsSpan(offset, length).ToArray();
                Publish(new(++_sequence, MultiplexerEventKind.Output, Data: owned)); offset += length;
            }
        }
    }
    private void OnDimensionsChanged(TerminalSessionDimensions dimensions)
    {
        ValidateDimensions(dimensions, _options);
        lock (_gate)
        { if (_state is MultiplexerBlockState.Closed or MultiplexerBlockState.Exited || _dimensions == dimensions) return; _dimensions = dimensions; Publish(new(++_sequence, MultiplexerEventKind.Resize, Dimensions: dimensions)); }
    }
    private void OnExit(int exitCode)
    {
        lock (_gate)
        {
            if (_exitCode is not null || _state == MultiplexerBlockState.Closed) return;
            _exitCode = exitCode; _state = MultiplexerBlockState.Exited; Publish(new(++_sequence, MultiplexerEventKind.Exit, ExitCode: exitCode));
            foreach (MultiplexerAttachment attachment in _attachments.Values) attachment.Complete();
            _attachments.Clear(); _controllerId = null;
        }
    }
    private void Publish(MultiplexerEvent item)
    {
        _journal.Enqueue(item); _journalBytes += item.Data?.Length ?? 0;
        while (_journal.Count > _options.MaxJournalEvents || _journalBytes > _options.MaxJournalBytes)
        {
            MultiplexerEvent evicted = _journal.Dequeue(); _journalBytes -= evicted.Data?.Length ?? 0; _cold?.Append(evicted);
        }
        List<Guid>? slow = null;
        foreach ((Guid id, MultiplexerAttachment attachment) in _attachments)
        {
            if (attachment.TryPublish(item)) continue;
            attachment.Complete(new MultiplexerException(MultiplexerErrorCode.CapacityExceeded, "The attachment did not consume output within its queue budget. Reattach using the last applied cursor."));
            (slow ??= []).Add(id);
        }
        if (slow is null) return;
        foreach (Guid id in slow) { _attachments.Remove(id); if (_controllerId == id) _controllerId = null; }
    }
    internal static void ValidateDimensions(TerminalSessionDimensions dimensions, MultiplexerOptions options)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(dimensions.Columns, 1); ArgumentOutOfRangeException.ThrowIfLessThan(dimensions.Rows, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(dimensions.Columns, options.MaxDimension); ArgumentOutOfRangeException.ThrowIfGreaterThan(dimensions.Rows, options.MaxDimension);
        ArgumentOutOfRangeException.ThrowIfNegative(dimensions.WidthPixels); ArgumentOutOfRangeException.ThrowIfNegative(dimensions.HeightPixels);
    }
}
