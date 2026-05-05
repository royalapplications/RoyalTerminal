// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia — Default selection and clipboard service.

using System.Text;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using RoyalTerminal.Avalonia.Controls;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Services;

namespace RoyalTerminal.Avalonia.Services;

/// <summary>
/// Default implementation for terminal selection and clipboard operations.
/// </summary>
public sealed class DefaultTerminalSelectionService : ITerminalSelectionService
{
    private const string BracketedPasteStart = "\x1b[200~";
    private const string BracketedPasteEnd = "\x1b[201~";

    /// <inheritdoc />
    public async Task CopySelectionAsync(
        Control owner,
        ITerminalSessionService sessionService,
        TerminalScreen? screen,
        SkiaTerminalRenderer? renderer)
    {
        string? text = null;
        bool usedRendererSelection = false;

        if (screen is not null &&
            renderer is not null &&
            !renderer.GetSelectionSpans().IsEmpty)
        {
            usedRendererSelection = true;
            text = GetSelectedText(screen, renderer.GetSelectionSpans());
        }
        else if (screen is not null &&
            renderer is not null &&
            TryCreateRendererSelectionRange(renderer, out RendererSelectionRange selection))
        {
            usedRendererSelection = true;
            text = GetSelectedText(screen, selection);
        }

        if (!usedRendererSelection && string.IsNullOrEmpty(text))
        {
            ITerminalSelectionSource? selectionSource = sessionService.SelectionSource;
            if (selectionSource is not null)
            {
                text = selectionSource.ReadSelection();
            }
        }

        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var clipboard = TopLevel.GetTopLevel(owner)?.Clipboard;
        if (clipboard is not null)
        {
            await clipboard.SetTextAsync(text);
        }
    }

    /// <inheritdoc />
    public Task PasteAsync(Control owner, Action<string> sendInput)
    {
        return PasteAsync(
            owner,
            sendInput,
            new TerminalPasteRequest(
                BracketedPasteEnabled: false,
                SafetyPolicy: TerminalPasteSafetyPolicy.None));
    }

    /// <inheritdoc />
    public async Task PasteAsync(Control owner, Action<string> sendInput, TerminalPasteRequest request)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(sendInput);

        var clipboard = TopLevel.GetTopLevel(owner)?.Clipboard;
        if (clipboard is null)
        {
            return;
        }

        string? text = await clipboard.TryGetTextAsync();
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        string payload = text;
        TerminalPasteRisk risk = EvaluatePasteRisk(payload);

        switch (request.SafetyPolicy)
        {
            case TerminalPasteSafetyPolicy.BlockUnsafe:
                if (risk != TerminalPasteRisk.None)
                {
                    return;
                }

                break;

            case TerminalPasteSafetyPolicy.SanitizeControlSequences:
                payload = SanitizeControlCharacters(payload);
                if (payload.Length == 0)
                {
                    return;
                }

                break;

            case TerminalPasteSafetyPolicy.ConfirmUnsafe:
                if (risk != TerminalPasteRisk.None)
                {
                    TerminalUnsafePasteHandler? handler = request.UnsafePasteHandler;
                    if (handler is null)
                    {
                        return;
                    }

                    TerminalPasteSafetyDecision decision =
                        await handler(new TerminalPasteContext(payload, risk));

                    if (decision == TerminalPasteSafetyDecision.Cancel)
                    {
                        return;
                    }

                    if (decision == TerminalPasteSafetyDecision.Sanitize)
                    {
                        payload = SanitizeControlCharacters(payload);
                        if (payload.Length == 0)
                        {
                            return;
                        }
                    }
                }

                break;

            case TerminalPasteSafetyPolicy.None:
            default:
                break;
        }

        if (request.BracketedPasteEnabled)
        {
            if (TrySendEncodedPaste(owner, payload, bracketedPaste: true))
            {
                return;
            }

            sendInput(string.Concat(BracketedPasteStart, payload, BracketedPasteEnd));
            return;
        }

