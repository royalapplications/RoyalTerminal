// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Globalization;
using System.Text;
using RoyalTerminal.Avalonia.Rendering;

namespace RoyalTerminal.Terminal;

/// <summary>
/// Styled export with deferred blanks and row boundaries, following Ghostty's
/// PageFormatter. HTML runs are properly nested even when style and link changes
/// overlap. VT protection and OSC 8 replay extend the upstream formatter.
/// </summary>
internal sealed class ManagedRichTextFormatter
{
    private readonly TerminalScreen _screen;
    private readonly StringBuilder _output;
    private readonly bool _html;
    private readonly TerminalSnapshotExportExtras _extras;
    private StyleKey? _style;
    private int _hyperlink;
    private bool _protected;

    private ManagedRichTextFormatter(TerminalScreen screen, StringBuilder output, bool html,
        TerminalSnapshotExportExtras extras)
    {
        _screen = screen;
        _output = output;
        _html = html;
        _extras = extras;
    }

    internal static void Append(TerminalScreen screen, StringBuilder output,
        in TerminalSnapshotExportOptions options, bool html)
    {
        ManagedRichTextFormatter writer = new(screen, output, html, options.Extras);
        writer.Open();
        if (screen.Columns > 0 && screen.TotalRows > 0)
        {
            ManagedSnapshotSelection range = options.Selection is TerminalSelectionRange selection
                ? ManagedSnapshotSelection.Create(screen, selection, options.Unwrap)
                : new(0, screen.TotalRows - 1, 0, screen.Columns - 1, false, options.Unwrap);
            writer.AppendRows(range);
        }
        writer.Close();
    }

    /// <summary>Reprints a pending-wrap edge glyph without trimming it.</summary>
    internal static void AppendVtCell(TerminalScreen screen, StringBuilder output,
        in TerminalCell cell, TerminalSnapshotExportExtras extras)
    {
        ManagedRichTextFormatter writer = new(screen, output, false, extras);
        writer.Open();
        writer.AppendCell(cell);
        writer.Close();
    }

    private void Open()
    {
        if (_html)
        {
            _output.Append("<!DOCTYPE html><html><body style=\"margin:0;\"><pre class=\"terminal-snapshot\" style=\"margin:0;font-family:monospace;white-space:pre;color:");
            AppendColor(_screen.DefaultForeground);
            _output.Append(";background-color:");
            AppendColor(_screen.DefaultBackground);
            _output.Append(";\">");
        }
        else
        {
            _output.Append("\u001b[0m");
            if (_extras.IncludeProtection) _output.Append("\u001b[0\"q");
        }
    }

    private void Close()
    {
        CloseRun();
        if (_html) _output.Append("</pre></body></html>");
        else SetProtection(false);
    }

    private void AppendRows(ManagedSnapshotSelection range)
    {
        int blankRows = 0;
        int blankCells = 0;
        for (int index = range.FirstRow; index <= range.LastRow; index++)
        {
            TerminalRow row = _screen.GetRow(index);
            if (!range.TryGetColumns(row, index, out int start, out int end)) continue;
            ReadOnlySpan<TerminalCell> cells = row.ReadOnlyCells;
            bool hasText = false;
            for (int column = start; column <= end; column++)
            {
                if (cells[column].Width != 0 && cells[column].HasContent) { hasText = true; break; }
            }
            // Even background-colored textless rows are deferred upstream.
            if (!hasText) { blankRows++; continue; }
            if (blankRows > 0)
            {
                CloseRun();
                SetProtection(false);
                for (int i = 0; i < blankRows; i++) _output.Append(_html ? "\n" : "\r\n");
            }
            blankRows = !range.Unwrap || !row.WrapsToNext ? 1 : 0;
            if (!range.Unwrap || !row.IsWrapContinuation) blankCells = 0;
            for (int column = start; column <= end; column++)
            {
                ref readonly TerminalCell cell = ref cells[column];
                if (cell.Width == 0 || cell.IsWideSpacerHead) continue;
                // Explicit spaces (styled or not) are content in rich exports.
                // Erased/default cells are emitted only if later content needs them.
                if (!cell.HasContent && !HasStyle(cell) &&
                    !(_extras.IncludeProtection && cell.IsProtected))
                {
                    blankCells++;
                    continue;
                }
                _output.Append(' ', blankCells);
                blankCells = 0;
                AppendCell(cell);
            }
        }
    }

    private static bool HasStyle(in TerminalCell cell) =>
        cell.ForegroundIdentity.Kind != TerminalColorKind.Default ||
        cell.BackgroundIdentity.Kind != TerminalColorKind.Default ||
        cell.Attributes != CellAttributes.None || cell.Decorations != CellDecorations.None ||
        cell.UnderlineStyle != TerminalUnderlineStyle.None || cell.HasUnderlineColor;

    private void AppendCell(in TerminalCell cell)
    {
        StyleKey style = new(cell);
        int link = _extras.IncludeHyperlinks &&
            _screen.TryGetHyperlinkUrl(cell.HyperlinkId, out _) ? cell.HyperlinkId : 0;
        if (_style is null || _style.Value != style || _hyperlink != link)
        {
            CloseRun();
            _style = style;
            _hyperlink = link;
            if (_html)
            {
                if (link != 0)
                {
                    _screen.TryGetHyperlinkUrl(link, out string? url);
                    _output.Append("<a href=\"");
                    AppendHtml(url.AsSpan());
                    _output.Append("\" style=\"color:inherit;text-decoration:inherit;\">");
                }
                _output.Append("<span style=\"");
                AppendHtmlStyle(cell);
                _output.Append("\">");
            }
            else
            {
                ManagedSgrFormatter.Append(_output, cell);
                if (link != 0) AppendVtHyperlink(link);
            }
        }
        SetProtection(cell.IsProtected);
        if (!string.IsNullOrEmpty(cell.Grapheme)) AppendText(cell.Grapheme.AsSpan());
        else if (cell.Codepoint == 0) _output.Append(' ');
        else
        {
            Span<char> scalar = stackalloc char[2];
            Rune rune = Rune.TryCreate(cell.Codepoint, out Rune valid) ? valid : Rune.ReplacementChar;
            AppendText(scalar[..rune.EncodeToUtf16(scalar)]);
        }
    }

