// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Terminal - Capture/replay contracts for terminal sessions.

using System.Text.Json.Serialization;

namespace RoyalTerminal.Terminal;

/// <summary>
/// Captured terminal event kind.
/// </summary>
public enum TerminalCaptureEventKind
{
    /// <summary>Terminal output bytes written to the screen pipeline.</summary>
    Output = 0,

    /// <summary>Terminal input bytes sent to the active endpoint/transport.</summary>
    Input = 1,

    /// <summary>Terminal grid resize event.</summary>
    Resize = 2,
}

/// <summary>
/// Captured terminal event with a relative timeline offset.
/// </summary>
public sealed record TerminalCaptureEvent
{
    /// <summary>Event offset from capture start in milliseconds.</summary>
    public long OffsetMilliseconds { get; init; }

    /// <summary>Event kind.</summary>
    public TerminalCaptureEventKind Kind { get; init; }

    /// <summary>
    /// Optional binary payload for <see cref="TerminalCaptureEventKind.Output"/> and
    /// <see cref="TerminalCaptureEventKind.Input"/> events.
    /// </summary>
    public byte[]? Data { get; init; }

    /// <summary>Captured column count for resize events.</summary>
    public int Columns { get; init; }

    /// <summary>Captured row count for resize events.</summary>
    public int Rows { get; init; }
}

/// <summary>
/// Serializable capture session payload for replay and persistence.
/// </summary>
public sealed record TerminalCaptureSession
{
    /// <summary>Current capture file format version.</summary>
    public const int CurrentFormatVersion = 1;

    /// <summary>Capture file format version.</summary>
    public int FormatVersion { get; init; } = CurrentFormatVersion;

    /// <summary>Capture creation time in UTC.</summary>
    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Optional transport identifier active when capture started.</summary>
    public string? TransportId { get; init; }

    /// <summary>Initial terminal column count.</summary>
    public int InitialColumns { get; init; } = 80;

    /// <summary>Initial terminal row count.</summary>
    public int InitialRows { get; init; } = 24;

    /// <summary>Ordered captured events.</summary>
    public List<TerminalCaptureEvent> Events { get; init; } = [];

    /// <summary>Total replay duration in milliseconds.</summary>
    [JsonIgnore]
    public long DurationMilliseconds
    {
        get
        {
            long duration = 0;
            for (int i = 0; i < Events.Count; i++)
            {
                long offset = Events[i].OffsetMilliseconds;
                if (offset > duration)
                {
                    duration = offset;
                }
            }

            return duration;
        }
    }
}
