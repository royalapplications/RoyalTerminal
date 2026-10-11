// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.GhosttySharp;
using static RoyalTerminal.GhosttySharp.Native.GhosttyVtNative;

namespace RoyalTerminal.Terminal;

public sealed partial class GhosttyVtProcessor : ITerminalPromptClickEncoderSource
{
    /// <inheritdoc />
    public bool TryEncodePromptClick(int column, int row, out byte[] sequence)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        sequence = [];
        TerminalPromptState state = PromptState;
        TerminalPromptClick policy = state.Click;
        bool lineClick = TerminalPromptLineClickEncoder.Supports(policy);
        if ((uint)column >= (uint)_screen.Columns || (uint)row >= (uint)_screen.ViewportRows ||
            (policy is not (TerminalPromptClick.Absolute or TerminalPromptClick.Relative) &&
                !lineClick) ||
            !_terminal.GetCursorAtPrompt() ||
            !_terminal.TryGetGridReference(GhosttyPoint.Active(_terminal.GetCursorX(), _terminal.GetCursorY()), out GhosttyGridRef cursor) ||
            !_terminal.TryGetPointFromGridReference(cursor, GhosttyPointTag.Screen, out GhosttyPointCoordinate cursorPoint) ||
            !_terminal.TryGetGridReference(GhosttyPoint.Viewport((ushort)column, (uint)row), out GhosttyGridRef click) ||
            !_terminal.TryGetPointFromGridReference(click, GhosttyPointTag.Screen, out GhosttyPointCoordinate clickPoint) ||
            !TerminalPromptClickEncoder.TryFindPrompt(new PromptRowReader(_terminal), cursorPoint.Y, out uint prompt,
                includeRetainedContinuation: lineClick) ||
            clickPoint.Y < prompt) return false;
        if (lineClick)
        {
            PromptLineReader reader = new(_terminal);
            return TerminalPromptLineClickEncoder.TryEncode(ref reader, _screen.Columns,
                (cursorPoint.X, cursorPoint.Y), (column, clickPoint.Y), state.Content, _applicationCursorKeys, out sequence);
        }
        sequence = TerminalPromptClickEncoder.Encode(column,
            policy == TerminalPromptClick.Relative ? clickPoint.Y - prompt + 1 : (uint)row + 1);
        return true;
    }

    private readonly struct PromptRowReader(GhosttyTerminal terminal) : ITerminalPromptRowReader
    {
        public unsafe bool TryReadPrompt(uint row, out TerminalSemanticPrompt prompt)
        {
            prompt = TerminalSemanticPrompt.None;
            GhosttyRowSemanticPrompt value = default;
            if (!terminal.TryGetGridReference(GhosttyPoint.Screen(0, row), out GhosttyGridRef reference) ||
                GridRefRow(reference, out ulong raw) != GhosttyResult.Success ||
                RowGet(raw, GhosttyRowData.SemanticPrompt, &value) != GhosttyResult.Success) return false;
            prompt = (TerminalSemanticPrompt)value;
            return true;
        }
    }

    private struct PromptLineReader(GhosttyTerminal terminal) : ITerminalPromptLineReader
    {
        private GhosttyGridRef _row;

        public unsafe bool TryReadRow(uint row, out TerminalPromptLine line)
        {
            line = default;
            bool wrap = false, continuation = false;
            GhosttyRowSemanticPrompt prompt = default;
            if (!terminal.TryGetGridReference(GhosttyPoint.Screen(0, row), out _row) ||
                GridRefRow(in _row, out ulong raw) != GhosttyResult.Success ||
                RowGet(raw, GhosttyRowData.Wrap, &wrap) != GhosttyResult.Success ||
                RowGet(raw, GhosttyRowData.WrapContinuation, &continuation) != GhosttyResult.Success ||
                RowGet(raw, GhosttyRowData.SemanticPrompt, &prompt) != GhosttyResult.Success) return false;
            line = new((TerminalSemanticPrompt)prompt, wrap, continuation);
            return true;
        }

        public unsafe bool TryReadInputCell(int column, out bool input)
        {
            input = false;
            _row.X = checked((ushort)column);
            GhosttyCellSemanticContent content = default;
            if (GridRefCell(in _row, out ulong cell) != GhosttyResult.Success ||
                CellGet(cell, GhosttyCellData.SemanticContent, &content) != GhosttyResult.Success) return false;
            input = content == GhosttyCellSemanticContent.Input;
            return true;
        }
    }
}
