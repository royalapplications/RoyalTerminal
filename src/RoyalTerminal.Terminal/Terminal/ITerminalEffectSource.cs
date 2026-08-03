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
public sealed record TerminalClipboardWrite(
    TerminalClipboardLocation Location,
    IReadOnlyList<TerminalClipboardContent> Contents);

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

    /// <summary>Handles terminal-requested desktop notifications.</summary>
    Action<TerminalDesktopNotification>? DesktopNotificationCallback { get; set; }

    /// <summary>Handles terminal progress reports.</summary>
    Action<TerminalProgressReport>? ProgressReportCallback { get; set; }

    /// <summary>Handles raw working-directory values reported by OSC 7, OSC 9, or OSC 1337.</summary>
    Action<string>? WorkingDirectoryCallback { get; set; }
}
