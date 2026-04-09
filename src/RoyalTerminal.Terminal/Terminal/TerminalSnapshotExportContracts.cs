// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Terminal - Shared terminal snapshot export contracts.

namespace RoyalTerminal.Terminal;

/// <summary>
/// Snapshot export formats supported by terminal engines.
/// </summary>
public enum TerminalSnapshotExportFormat
{
    /// <summary>Plain text terminal content.</summary>
    PlainText = 0,

    /// <summary>Styled VT output that can recreate terminal state.</summary>
    StyledVt = 1,

    /// <summary>HTML output with inline styles.</summary>
    Html = 2,
}

/// <summary>
/// Optional styled-export extras that mirror the official Ghostty formatter surface.
/// </summary>
/// <param name="IncludeCursor">Include cursor position.</param>
/// <param name="IncludeStyle">Include active SGR style.</param>
/// <param name="IncludeHyperlinks">Include hyperlink state.</param>
/// <param name="IncludeProtection">Include DECSCA protection state.</param>
/// <param name="IncludeKittyKeyboard">Include Kitty keyboard protocol state.</param>
/// <param name="IncludeCharsets">Include character set state.</param>
/// <param name="IncludePalette">Include palette definitions.</param>
/// <param name="IncludeModes">Include terminal modes.</param>
/// <param name="IncludeScrollingRegion">Include scroll region state.</param>
/// <param name="IncludeTabstops">Include tab stop state.</param>
/// <param name="IncludeWorkingDirectory">Include OSC 7 working directory state.</param>
/// <param name="IncludeKeyboardModes">Include keyboard mode state.</param>
public readonly record struct TerminalSnapshotExportExtras(
    bool IncludeCursor = false,
    bool IncludeStyle = false,
    bool IncludeHyperlinks = false,
    bool IncludeProtection = false,
    bool IncludeKittyKeyboard = false,
    bool IncludeCharsets = false,
    bool IncludePalette = false,
    bool IncludeModes = false,
    bool IncludeScrollingRegion = false,
    bool IncludeTabstops = false,
    bool IncludeWorkingDirectory = false,
    bool IncludeKeyboardModes = false);

/// <summary>
/// Options for exporting a terminal snapshot.
/// </summary>
/// <param name="Unwrap">Whether soft-wrapped lines should be unwrapped.</param>
/// <param name="TrimTrailingWhitespace">Whether trailing whitespace should be trimmed.</param>
/// <param name="Selection">Optional viewport-relative selection range to export.</param>
/// <param name="Extras">Optional styled-export extras.</param>
public readonly record struct TerminalSnapshotExportOptions(
    bool Unwrap = false,
    bool TrimTrailingWhitespace = false,
    TerminalSelectionRange? Selection = null,
    TerminalSnapshotExportExtras Extras = default);

/// <summary>
/// Optional VT processor capability for exporting full terminal snapshots.
/// </summary>
public interface ITerminalSnapshotExportSource
{
    /// <summary>
    /// Returns whether the processor can export the requested format.
    /// </summary>
    bool SupportsSnapshotFormat(TerminalSnapshotExportFormat format);

    /// <summary>
    /// Exports the requested snapshot format.
    /// </summary>
    bool TryExportSnapshot(
        TerminalSnapshotExportFormat format,
        in TerminalSnapshotExportOptions options,
        out string snapshot);
}
