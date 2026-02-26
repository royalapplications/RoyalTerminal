// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia - Reusable capture and replay runtime for TerminalControl.

using System.Diagnostics;
using Avalonia.Threading;
using RoyalTerminal.Avalonia.Controls;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Services;

namespace RoyalTerminal.Avalonia.Capture;

/// <summary>
/// Provides capture and replay orchestration for a <see cref="TerminalControl"/>.
/// </summary>
/// <remarks>
/// The runtime records terminal output, session input bytes, and terminal resize events.
/// Replay renders captured output and resize changes back into the target control using a
/// timeline model with play/pause/stop/seek operations.
/// </remarks>
public sealed class TerminalCaptureRuntime : IDisposable
{
    private static readonly byte[] ResetTerminalSequence = [0x1B, (byte)'c'];

    private readonly TerminalControl _control;
    private readonly TerminalCaptureRecorder _recorder = new();
    private readonly DispatcherTimer _replayTimer;

    private TerminalCaptureSession? _captureSession;
    private TerminalCaptureSession? _replaySession;
    private string? _replaySourceLabel;
    private int _replayNextEventIndex;
    private long _replayPositionMilliseconds;
    private long _replayStartOffsetMilliseconds;
    private long _replayStartTimestamp;
    private bool _isReplayPlaying;
    private bool _disposed;

