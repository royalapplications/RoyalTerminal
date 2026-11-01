// Copyright (c) Royal Apps. Licensed under the MIT license.
using System.Runtime.InteropServices.JavaScript;
using RoyalTerminal.Terminal;
using RoyalTerminal.Avalonia.Rendering;
namespace RoyalTerminal.BrowserReplica;

public static partial class Replica
{
    /// <summary>Copies the current active buffer into a frozen, browser-selectable transcript. No parser mutation or remote input.</summary>
    [JSExport]
    public static string Scrollback(int id)
    {
        const int maxChars = 4 << 20;
        var state = States[id]; state.Processor.RefreshTimedState();
        var screen = state.Screen;
        bool alternate = state.Processor.AlternateScreen;
        int rows = alternate ? screen.ViewportRows : screen.TotalRows;
        long bound = rows;
        for (int y = 0; y < rows; ++y)
        {
            var row = alternate ? screen.GetViewportRow(y) : screen.GetRow(y);
            foreach (ref readonly TerminalCell cell in row.ReadOnlyCells)
            {
                // Conservative upper bound for deferred spaces, UTF-16 scalars and full grapheme strings.
                bound += Math.Max(2, cell.Grapheme?.Length ?? 0);
                if (bound > maxChars) throw new InvalidDataException("Retained text exceeds the browser transcript limit.");
            }
        }
        TerminalSelectionRange? selection = alternate ? new TerminalSelectionRange(0,0,screen.Columns-1,screen.ViewportRows-1) : null;
        if (!state.Processor.TryExportSnapshot(TerminalSnapshotExportFormat.PlainText,
            new(Unwrap:true, TrimTrailingWhitespace:false, Selection:selection), out var text) || text.Length > maxChars)
            throw new InvalidDataException("Unable to export this retained terminal buffer.");
        return text;
    }
}
