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
        TerminalPromptClick policy = PromptState.Click;
        if ((uint)column >= (uint)_screen.Columns || (uint)row >= (uint)_screen.ViewportRows ||
            policy is not (TerminalPromptClick.Absolute or TerminalPromptClick.Relative) ||
            !_terminal.GetCursorAtPrompt() ||
            !_terminal.TryGetGridReference(GhosttyPoint.Active(0, _terminal.GetCursorY()), out GhosttyGridRef cursor) ||
            !_terminal.TryGetPointFromGridReference(cursor, GhosttyPointTag.Screen, out GhosttyPointCoordinate cursorPoint) ||
            !_terminal.TryGetGridReference(GhosttyPoint.Viewport((ushort)column, (uint)row), out GhosttyGridRef click) ||
            !_terminal.TryGetPointFromGridReference(click, GhosttyPointTag.Screen, out GhosttyPointCoordinate clickPoint) ||
            !TerminalPromptClickEncoder.TryFindPrompt(new PromptRowReader(_terminal), cursorPoint.Y, out uint prompt) ||
            clickPoint.Y < prompt) return false;
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
}
