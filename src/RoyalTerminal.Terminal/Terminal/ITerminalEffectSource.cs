// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Terminal;

/// <summary>Normalized terminal clipboard destinations.</summary>
public enum TerminalClipboardLocation
{
    /// <summary>The standard system clipboard.</summary>
    Standard,

    /// <summary>The selection clipboard.</summary>
    Selection,

    /// <summary>The primary selection clipboard.</summary>
    Primary,
}

/// <summary>Result returned by a terminal clipboard-write policy callback.</summary>
public enum TerminalClipboardWriteResult
{
    /// <summary>The write completed successfully.</summary>
    Success,

    /// <summary>The write was denied by policy or the user.</summary>
    Denied,

    /// <summary>The destination or representation is unsupported.</summary>
    Unsupported,

    /// <summary>The clipboard is temporarily unavailable.</summary>
    Busy,

    /// <summary>The supplied data is invalid.</summary>
    InvalidData,

    /// <summary>The write failed because of an I/O error.</summary>
    IoError,
}

/// <summary>One binary-safe MIME representation of a terminal clipboard value.</summary>
/// <param name="MimeType">The representation MIME type.</param>
/// <param name="Data">Owned representation bytes.</param>
public sealed record TerminalClipboardContent(string MimeType, byte[] Data);

/// <summary>An atomic, normalized terminal clipboard-write request.</summary>
/// <param name="Location">The target clipboard.</param>
/// <param name="Contents">Representations of one value; an empty list requests a clear.</param>
/// <param name="Name">The requesting program name, when supplied by the protocol.</param>
/// <param name="Granted">Whether the requesting program already has a session grant.</param>
/// <param name="CanRemember">Whether the host may remember a grant for this program.</param>
public sealed record TerminalClipboardWrite(
    TerminalClipboardLocation Location,
    IReadOnlyList<TerminalClipboardContent> Contents,
    string Name = "",
    bool Granted = false,
    bool CanRemember = false);

/// <summary>A policy and completion reply for a terminal clipboard write.</summary>
/// <param name="Result">The write outcome.</param>
/// <param name="Remember">Whether to grant the named program for this session.</param>
public readonly record struct TerminalClipboardWriteReply(
    TerminalClipboardWriteResult Result,
    bool Remember = false);

/// <summary>Result returned by a terminal clipboard-read policy callback.</summary>
public enum TerminalClipboardReadResult
{
    /// <summary>The clipboard was read successfully.</summary>
    Success,

    /// <summary>The read was denied by policy or the user.</summary>
    Denied,

    /// <summary>The clipboard location is unsupported.</summary>
    Unsupported,

    /// <summary>The clipboard is temporarily unavailable.</summary>
    Busy,

    /// <summary>The read failed because of an I/O error.</summary>
    IoError,
}

/// <summary>An atomic, normalized terminal clipboard-read request.</summary>
/// <param name="Location">The source clipboard.</param>
/// <param name="MimeTypes">Requested MIME types in preference order.</param>
/// <param name="ListAvailableTypes">Whether all available MIME types should be listed.</param>
/// <param name="Name">The requesting program name, when supplied by the protocol.</param>
/// <param name="Granted">Whether this program already has a session grant.</param>
/// <param name="CanRemember">Whether a successful reply may remember a session grant.</param>
public sealed record TerminalClipboardRead(
    TerminalClipboardLocation Location,
    IReadOnlyList<string> MimeTypes,
    bool ListAvailableTypes = false,
    string Name = "",
    bool Granted = false,
    bool CanRemember = false);

/// <summary>An atomic terminal clipboard-read reply.</summary>
/// <param name="Result">The read outcome.</param>
/// <param name="Contents">Available requested representations.</param>
/// <param name="AvailableMimeTypes">All available MIME types for list requests.</param>
/// <param name="Remember">Whether to grant the named program for this session.</param>
public sealed record TerminalClipboardReadReply(
    TerminalClipboardReadResult Result,
    IReadOnlyList<TerminalClipboardContent> Contents,
    IReadOnlyList<string>? AvailableMimeTypes = null,
    bool Remember = false);

/// <summary>Kind of syntactically valid but unsupported terminal sequence.</summary>
public enum TerminalUnknownSequenceType
{
    /// <summary>Application Program Command string.</summary>
    Apc,
}

/// <summary>An unsupported terminal sequence retained for host-level extensions.</summary>
/// <param name="Type">The sequence family.</param>
/// <param name="Content">Binary-safe bytes between the introducer and terminator.</param>
/// <param name="Truncated">Whether the configured retention limit shortened the content.</param>
public sealed record TerminalUnknownSequence(
    TerminalUnknownSequenceType Type,
    byte[] Content,
    bool Truncated);

/// <summary>A terminal-requested desktop notification.</summary>
/// <param name="Title">Notification title, or an empty string.</param>
/// <param name="Body">Notification body.</param>
public sealed record TerminalDesktopNotification(string Title, string Body);

/// <summary>Semantic state of a terminal progress report.</summary>
public enum TerminalProgressState
{
    /// <summary>Remove progress indication.</summary>
    Remove,

    /// <summary>Show determinate progress.</summary>
    Set,

    /// <summary>Show a failed progress state.</summary>
    Error,

    /// <summary>Show indeterminate progress.</summary>
    Indeterminate,

    /// <summary>Show paused progress.</summary>
    Pause,
}

/// <summary>A terminal progress report.</summary>
/// <param name="State">The literal reported progress state.</param>
/// <param name="Progress">Percentage from 0 through 100, or null when omitted.</param>
public sealed record TerminalProgressReport(TerminalProgressState State, byte? Progress);

/// <summary>
/// Optional framework-neutral sink for terminal effects that require host policy or platform services.
/// </summary>
public interface ITerminalEffectSource
{
    /// <summary>
    /// Handles normalized clipboard writes synchronously. When null, writes are reported unsupported.
    /// </summary>
    Func<TerminalClipboardWrite, TerminalClipboardWriteResult>? ClipboardWriteCallback { get; set; }

    /// <summary>
    /// Handles clipboard writes with permission-grant metadata. When set, this takes precedence
    /// over <see cref="ClipboardWriteCallback"/>.
    /// </summary>
    Func<TerminalClipboardWrite, TerminalClipboardWriteReply>? ClipboardWriteRequestCallback { get; set; }

    /// <summary>Handles normalized clipboard reads synchronously.</summary>
    Func<TerminalClipboardRead, TerminalClipboardReadReply>? ClipboardReadCallback { get; set; }

    /// <summary>Observes syntactically valid terminal sequences unsupported by the processor.</summary>
    Action<TerminalUnknownSequence>? UnknownSequenceCallback { get; set; }

    /// <summary>Handles terminal-requested desktop notifications.</summary>
    Action<TerminalDesktopNotification>? DesktopNotificationCallback { get; set; }

    /// <summary>Handles terminal progress reports.</summary>
    Action<TerminalProgressReport>? ProgressReportCallback { get; set; }

    /// <summary>Handles raw working-directory values reported by OSC 7, OSC 9, or OSC 1337.</summary>
    Action<string>? WorkingDirectoryCallback { get; set; }
}