        if (TrySendEncodedPaste(owner, payload, bracketedPaste: false))
        {
            return;
        }

        sendInput(payload);
    }

    /// <inheritdoc />
    public void ClearSelection(
        TerminalScreen? screen,
        SkiaTerminalRenderer? renderer,
        TerminalPresenter? presenter)
    {
        if (renderer is null)
        {
            return;
        }

        renderer.SelectionStart = null;
        renderer.SelectionEnd = null;
        renderer.SelectionIsRectangle = false;
        renderer.SetSelectionSpans(ReadOnlySpan<TerminalHighlightSpan>.Empty);
        if (screen is not null)
        {
            lock (screen.SyncRoot)
            {
                screen.InvalidateViewport();
            }
        }

        presenter?.Invalidate();
    }

    private static bool TryCreateRendererSelectionRange(SkiaTerminalRenderer renderer, out RendererSelectionRange selection)
    {
        if (renderer.SelectionStart is not { } start || renderer.SelectionEnd is not { } end)
        {
            selection = default;
            return false;
        }

        if (renderer.SelectionIsRectangle)
        {
            selection = new RendererSelectionRange(
                Math.Min(start.Column, end.Column),
                Math.Min(start.Row, end.Row),
                Math.Max(start.Column, end.Column),
                Math.Max(start.Row, end.Row),
                Rectangle: true);
            return true;
        }

        int startColumn = start.Column;
        int startRow = start.Row;
        int endColumnExclusive = end.Column;
        int endRow = end.Row;
        if (startRow > endRow || (startRow == endRow && startColumn > endColumnExclusive))
        {
            (startColumn, startRow, endColumnExclusive, endRow) = (endColumnExclusive, endRow, startColumn, startRow);
        }

        selection = new RendererSelectionRange(startColumn, startRow, endColumnExclusive, endRow, Rectangle: false);
        return true;
    }

    private static bool TrySendEncodedPaste(
        Control owner,
        string payload,
        bool bracketedPaste)
    {
        if (owner is TerminalControl terminalControl &&
            terminalControl.ActiveVtProcessor is ITerminalPasteSequenceEncoderSource pasteEncoder &&
            pasteEncoder.TryEncodePaste(payload, bracketedPaste, out byte[] sequence))
        {
            terminalControl.SendInput(sequence);
            return true;
        }

        return false;
    }

    private static string? GetSelectedText(TerminalScreen screen, RendererSelectionRange selection)
    {
        int startCol = selection.StartColumn;
        int startRow = selection.StartRow;
        int endColExclusive = selection.EndColumnExclusive;
        int endRow = selection.EndRow;

        StringBuilder sb = new();
        lock (screen.SyncRoot)
        {
            int viewportTopAbsoluteRow = screen.ViewportTopAbsoluteRow;
            for (int row = startRow; row <= endRow; row++)
            {
                int absoluteRow = viewportTopAbsoluteRow + row;
                if (absoluteRow < 0 || absoluteRow >= screen.TotalRows)
                {
                    continue;
                }

                TerminalRow termRow = screen.GetRow(absoluteRow);
                int colStart = selection.Rectangle || row == startRow ? startCol : 0;
                int colEndExclusive = selection.Rectangle || row == endRow ? endColExclusive : screen.Columns;
                if (colEndExclusive <= 0 || colStart >= screen.Columns)
                {
                    continue;
                }

                colStart = Math.Max(0, colStart);
                colEndExclusive = Math.Min(screen.Columns, colEndExclusive);

                for (int col = colStart; col < colEndExclusive; col++)
                {
                    ref TerminalCell cell = ref termRow[col];
                    if (cell.Width == 0)
                    {
                        continue;
                    }

                    if (!string.IsNullOrEmpty(cell.Grapheme))
                    {
                        sb.Append(cell.Grapheme);
                    }
                    else if (cell.Codepoint > 0 && Rune.IsValid(cell.Codepoint))
                    {
                        sb.Append(char.ConvertFromUtf32(cell.Codepoint));
                    }
                    else
                    {
                        sb.Append(' ');
                    }
                }

                if (row < endRow)
                {
                    sb.AppendLine();
                }
            }
        }

        return sb.ToString();
    }

    private static string? GetSelectedText(TerminalScreen screen, ReadOnlySpan<TerminalHighlightSpan> selectionSpans)
    {
        if (selectionSpans.IsEmpty)
        {
            return null;
        }

        TerminalHighlightSpan[] spans = selectionSpans.ToArray();
        Array.Sort(spans, static (left, right) =>
        {
            int rowComparison = left.Row.CompareTo(right.Row);
            return rowComparison != 0
                ? rowComparison
                : left.StartColumn.CompareTo(right.StartColumn);
        });

        StringBuilder sb = new();
        lock (screen.SyncRoot)
        {
            int viewportTopAbsoluteRow = screen.ViewportTopAbsoluteRow;
            int previousRow = int.MinValue;
            int previousEndColumn = -1;
            for (int i = 0; i < spans.Length; i++)
            {
                TerminalHighlightSpan span = spans[i];
                int absoluteRow = viewportTopAbsoluteRow + span.Row;
                if (absoluteRow < 0 || absoluteRow >= screen.TotalRows)
                {
                    continue;
                }

                bool wrappedContinuation = previousRow != int.MinValue &&
                    span.Row == previousRow + 1 &&
                    previousEndColumn >= screen.Columns - 1 &&
                    span.StartColumn == 0;
                if (previousRow != int.MinValue && span.Row != previousRow && !wrappedContinuation)
                {
                    sb.AppendLine();
                }

                previousRow = span.Row;
                previousEndColumn = span.EndColumn;
                TerminalRow termRow = screen.GetRow(absoluteRow);
                int colStart = Math.Clamp(span.StartColumn, 0, screen.Columns);
                int colEndInclusive = Math.Clamp(span.EndColumn, -1, Math.Max(0, screen.Columns - 1));
                if (colStart > colEndInclusive)
                {
                    continue;
                }

                AppendCells(sb, termRow, colStart, colEndInclusive + 1);
            }
        }

        return sb.ToString();
    }

    private static void AppendCells(StringBuilder sb, TerminalRow termRow, int colStart, int colEndExclusive)
    {
        for (int col = colStart; col < colEndExclusive; col++)
        {
            ref TerminalCell cell = ref termRow[col];
            if (cell.Width == 0)
            {
                continue;
            }

            if (!string.IsNullOrEmpty(cell.Grapheme))
            {
                sb.Append(cell.Grapheme);
            }
            else if (cell.Codepoint > 0 && Rune.IsValid(cell.Codepoint))
            {
                sb.Append(char.ConvertFromUtf32(cell.Codepoint));
            }
            else
            {
                sb.Append(' ');
            }
        }
    }

    private readonly record struct RendererSelectionRange(
        int StartColumn,
        int StartRow,
        int EndColumnExclusive,
        int EndRow,
        bool Rectangle);

    private static TerminalPasteRisk EvaluatePasteRisk(string text)
    {
        TerminalPasteRisk risk = TerminalPasteRisk.None;

        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            if (ch is '\n' or '\r')
            {
                risk |= TerminalPasteRisk.Multiline;
            }

            if (IsUnsafeControlCharacter(ch))
            {
                risk |= TerminalPasteRisk.ControlSequence;
            }

            if (risk == (TerminalPasteRisk.Multiline | TerminalPasteRisk.ControlSequence))
            {
                return risk;
            }
        }

        return risk;
    }

    private static string SanitizeControlCharacters(string text)
    {
        StringBuilder builder = new(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            if (!IsUnsafeControlCharacter(ch))
            {
                builder.Append(ch);
            }
        }

        return builder.ToString();
    }

    private static bool IsUnsafeControlCharacter(char ch)
    {
        if (ch is '\n' or '\r' or '\t')
        {
            return false;
        }

        return ch < ' ' || ch == '\u007f' || (ch >= '\u0080' && ch <= '\u009f');
    }
}
