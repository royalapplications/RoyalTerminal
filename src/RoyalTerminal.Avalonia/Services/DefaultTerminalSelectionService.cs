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

        ITerminalSelectionSource? selectionSource = sessionService.SelectionSource;
        if (selectionSource is not null)
        {
            text = selectionSource.ReadSelection();
        }

        if (string.IsNullOrEmpty(text) && screen is not null && renderer is not null)
        {
            text = GetSelectedText(screen, renderer);
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
            sendInput(string.Concat(BracketedPasteStart, payload, BracketedPasteEnd));
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
        screen?.InvalidateAll();
        presenter?.Invalidate();
    }

    private static string? GetSelectedText(TerminalScreen screen, SkiaTerminalRenderer renderer)
    {
        if (renderer.SelectionStart is null || renderer.SelectionEnd is null)
        {
            return null;
        }

        (int startCol, int startRow) = renderer.SelectionStart.Value;
        (int endCol, int endRow) = renderer.SelectionEnd.Value;

        if (startRow > endRow || (startRow == endRow && startCol > endCol))
        {
            (startCol, startRow, endCol, endRow) = (endCol, endRow, startCol, startRow);
        }

        StringBuilder sb = new();
        for (int row = startRow; row <= endRow; row++)
        {
            if (row < 0 || row >= screen.ViewportRows)
            {
                continue;
            }

            TerminalRow termRow = screen.GetViewportRow(row);
            int colStart = row == startRow ? startCol : 0;
            int colEnd = row == endRow ? endCol : screen.Columns - 1;
            if (colEnd < 0 || colStart >= screen.Columns)
            {
                continue;
            }

            colStart = Math.Max(0, colStart);
            colEnd = Math.Min(screen.Columns - 1, colEnd);

            for (int col = colStart; col <= colEnd; col++)
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

        return sb.ToString();
    }

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
