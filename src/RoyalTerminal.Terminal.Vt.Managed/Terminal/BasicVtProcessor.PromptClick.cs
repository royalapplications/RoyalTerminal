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
        if (_inAltScreen || (uint)column >= (uint)_screen.Columns || (uint)row >= (uint)_screen.ViewportRows ||
            PromptState.Click is not (TerminalPromptClick.Absolute or TerminalPromptClick.Relative)) return false;
        uint cursor = (uint)(_screen.TotalRows - _screen.ViewportRows + _cursorRow);
        PromptRowReader reader = new(_screen);
        if (!reader.TryReadPrompt(cursor, out TerminalSemanticPrompt marker) ||
            marker == TerminalSemanticPrompt.None && PromptState.Content == TerminalSemanticContent.Output ||
            !TerminalPromptClickEncoder.TryFindPrompt(reader, cursor, out uint prompt)) return false;
        uint click = (uint)(_screen.TotalRows - _screen.ViewportRows - _screen.ScrollOffset + row);
        if (click < prompt) return false;
        sequence = TerminalPromptClickEncoder.Encode(column,
            PromptState.Click == TerminalPromptClick.Relative ? click - prompt + 1 : (uint)row + 1);
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
}
