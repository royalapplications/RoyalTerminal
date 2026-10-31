// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Snapshots;

namespace RoyalTerminal.Multiplexing.Inspection;

/// <summary>Maintains canonical managed VT state even while no client is attached.</summary>
/// <remarks>Output is parsed and published under one gate; only this parser writes terminal-query replies.</remarks>
public sealed class InspectingTerminalTransport : ITerminalTransport, IMultiplexerResizeSource
{
    private readonly object _gate = new();
    private readonly object _resizeGate = new();
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly ITerminalTransport _inner;
    private readonly BasicVtProcessor _processor;
    private readonly TerminalInspectionOptions _options;
    private readonly Queue<byte[]> _responses = [];
    private TerminalSessionDimensions _dimensions;
    private int _responseBytes;
    private bool _drainingResponses;
    private bool _startAttempted;
    private bool _started;
    private bool _exited;
    private bool _disposed;
    private Exception? _responseError;

    /// <summary>Creates an inspection wrapper that owns and disposes the underlying transport.</summary>
    public InspectingTerminalTransport(ITerminalTransport inner, TerminalSessionDimensions dimensions,
        TerminalInspectionOptions? options = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _options = options ?? new();
        _options.Validate();
        _options.ValidateDimensions(dimensions);
        _dimensions = dimensions;
        TerminalScreen screen = new(dimensions.Columns, dimensions.Rows, _options.ScrollbackLimit)
        {
            SnapshotScrollbackQuota = new GhosttySnapshotScrollbackQuota { MaximumBytes = _options.ScrollbackBytes },
        };
        _processor = new(screen, new BasicVtProcessorOptions
        {
            TitleReportEnabled = false, ClipboardWriteLimitBytes = 0, SixelGraphicsEnabled = false,
            GlyphProtocolEnabled = false, KittyGraphicsMaxApcBytes = 0, KittyGraphicsMaxImageBytes = 0,
            KittyGraphicsStorageLimitBytes = 0, UnknownSequenceMaxBytes = 0, TerminfoName = "xterm-256color",
        });
        _processor.NotifyResize(dimensions.Columns, dimensions.Rows, dimensions.WidthPixels, dimensions.HeightPixels);
        _processor.ResponseCallback = EnqueueResponse;
        _processor.BellCallback = Metadata.Bell;
        _processor.TitleCallback = Metadata.Title;
        _processor.ProgramStatusChangedCallback = () => Metadata.StatusChanged(_processor.ProgramStatuses);
        _inner.DataReceived += OnData;
        _inner.ProcessExited += OnExit;
    }

    /// <summary>Reads recent metadata transitions independently from the VT replay journal.</summary>
    public TerminalMetadataFeed Metadata { get; } = new();
    internal event Action? Disposed;
    /// <inheritdoc />
    public event Action<byte[], int>? DataReceived;
    /// <inheritdoc />
    public event Action<int>? ProcessExited;
    /// <summary>Raised after canonical geometry commits, in the same serialized order as output.</summary>
    public event Action<TerminalSessionDimensions>? DimensionsChanged;
    /// <inheritdoc />
    public bool IsRunning => !Volatile.Read(ref _disposed) && _inner.IsRunning;

