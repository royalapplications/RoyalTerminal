// Licensed under the MIT License.
// GhosttySharp.Avalonia — Default selection and clipboard service.

using System.Text;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using GhosttySharp.Avalonia.Controls;
using GhosttySharp.Avalonia.Rendering;
using GhosttySharp.Terminal.Services;

namespace GhosttySharp.Avalonia.Services;

/// <summary>
/// Default implementation for terminal selection and clipboard operations.
/// </summary>
public sealed class DefaultTerminalSelectionService : ITerminalSelectionService
{
    /// <inheritdoc />
    public async Task CopySelectionAsync(
        Control owner,
        ITerminalSessionService sessionService,
        TerminalScreen? screen,
        SkiaTerminalRenderer? renderer)
    {
        string? text = null;

        if (sessionService.Surface is not null)
        {
            text = sessionService.Surface.ReadSelection();
        }
        else if (screen is not null && renderer is not null)
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
    public async Task PasteAsync(Control owner, Action<string> sendInput)
    {
        var clipboard = TopLevel.GetTopLevel(owner)?.Clipboard;
        if (clipboard is null)
        {
            return;
        }

        string? text = await clipboard.TryGetTextAsync();
        if (!string.IsNullOrEmpty(text))
        {
            sendInput(text);
        }
    }

    /// <inheritdoc />
    public void ClearSelection(
        TerminalScreen? screen,
        SkiaTerminalRenderer? renderer,
        GhosttyTerminalPresenter? presenter)
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

            for (int col = colStart; col <= colEnd && col < screen.Columns; col++)
            {
                ref TerminalCell cell = ref termRow[col];
                if (cell.Codepoint > 0)
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
}
