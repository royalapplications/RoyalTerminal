// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Terminal - Optional selection export and paste encoding contracts.

using RoyalTerminal.Avalonia.Rendering;

namespace RoyalTerminal.Terminal;

/// <summary>
/// Viewport-relative text selection range.
/// </summary>
/// <param name="StartColumn">Selection start column.</param>
/// <param name="StartRow">Selection start row.</param>
/// <param name="EndColumn">Selection end column.</param>
/// <param name="EndRow">Selection end row.</param>
/// <param name="Rectangle">Whether the selection is rectangular.</param>
public readonly record struct TerminalSelectionRange(
    int StartColumn,
    int StartRow,
    int EndColumn,
    int EndRow,
    bool Rectangle = false)
{
    /// <summary>
    /// Returns a normalized range with start/end ordered top-to-bottom then left-to-right.
    /// </summary>
    public TerminalSelectionRange Normalize()
    {
        if (StartRow < EndRow || (StartRow == EndRow && StartColumn <= EndColumn))
        {
            return this;
        }

        return new TerminalSelectionRange(
            EndColumn,
            EndRow,
            StartColumn,
            StartRow,
            Rectangle);
    }
}

/// <summary>
/// Optional VT processor capability for exporting selection text.
/// </summary>
public interface ITerminalSelectionExportSource
{
    /// <summary>
    /// Reads the supplied viewport-relative selection.
    /// </summary>
    string? ReadSelection(in TerminalSelectionRange selection);
}

/// <summary>
/// Optional VT processor capability for exporting a screen-row snapshot.
/// </summary>
public interface ITerminalScreenSnapshotSource
{
    /// <summary>
    /// Creates a screen snapshot for the requested absolute row range.
    /// </summary>
    /// <param name="firstAbsoluteRow">The first absolute row to include.</param>
    /// <param name="rowCount">The maximum number of rows to include.</param>
    /// <param name="scrollbackLimit">The scrollback limit to use for the returned snapshot.</param>
    /// <param name="snapshot">The created screen snapshot when successful.</param>
    /// <returns><see langword="true" /> when the requested snapshot was created.</returns>
    bool TryCreateScreenSnapshot(
        int firstAbsoluteRow,
        int rowCount,
        int scrollbackLimit,
        out TerminalScreen snapshot);
}

/// <summary>
/// Optional VT processor capability for encoding clipboard paste payloads.
/// </summary>
public interface ITerminalPasteSequenceEncoderSource
{
    /// <summary>
    /// Returns whether the supplied text is considered safe to paste.
    /// </summary>
    bool IsPasteSafe(string text);

    /// <summary>
    /// Encodes the supplied text into terminal input bytes.
    /// </summary>
    bool TryEncodePaste(string text, bool bracketedPaste, out byte[] sequence);
}