    /// <summary>Gets the current raw terminal title as plain text, never markup.</summary>
    public string Title
    {
        get
        {
            lock (_gate)
            {
                ThrowIfDisposed();
                Span<byte> bytes = stackalloc byte[1024];
                return _processor.TryCopyTitle(bytes, out int length) ? Encoding.UTF8.GetString(bytes[..length]) : string.Empty;
            }
        }
    }
    /// <summary>Gets the terminal-reported directory without resolving it against the host filesystem.</summary>
    public string WorkingDirectory
    {
        get
        {
            lock (_gate)
            {
                ThrowIfDisposed();
                Span<byte> bytes = stackalloc byte[4096];
                return _processor.TryCopyWorkingDirectory(bytes, out int length) ? Encoding.UTF8.GetString(bytes[..length]) : string.Empty;
            }
        }
    }
    /// <summary>Gets an owned snapshot of current OSC 7501 records.</summary>
    public IReadOnlyList<TerminalProgramStatus> ProgramStatuses
    {
        get { lock (_gate) { ThrowIfDisposed(); return _processor.ProgramStatuses.ToArray(); } }
    }
    /// <summary>Gets the geometry used by the canonical screen.</summary>
    public TerminalSessionDimensions Dimensions
    {
        get { lock (_gate) { ThrowIfDisposed(); return _dimensions; } }
    }
    /// <summary>Gets a terminal-reply failure, if any. Output remains inspectable.</summary>
    public Exception? ResponseError { get { lock (_gate) return _responseError; } }
    /// <summary>Captures the active screen and retained history with the existing terminal formatter.</summary>
    public string Capture(TerminalSnapshotExportFormat format, TerminalSnapshotExportOptions options = default)
    {
        lock (_gate)
        {
            ThrowIfDisposed(); _processor.RefreshTimedState();
            if (!_processor.TryExportSnapshot(format, options, out string snapshot))
                throw new ArgumentOutOfRangeException(nameof(format), "Unsupported terminal snapshot format.");
            return snapshot;
        }
    }
    /// <summary>Resolves the nearest inherited application identity.</summary>
    public string GetProgramStatusApplication(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        lock (_gate) { ThrowIfDisposed(); return _processor.GetProgramStatusApplication(id); }
    }
    /// <inheritdoc />
    public async ValueTask StartAsync(ITerminalTransportOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (_gate)
            {
                ThrowIfDisposed();
                if (_startAttempted) throw new InvalidOperationException("An inspected terminal can only be started once.");
                if (options.Dimensions != _dimensions) throw new ArgumentException("Start dimensions must match initial inspection dimensions.", nameof(options));
                _startAttempted = true;
            }
            await _inner.StartAsync(options, cancellationToken).ConfigureAwait(false);
            bool drain;
            lock (_gate) { _started = !_disposed && !_exited; drain = BeginResponseDrain(); }
            if (drain) DrainResponses();
        }
        finally { _lifecycle.Release(); }
    }
    /// <inheritdoc />
    public void SendInput(ReadOnlySpan<byte> utf8)
    {
        ThrowIfDisposed();
        // A native write can synchronously wait for an output callback; never hold the parser gate here.
        _inner.SendInput(utf8);
    }
    /// <inheritdoc />
    public void Resize(TerminalSessionDimensions dimensions)
    {
        _options.ValidateDimensions(dimensions);
        lock (_resizeGate)
        {
            TerminalSessionDimensions previous;
            bool drain;
            lock (_gate)
            {
                ThrowIfDisposed(); previous = _dimensions;
                if (previous == dimensions) return;
                ApplyDimensions(dimensions); drain = BeginResponseDrain();
            }
            try { _inner.Resize(dimensions); }
            catch { lock (_gate) if (!_disposed) ApplyDimensions(previous); throw; }
            finally { if (drain) DrainResponses(); }
        }
    }
    /// <inheritdoc />
    public async ValueTask StopAsync()
    {
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            lock (_gate) { if (_disposed) return; _started = false; ClearResponses(); }
            await _inner.StopAsync().ConfigureAwait(false);
        }
        finally { _lifecycle.Release(); }
    }
    /// <inheritdoc />
    public void Dispose()
    {
        Action? onDisposed;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true; _started = false; ClearResponses();
            _inner.DataReceived -= OnData; _inner.ProcessExited -= OnExit;
            Metadata.End(); _processor.Dispose(); onDisposed = Disposed; Disposed = null;
        }
        try { _inner.Dispose(); }
        finally { onDisposed?.Invoke(); }
    }
    private void OnData(byte[] data, int count)
    {
        if (count <= 0 || count > data.Length) return;
        bool drain;
        lock (_gate)
        {
            if (_disposed || _exited) return;
            _processor.Process(data.AsSpan(0, count));
            DataReceived?.Invoke(data, count);
            drain = BeginResponseDrain();
        }
        if (drain) DrainResponses();
    }
    private void OnExit(int code)
    {
        lock (_gate)
        {
            if (_disposed || _exited) return;
            _exited = true; _started = false; ClearResponses();
            _processor.NotifyProgramStatusProcessExit(); Metadata.ChildExited(code); ProcessExited?.Invoke(code);
        }
    }
    private void ApplyDimensions(TerminalSessionDimensions dimensions)
    {
        _processor.ResizeScreen(dimensions.Columns, dimensions.Rows, dimensions.WidthPixels, dimensions.HeightPixels);
        _dimensions = dimensions; DimensionsChanged?.Invoke(dimensions);
    }
    private void EnqueueResponse(byte[] response)
    {
        if (_responseError is not null || response.Length == 0) return;
        if (response.Length > _options.MaxPendingResponseBytes - _responseBytes)
        {
            _responseError = new IOException("Terminal query responses exceeded the configured pending-byte limit.");
            ClearResponses(); return;
        }
        _responses.Enqueue(response); _responseBytes += response.Length;
    }
    private bool BeginResponseDrain()
    {
        if (!_started || _disposed || _drainingResponses || _responses.Count == 0) return false;
        _drainingResponses = true; return true;
    }
    private void DrainResponses()
    {
        while (true)
        {
            byte[] response;
            lock (_gate)
            {
                if (!_started || _disposed || !_responses.TryDequeue(out response!)) { _drainingResponses = false; return; }
                _responseBytes -= response.Length;
            }
            try { _inner.SendInput(response); }
            catch (Exception error)
            {
                lock (_gate) { _responseError = error; ClearResponses(); _drainingResponses = false; }
                return;
            }
        }
    }
    private void ClearResponses() { _responses.Clear(); _responseBytes = 0; }
    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed), this);
}