    /// <summary>
    /// Initializes a runtime bound to a terminal control.
    /// </summary>
    /// <param name="control">Target terminal control.</param>
    public TerminalCaptureRuntime(TerminalControl control)
    {
        _control = control ?? throw new ArgumentNullException(nameof(control));
        _control.DataReceived += OnDataReceived;
        _control.TerminalResized += OnTerminalResized;
        _control.TerminalSessionService.InputSent += OnInputSent;

        _replayTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(16),
        };
        _replayTimer.Tick += OnReplayTick;
    }

    /// <summary>
    /// Raised when capture or replay state changes.
    /// </summary>
    public event EventHandler? StateChanged;

    /// <summary>
    /// Gets whether capture recording is active.
    /// </summary>
    public bool IsCaptureActive => _recorder.IsCapturing;

    /// <summary>
    /// Gets whether any capture is available (active or finalized).
    /// </summary>
    public bool HasCapture => _captureSession is not null || _recorder.IsCapturing;

    /// <summary>
    /// Gets the finalized capture session, if available.
    /// </summary>
    public TerminalCaptureSession? CaptureSession => _captureSession;

    /// <summary>
    /// Gets whether replay has been loaded.
    /// </summary>
    public bool IsReplayEnabled => _replaySession is not null;

    /// <summary>
    /// Gets whether replay playback is currently running.
    /// </summary>
    public bool IsReplayPlaying => _isReplayPlaying;

    /// <summary>
    /// Gets an optional source label for the loaded replay.
    /// </summary>
    public string? ReplaySourceLabel => _replaySourceLabel;

    /// <summary>
    /// Gets replay duration in seconds.
    /// </summary>
    public double ReplayDurationSeconds
    {
        get
        {
            if (_replaySession is null)
            {
                return 0;
            }

            return _replaySession.DurationMilliseconds / 1000.0;
        }
    }

    /// <summary>
    /// Gets current replay position in seconds.
    /// </summary>
    public double ReplayPositionSeconds => _replayPositionMilliseconds / 1000.0;

    /// <summary>
    /// Starts a new capture and clears loaded replay state.
    /// </summary>
    public void StartCapture()
    {
        ThrowIfDisposed();

        PauseReplayInternal(raiseStateChanged: false);
        _replaySession = null;
        _replaySourceLabel = null;
        _replayNextEventIndex = 0;
        _replayPositionMilliseconds = 0;

        _captureSession = null;
        _recorder.StartCapture(_control.Columns, _control.Rows, _control.ActiveTransportId);
        RaiseStateChanged();
    }

    /// <summary>
    /// Stops capture recording and returns the resulting session.
    /// </summary>
    public TerminalCaptureSession StopCapture()
    {
        ThrowIfDisposed();

        TerminalCaptureSession session = _recorder.StopCapture();
        _captureSession = session;
        RaiseStateChanged();
        return session;
    }

    /// <summary>
    /// Returns a current capture snapshot without stopping active capture.
    /// </summary>
    public TerminalCaptureSession? GetCaptureSnapshot()
    {
        ThrowIfDisposed();

        if (_recorder.IsCapturing)
        {
            return _recorder.CreateSnapshot();
        }

        return _captureSession;
    }

    /// <summary>
    /// Loads a replay session and seeks to the beginning.
    /// </summary>
    /// <param name="session">Capture session to replay.</param>
    /// <param name="sourceLabel">Optional source label displayed by hosts.</param>
    public void LoadReplay(TerminalCaptureSession session, string? sourceLabel)
    {
        ArgumentNullException.ThrowIfNull(session);
        ThrowIfDisposed();

        PauseReplayInternal(raiseStateChanged: false);
        _replaySession = session;
        _replaySourceLabel = sourceLabel;
        _captureSession = session;
        SeekReplayInternal(0, forceReset: true);
        RaiseStateChanged();
    }

    /// <summary>
    /// Starts replay playback.
    /// </summary>
    public void PlayReplay()
    {
        ThrowIfDisposed();
        if (_replaySession is null)
        {
            return;
        }

        if (_replaySession.Events.Count == 0)
        {
            _isReplayPlaying = false;
            _replayPositionMilliseconds = 0;
            RaiseStateChanged();
            return;
        }

        long duration = _replaySession.DurationMilliseconds;
        if (_replayPositionMilliseconds >= duration)
        {
            SeekReplayInternal(0, forceReset: true);
        }

        _replayStartOffsetMilliseconds = _replayPositionMilliseconds;
        _replayStartTimestamp = Stopwatch.GetTimestamp();
        _isReplayPlaying = true;
        if (!_replayTimer.IsEnabled)
        {
            _replayTimer.Start();
        }

        RaiseStateChanged();
    }

    /// <summary>
    /// Pauses replay playback.
    /// </summary>
    public void PauseReplay()
    {
        ThrowIfDisposed();
        PauseReplayInternal(raiseStateChanged: true);
    }

    /// <summary>
    /// Stops replay playback and seeks to the start.
    /// </summary>
    public void StopReplay()
    {
        ThrowIfDisposed();
        if (_replaySession is null)
        {
            return;
        }

        PauseReplayInternal(raiseStateChanged: false);
        SeekReplayInternal(0, forceReset: true);
        RaiseStateChanged();
    }

    /// <summary>
    /// Seeks replay to the target position (seconds).
    /// </summary>
    public void SeekReplay(double positionSeconds)
    {
        ThrowIfDisposed();
        if (_replaySession is null)
        {
            return;
        }

        long targetMilliseconds = (long)Math.Round(positionSeconds * 1000.0, MidpointRounding.AwayFromZero);
        SeekReplayInternal(targetMilliseconds);
        RaiseStateChanged();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _replayTimer.Stop();
        _replayTimer.Tick -= OnReplayTick;

        _control.DataReceived -= OnDataReceived;
        _control.TerminalResized -= OnTerminalResized;
        _control.TerminalSessionService.InputSent -= OnInputSent;

        StateChanged = null;
    }

    private void SeekReplayInternal(long targetMilliseconds, bool forceReset = false)
    {
        if (_replaySession is null)
        {
            return;
        }

        bool wasPlaying = _isReplayPlaying;
        PauseReplayInternal(raiseStateChanged: false);

        long duration = _replaySession.DurationMilliseconds;
        long clampedTarget = Math.Clamp(targetMilliseconds, 0, duration);
        bool requiresReset = forceReset || clampedTarget < _replayPositionMilliseconds;

        if (requiresReset)
        {
            ResetReplaySurface();
            _replayNextEventIndex = 0;
            _replayPositionMilliseconds = 0;
        }

        ApplyEventsUntil(clampedTarget);
        _replayPositionMilliseconds = clampedTarget;

        if (wasPlaying)
        {
            _replayStartOffsetMilliseconds = _replayPositionMilliseconds;
            _replayStartTimestamp = Stopwatch.GetTimestamp();
            _isReplayPlaying = true;
            if (!_replayTimer.IsEnabled)
            {
                _replayTimer.Start();
            }
        }
    }

    private void ApplyEventsUntil(long targetMilliseconds)
    {
        if (_replaySession is null)
        {
            return;
        }

        IReadOnlyList<TerminalCaptureEvent> events = _replaySession.Events;
        while (_replayNextEventIndex < events.Count)
        {
            TerminalCaptureEvent replayEvent = events[_replayNextEventIndex];
            if (replayEvent.OffsetMilliseconds > targetMilliseconds)
            {
                break;
            }

            ApplyReplayEvent(replayEvent);
            _replayNextEventIndex++;
        }
    }

    private void ApplyReplayEvent(TerminalCaptureEvent replayEvent)
    {
        switch (replayEvent.Kind)
        {
            case TerminalCaptureEventKind.Output:
                if (replayEvent.Data is { Length: > 0 } output)
                {
                    _control.WriteOutput(output);
                }
                break;

            case TerminalCaptureEventKind.Input:
                // Input events are preserved for timeline fidelity and persistence.
                // Replaying them into live transports is intentionally skipped.
                break;

            case TerminalCaptureEventKind.Resize:
                if (replayEvent.Columns > 0)
                {
                    _control.Columns = replayEvent.Columns;
                }

                if (replayEvent.Rows > 0)
                {
                    _control.Rows = replayEvent.Rows;
                }
                break;
        }
    }

    private void ResetReplaySurface()
    {
        if (_control.HasActiveSession || _control.HasPty)
        {
            _control.StopPty();
        }

        if (_replaySession is not null)
        {
            _control.Columns = Math.Max(1, _replaySession.InitialColumns);
            _control.Rows = Math.Max(1, _replaySession.InitialRows);
        }

        _control.WriteOutput(ResetTerminalSequence);
    }

    private void PauseReplayInternal(bool raiseStateChanged)
    {
        bool wasPlaying = _isReplayPlaying || _replayTimer.IsEnabled;
        _isReplayPlaying = false;
        _replayTimer.Stop();

        if (raiseStateChanged && wasPlaying)
        {
            RaiseStateChanged();
        }
    }

    private void OnReplayTick(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;

        if (!_isReplayPlaying || _replaySession is null)
        {
            _replayTimer.Stop();
            return;
        }

        long elapsedMilliseconds = _replayStartOffsetMilliseconds
            + GetElapsedMilliseconds(_replayStartTimestamp);
        long duration = _replaySession.DurationMilliseconds;
        long targetMilliseconds = Math.Min(elapsedMilliseconds, duration);

        ApplyEventsUntil(targetMilliseconds);
        _replayPositionMilliseconds = targetMilliseconds;

        if (targetMilliseconds >= duration)
        {
            _isReplayPlaying = false;
            _replayTimer.Stop();
        }

        RaiseStateChanged();
    }

    private void OnDataReceived(object? sender, TerminalDataEventArgs e)
    {
        _ = sender;
        if (!IsCaptureActive)
        {
            return;
        }

        _recorder.CaptureOutput(e.DataSpan);
    }

    private void OnInputSent(object? sender, TerminalSessionInputEventArgs e)
    {
        _ = sender;
        if (!IsCaptureActive)
        {
            return;
        }

        _recorder.CaptureInput(e.Data.Span);
    }

    private void OnTerminalResized(object? sender, TerminalSizeEventArgs e)
    {
        _ = sender;
        if (!IsCaptureActive)
        {
            return;
        }

        _recorder.CaptureResize(e.Columns, e.Rows);
    }

    private static long GetElapsedMilliseconds(long startTimestamp)
    {
        long elapsedTicks = Stopwatch.GetTimestamp() - startTimestamp;
        return (elapsedTicks * 1000) / Stopwatch.Frequency;
    }

    private void RaiseStateChanged()
    {
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
