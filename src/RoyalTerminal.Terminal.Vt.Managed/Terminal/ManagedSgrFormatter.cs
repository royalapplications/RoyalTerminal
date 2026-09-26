// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Globalization;
using System.Text;
using RoyalTerminal.Avalonia.Rendering;

namespace RoyalTerminal.Terminal;

/// <summary>Self-contained VT style emission retaining logical color identity.</summary>
internal static class ManagedSgrFormatter
{
    internal static void Append(StringBuilder builder, in TerminalCell cell)
    {
        // Match Ghostty's Style.VTFormatter; never serialize resolved paint colors
        // in place of default/palette references. This also restores underline
        // colors that DECRPSS's narrower current-SGR response does not include.
        builder.Append("\u001b[0m");
        if ((cell.Attributes & CellAttributes.Bold) != 0) builder.Append("\u001b[1m");
        if ((cell.Attributes & CellAttributes.Dim) != 0) builder.Append("\u001b[2m");
        if ((cell.Attributes & CellAttributes.Italic) != 0) builder.Append("\u001b[3m");
        if ((cell.Attributes & CellAttributes.Blink) != 0) builder.Append("\u001b[5m");
        if ((cell.Attributes & CellAttributes.Inverse) != 0) builder.Append("\u001b[7m");
        if ((cell.Attributes & CellAttributes.Hidden) != 0) builder.Append("\u001b[8m");
        if ((cell.Attributes & CellAttributes.Strikethrough) != 0) builder.Append("\u001b[9m");
        if ((cell.Decorations & CellDecorations.Overline) != 0) builder.Append("\u001b[53m");
        TerminalUnderlineStyle underline = cell.UnderlineStyle;
        if (underline == TerminalUnderlineStyle.None && (cell.Attributes & CellAttributes.Underline) != 0)
            underline = TerminalUnderlineStyle.Single;
        switch (underline)
        {
            case TerminalUnderlineStyle.Single: builder.Append("\u001b[4m"); break;
            case TerminalUnderlineStyle.Double: builder.Append("\u001b[4:2m"); break;
            case TerminalUnderlineStyle.Curly: builder.Append("\u001b[4:3m"); break;
            case TerminalUnderlineStyle.Dotted: builder.Append("\u001b[4:4m"); break;
            case TerminalUnderlineStyle.Dashed: builder.Append("\u001b[4:5m"); break;
        }
        AppendColor(builder, "\u001b[38", cell.ForegroundIdentity);
        AppendColor(builder, "\u001b[48", cell.BackgroundIdentity);
        if (cell.HasUnderlineColor) AppendColor(builder, "\u001b[58", cell.UnderlineIdentity);
    }

    private static void AppendColor(StringBuilder builder, string prefix, TerminalColorIdentity color)
    {
        if (color.Kind == TerminalColorKind.Default) return;
        builder.Append(prefix);
        if (color.Kind == TerminalColorKind.Palette)
        {
            builder.Append(";5;");
            AppendNumber(builder, color.Value);
        }
        else
        {
            builder.Append(";2;");
            AppendNumber(builder, (color.Value >> 16) & 255);
            builder.Append(';');
            AppendNumber(builder, (color.Value >> 8) & 255);
            builder.Append(';');
            AppendNumber(builder, color.Value & 255);
        }
        builder.Append('m');
    }

    private static void AppendNumber(StringBuilder builder, uint value)
    {
        Span<char> digits = stackalloc char[10];
        value.TryFormat(digits, out int written, provider: CultureInfo.InvariantCulture);
        builder.Append(digits[..written]);
    }
}
