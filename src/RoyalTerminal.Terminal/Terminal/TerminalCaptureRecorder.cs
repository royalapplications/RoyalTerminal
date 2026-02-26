// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Terminal - Runtime capture recorder for terminal event streams.

using System.Diagnostics;

namespace RoyalTerminal.Terminal;

/// <summary>
/// Records terminal input/output/resize events with a relative timeline.
/// </summary>
public sealed class TerminalCaptureRecorder
{
    private readonly object _sync = new();
    private readonly List<TerminalCaptureEvent> _events = [];
    private readonly Stopwatch _stopwatch = new();

    private DateTimeOffset _createdUtc = DateTimeOffset.UtcNow;
    private string? _transportId;
    private int _initialColumns = 80;
    private int _initialRows = 24;
    private int _lastResizeColumns = 80;
    private int _lastResizeRows = 24;
    private long _durationMilliseconds;
    private volatile bool _isCapturing;

    /// <summary>Gets whether capture recording is active.</summary>
    public bool IsCapturing => _isCapturing;

    /// <summary>
    /// Starts a new capture session and clears previously recorded events.
    /// </summary>
    public void StartCapture(int initialColumns, int initialRows, string? transportId = null)
    {
        lock (_sync)
        {
            _events.Clear();
            _createdUtc = DateTimeOffset.UtcNow;
            _transportId = string.IsNullOrWhiteSpace(transportId) ? null : transportId.Trim();
            _initialColumns = Math.Max(1, initialColumns);
            _initialRows = Math.Max(1, initialRows);
            _lastResizeColumns = _initialColumns;
            _lastResizeRows = _initialRows;
            _durationMilliseconds = 0;
            _stopwatch.Restart();
            _isCapturing = true;
        }
    }

    /// <summary>
    /// Stops capture recording and returns the resulting session snapshot.
    /// </summary>
    public TerminalCaptureSession StopCapture()
    {
        lock (_sync)
        {
            if (_isCapturing)
            {
                _stopwatch.Stop();
                _isCapturing = false;
            }

            return CreateSessionLocked();
        }
    }

    /// <summary>
    /// Clears all recorded events and resets recorder state.
    /// </summary>
    public void Reset()
    {
        lock (_sync)
        {
            _events.Clear();
            _stopwatch.Reset();
            _durationMilliseconds = 0;
            _isCapturing = false;
            _transportId = null;
            _createdUtc = DateTimeOffset.UtcNow;
        }
    }

    /// <summary>
    /// Creates a stable session snapshot without stopping active capture.
    /// </summary>
    public TerminalCaptureSession CreateSnapshot()
    {
        lock (_sync)
        {
            return CreateSessionLocked();
        }
    }

    /// <summary>
    /// Captures terminal output bytes.
    /// </summary>
    public void CaptureOutput(ReadOnlySpan<byte> data)
    {
        CaptureBinaryEvent(TerminalCaptureEventKind.Output, data);
    }

    /// <summary>
    /// Captures terminal input bytes.
    /// </summary>
    public void CaptureInput(ReadOnlySpan<byte> data)
    {
        CaptureBinaryEvent(TerminalCaptureEventKind.Input, data);
    }

    /// <summary>
    /// Captures terminal resize events.
    /// </summary>
    public void CaptureResize(int columns, int rows)
    {
        if (!_isCapturing)
        {
            return;
        }

        lock (_sync)
        {
            if (!_isCapturing)
            {
                return;
            }

            int safeColumns = Math.Max(1, columns);
            int safeRows = Math.Max(1, rows);
            if (_lastResizeColumns == safeColumns && _lastResizeRows == safeRows)
            {
                return;
            }

            long offsetMilliseconds = _stopwatch.ElapsedMilliseconds;
            _events.Add(new TerminalCaptureEvent
            {
                OffsetMilliseconds = offsetMilliseconds,
                Kind = TerminalCaptureEventKind.Resize,
                Columns = safeColumns,
                Rows = safeRows,
            });
            _lastResizeColumns = safeColumns;
            _lastResizeRows = safeRows;
            if (offsetMilliseconds > _durationMilliseconds)
            {
                _durationMilliseconds = offsetMilliseconds;
            }
        }
    }

    private void CaptureBinaryEvent(TerminalCaptureEventKind kind, ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty || !_isCapturing)
        {
            return;
        }

        lock (_sync)
        {
            if (!_isCapturing)
            {
                return;
            }

            long offsetMilliseconds = _stopwatch.ElapsedMilliseconds;
            _events.Add(new TerminalCaptureEvent
            {
                OffsetMilliseconds = offsetMilliseconds,
                Kind = kind,
                Data = data.ToArray(),
            });
            if (offsetMilliseconds > _durationMilliseconds)
            {
                _durationMilliseconds = offsetMilliseconds;
            }
        }
    }

    private TerminalCaptureSession CreateSessionLocked()
    {
        List<TerminalCaptureEvent> snapshotEvents = new(_events.Count);
        for (int i = 0; i < _events.Count; i++)
        {
            TerminalCaptureEvent recordedEvent = _events[i];
            snapshotEvents.Add(recordedEvent with
            {
                Data = recordedEvent.Data is null
                    ? null
                    : recordedEvent.Data.ToArray(),
            });
        }

        return new TerminalCaptureSession
        {
            FormatVersion = TerminalCaptureSession.CurrentFormatVersion,
            CreatedUtc = _createdUtc,
            TransportId = _transportId,
            InitialColumns = _initialColumns,
            InitialRows = _initialRows,
            DurationMilliseconds = _durationMilliseconds,
            Events = snapshotEvents,
        };
    }
}
