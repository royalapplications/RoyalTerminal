// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Avalonia.Rendering;

namespace RoyalTerminal.Terminal;

public sealed partial class BasicVtProcessor : ITerminalPromptClickEncoderSource
{
    /// <inheritdoc />
    public bool TryEncodePromptClick(int column, int row, out byte[] sequence)
    {
        sequence = [];
        TerminalPromptState state = PromptState;
        bool lineClick = TerminalPromptLineClickEncoder.Supports(state.Click);
        if (_inAltScreen || (uint)column >= (uint)_screen.Columns || (uint)row >= (uint)_screen.ViewportRows ||
            (state.Click is not (TerminalPromptClick.Absolute or TerminalPromptClick.Relative) &&
                !lineClick)) return false;
        uint cursor = (uint)(_screen.TotalRows - _screen.ViewportRows + _cursorRow);
        PromptRowReader reader = new(_screen);
        if (!reader.TryReadPrompt(cursor, out TerminalSemanticPrompt marker) ||
            marker == TerminalSemanticPrompt.None && state.Content == TerminalSemanticContent.Output ||
            !TerminalPromptClickEncoder.TryFindPrompt(reader, cursor, out uint prompt,
                includeRetainedContinuation: lineClick)) return false;
        uint click = (uint)(_screen.TotalRows - _screen.ViewportRows - _screen.ScrollOffset + row);
        if (click < prompt) return false;
        if (lineClick)
        {
            PromptLineReader lineReader = new(_screen);
            return TerminalPromptLineClickEncoder.TryEncode(ref lineReader, _screen.Columns,
                (_cursorCol, cursor), (column, click), state.Content, _applicationCursorKeys, out sequence);
        }
        sequence = TerminalPromptClickEncoder.Encode(column,
            state.Click == TerminalPromptClick.Relative ? click - prompt + 1 : (uint)row + 1);
        return true;
    }

    private readonly struct PromptRowReader(TerminalScreen screen) : ITerminalPromptRowReader
    {
        public bool TryReadPrompt(uint row, out TerminalSemanticPrompt prompt)
        {
            prompt = TerminalSemanticPrompt.None;
            if (row >= (uint)screen.TotalRows) return false;
            prompt = screen.GetRow((int)row).SemanticPrompt;
            return true;
        }
    }

    private struct PromptLineReader(TerminalScreen screen) : ITerminalPromptLineReader
    {
        private TerminalRow? _row;

        public bool TryReadRow(uint row, out TerminalPromptLine line)
        {
            _row = row < (uint)screen.TotalRows ? screen.GetRow((int)row) : null;
            line = _row is null ? default : new(_row.SemanticPrompt, _row.WrapsToNext, _row.IsWrapContinuation);
            return _row is not null;
        }

        public bool TryReadInputCell(int column, out bool input)
        {
            input = false;
            if (_row is null || (uint)column >= (uint)_row.Columns) return false;
            input = _row.ReadOnlyCells[column].SemanticContent == TerminalSemanticContent.Input;
            return true;
        }
    }
}
