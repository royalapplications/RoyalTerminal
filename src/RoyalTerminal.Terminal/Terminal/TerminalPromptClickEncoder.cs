// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers.Text;
using RoyalTerminal.Avalonia.Rendering;

namespace RoyalTerminal.Terminal;

/// <summary>Encodes shell-requested OSC 133 click events using live terminal coordinates.</summary>
public interface ITerminalPromptClickEncoderSource
{
    /// <summary>
    /// Encodes a primary click at a zero-based viewport cell. Returns false outside
    /// the current prompt or when no click-event policy is active. The host must
    /// exclude selections, drags, modified clicks and application mouse reporting,
    /// and serialize this operation with processor access.
    /// </summary>
    bool TryEncodePromptClick(int column, int row, out byte[] sequence);
}

internal interface ITerminalPromptRowReader
{
    bool TryReadPrompt(uint row, out TerminalSemanticPrompt prompt);
}

internal static class TerminalPromptClickEncoder
{
    // Ghostty Pin.PromptIterator.nextLeftUp. Read absolute rows so a prompt
    // spanning multiple native pages or a scrolled viewport has one coordinate space.
    internal static bool TryFindPrompt<T>(T reader, uint cursorRow, out uint promptRow)
        where T : struct, ITerminalPromptRowReader
    {
        promptRow = 0;
        uint row = cursorRow;
        while (reader.TryReadPrompt(row, out TerminalSemanticPrompt prompt))
        {
            if (prompt == TerminalSemanticPrompt.Prompt) { promptRow = row; return true; }
            if (prompt == TerminalSemanticPrompt.PromptContinuation)
            {
                uint end = row;
                while (end > 0 && reader.TryReadPrompt(end - 1, out TerminalSemanticPrompt prior))
                {
                    if (prior == TerminalSemanticPrompt.None) { promptRow = end; return true; }
                    if (prior == TerminalSemanticPrompt.Prompt) { promptRow = end - 1; return true; }
                    end--;
                }
                // Match native's original continuation pin when history was trimmed.
                promptRow = row;
                return true;
            }
            if (row == 0) break;
            row--;
        }
        return false;
    }

    internal static byte[] Encode(int column, uint row)
    {
        Span<byte> buffer = stackalloc byte[32];
        "\u001b[<0;"u8.CopyTo(buffer);
        Utf8Formatter.TryFormat((uint)column + 1, buffer[5..], out int width);
        int length = 5 + width;
        buffer[length++] = (byte)';';
        Utf8Formatter.TryFormat(row, buffer[length..], out width);
        length += width;
        buffer[length++] = (byte)'M';
        return buffer[..length].ToArray();
    }
}