    private void CloseRun()
    {
        if (_style is not null)
        {
            _output.Append(_html ? "</span>" : "\u001b[0m");
            _style = null;
        }
        if (_hyperlink != 0)
        {
            _output.Append(_html ? "</a>" : "\u001b]8;;\u001b\\");
            _hyperlink = 0;
        }
    }

    private void SetProtection(bool value)
    {
        if (_html || !_extras.IncludeProtection || value == _protected) return;
        _output.Append(value ? "\u001b[1\"q" : "\u001b[0\"q");
        _protected = value;
    }

    private void AppendVtHyperlink(int token)
    {
        _output.Append("\u001b]8;");
        if (_screen.TryGetHyperlink(token, out TerminalHyperlink? link) && link!.IsExplicit)
            _output.Append("id=").Append(Encoding.UTF8.GetString(link.ExplicitId));
        _screen.TryGetHyperlinkUrl(token, out string? url);
        _output.Append(';').Append(url).Append("\u001b\\");
    }

    private void AppendText(ReadOnlySpan<char> text)
    {
        if (_html) AppendHtml(text);
        else _output.Append(text);
    }

    private void AppendHtml(ReadOnlySpan<char> text)
    {
        // Append runs directly; no per-cell text or HTML-encoded strings.
        while (!text.IsEmpty)
        {
            int special = text.IndexOfAny("&<>\"'".AsSpan());
            if (special < 0) { _output.Append(text); break; }
            _output.Append(text[..special]);
            _output.Append(text[special] switch
            {
                '&' => "&amp;", '<' => "&lt;", '>' => "&gt;", '"' => "&quot;", _ => "&#39;",
            });
            text = text[(special + 1)..];
        }
    }

    private void AppendHtmlStyle(in TerminalCell cell)
    {
        uint foreground = cell.Foreground;
        uint background = cell.HasBackground ? cell.Background : _screen.DefaultBackground;
        if ((cell.Attributes & CellAttributes.Inverse) != 0) (foreground, background) = (background, foreground);
        _output.Append("color:");
        AppendColor(foreground);
        _output.Append(";background-color:");
        AppendColor(background);
        _output.Append(';');
        if ((cell.Attributes & CellAttributes.Bold) != 0) _output.Append("font-weight:bold;");
        if ((cell.Attributes & CellAttributes.Italic) != 0) _output.Append("font-style:italic;");
        if ((cell.Attributes & CellAttributes.Dim) != 0) _output.Append("opacity:0.5;");
        if ((cell.Attributes & CellAttributes.Hidden) != 0) _output.Append("visibility:hidden;");
        TerminalUnderlineStyle underline = cell.UnderlineStyle;
        if (underline == TerminalUnderlineStyle.None && (cell.Attributes & CellAttributes.Underline) != 0)
            underline = TerminalUnderlineStyle.Single;
        if (underline != TerminalUnderlineStyle.None ||
            (cell.Attributes & (CellAttributes.Strikethrough | CellAttributes.Blink)) != 0 ||
            (cell.Decorations & CellDecorations.Overline) != 0)
        {
            _output.Append("text-decoration-line:");
            if (underline != TerminalUnderlineStyle.None) _output.Append(" underline");
            if ((cell.Attributes & CellAttributes.Strikethrough) != 0) _output.Append(" line-through");
            if ((cell.Decorations & CellDecorations.Overline) != 0) _output.Append(" overline");
            if ((cell.Attributes & CellAttributes.Blink) != 0) _output.Append(" blink");
            _output.Append(';');
        }
        if (underline != TerminalUnderlineStyle.None)
        {
            _output.Append("text-decoration-style:").Append(underline switch
            {
                TerminalUnderlineStyle.Double => "double", TerminalUnderlineStyle.Curly => "wavy",
                TerminalUnderlineStyle.Dotted => "dotted", TerminalUnderlineStyle.Dashed => "dashed", _ => "solid",
            }).Append(';');
        }
        if (cell.HasUnderlineColor)
        {
            _output.Append("text-decoration-color:");
            AppendColor(cell.UnderlineColor);
            _output.Append(';');
        }
    }

    private void AppendColor(uint color)
    {
        Span<char> digits = stackalloc char[6];
        (color & 0xFFFFFF).TryFormat(digits, out _, "X6", CultureInfo.InvariantCulture);
        _output.Append('#').Append(digits);
    }

    private readonly record struct StyleKey(TerminalColorIdentity ForegroundIdentity,
        TerminalColorIdentity BackgroundIdentity, TerminalColorIdentity UnderlineIdentity,
        uint Foreground, uint Background, uint UnderlineColor, CellAttributes Attributes,
        CellDecorations Decorations, TerminalUnderlineStyle Underline, bool HasBackground, bool HasUnderlineColor)
    {
        internal StyleKey(in TerminalCell cell) : this(cell.ForegroundIdentity, cell.BackgroundIdentity,
            cell.UnderlineIdentity, cell.Foreground, cell.Background, cell.UnderlineColor, cell.Attributes,
            cell.Decorations, cell.UnderlineStyle, cell.HasBackground, cell.HasUnderlineColor) { }
    }
}
