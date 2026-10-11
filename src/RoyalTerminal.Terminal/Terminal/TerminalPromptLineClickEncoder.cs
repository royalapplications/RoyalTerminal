// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Avalonia.Rendering;

namespace RoyalTerminal.Terminal;

internal readonly record struct TerminalPromptLine(
    TerminalSemanticPrompt Prompt, bool WrapsToNext, bool IsWrapContinuation);

// Selecting a row once lets native readers reuse a borrowed grid reference for
// every cell. The caller serializes the entire operation with terminal mutation.
internal interface ITerminalPromptLineReader
{
    bool TryReadRow(uint row, out TerminalPromptLine line);
    bool TryReadInputCell(int column, out bool input);
}

internal static class TerminalPromptLineClickEncoder
{
    internal static bool Supports(TerminalPromptClick policy) =>
        policy is TerminalPromptClick.Line or TerminalPromptClick.Multiple or
            TerminalPromptClick.ConservativeVertical or TerminalPromptClick.SmartVertical;

    // Port of Ghostty Screen.promptClickLine: all cl policies currently use
    // horizontal movement within one logical line, counting input-marked cells.
    internal static bool TryEncode<T>(ref T reader, int columns,
        (int Column, uint Row) cursor, (int Column, uint Row) click,
        TerminalSemanticContent cursorContent, bool applicationCursorKeys, out byte[] sequence)
        where T : struct, ITerminalPromptLineReader
    {
        sequence = [];
        if (cursor == click ||
            !reader.TryReadRow(cursor.Row, out TerminalPromptLine line) ||
            !reader.TryReadInputCell(cursor.Column, out bool cursorCellInput) ||
            (cursorContent != TerminalSemanticContent.Input && !cursorCellInput)) return false;

        bool right = cursor.Row < click.Row || (cursor.Row == click.Row && cursor.Column < click.Column);
        int count = 0;
        uint row = cursor.Row;
        while (true)
        {
            if (right && row != cursor.Row && line.Prompt != TerminalSemanticPrompt.PromptContinuation) break;
            int column = right
                ? row == cursor.Row ? cursor.Column + 1 : 0
                : row == cursor.Row ? cursor.Column - 1 : columns - 1;
            for (; (uint)column < (uint)columns; column += right ? 1 : -1)
            {
                if (!reader.TryReadInputCell(column, out bool input)) return false;
                if (!input) continue;
                // The public encoder returns one byte array, unlike Ghostty's
                // queued individual arrows. Fail without emitting partial input
                // if that result cannot be represented by a managed array.
                if (count == Array.MaxLength / 3) return false;
                count++;
                if (row == click.Row && column == click.Column)
                    return EncodeArrows(count, right, applicationCursorKeys, out sequence);
            }

            if (right ? !line.WrapsToNext : !line.IsWrapContinuation)
            {
                // A printed input cell can move one step beyond the last input.
                // A cursor already on the empty end cell must not overshoot it.
                if (right && cursorCellInput)
                {
                    if (count == Array.MaxLength / 3) return false;
                    count++;
                }
                break;
            }

            if (row == click.Row) break;
            row = right ? row + 1 : row - 1;
            if (!reader.TryReadRow(row, out line)) return false;
        }

        return EncodeArrows(count, right, applicationCursorKeys, out sequence);
    }

    private static bool EncodeArrows(int count, bool right, bool applicationCursorKeys, out byte[] sequence)
    {
        sequence = [];
        if (count == 0) return false;
        sequence = GC.AllocateUninitializedArray<byte>(count * 3);
        byte prefix = applicationCursorKeys ? (byte)'O' : (byte)'[';
        byte direction = right ? (byte)'C' : (byte)'D';
        for (int offset = 0; offset < sequence.Length; offset += 3)
        {
            sequence[offset] = 0x1b;
            sequence[offset + 1] = prefix;
            sequence[offset + 2] = direction;
        }
        return true;
    }
}
