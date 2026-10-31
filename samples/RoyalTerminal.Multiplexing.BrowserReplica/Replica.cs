// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using RoyalTerminal.Terminal;
using RoyalTerminal.Avalonia.Rendering;

namespace RoyalTerminal.BrowserReplica;

// Invoked on one dedicated worker. No credentials, clipboard, host effects, writable transport or network callbacks.
[SupportedOSPlatform("browser")]
public static partial class Replica
{
    private const int MaximumBytes = 8 << 20;
    private static readonly Dictionary<int, ManagedTerminalSnapshot> States = [];
    private static int _next;
    public static void Main() { }

    [JSExport]
    public static int Open([JSMarshalAs<JSType.Array<JSType.Number>>] byte[] bytes)
    {
        if (bytes.Length is < 1 or > MaximumBytes || States.Count >= 32) throw new InvalidDataException("Replica admission limit exceeded.");
        int id = checked(++_next);
        ManagedTerminalSnapshot restored = ManagedTerminalSnapshot.Restore(bytes, new()
        {
            DecodeLimits = new(MaximumPayloadBytes: 4 << 20, MaximumTotalPayloadBytes: MaximumBytes, MaximumCells: 1 << 20,
                MaximumPages: 4096, MaximumSuffixCodepointsPerPage: 256 << 10, MaximumStringBytesPerRecord: 256 << 10, MaximumContinuationBytes: 64 << 10),
            ScrollbackLimit = 2000, ScrollbackQuota = new() { MaximumBytes = 4 << 20 },
            ProcessorOptions = new() { GlyphProtocolEnabled = false, SixelGraphicsEnabled = false, ClipboardWriteLimitBytes = 0,
                KittyGraphicsMaxApcBytes = 0, KittyGraphicsMaxImageBytes = 0, KittyGraphicsStorageLimitBytes = 0, UnknownSequenceMaxBytes = 0 },
        });
        try { CheckSize(restored.Screen.Columns, restored.Screen.ViewportRows); States.Add(id, restored); return id; }
        catch { restored.Dispose(); throw; }
    }
    [JSExport] public static void Close(int id) { if (States.Remove(id, out var r)) r.Dispose(); }
    [JSExport] public static int Columns(int id) => States[id].Screen.Columns;
    [JSExport] public static int Rows(int id) => States[id].Screen.ViewportRows;
    [JSExport] public static void Write(int id, string base64)
    {
        if (base64.Length > 128 * 1024) throw new InvalidDataException("Oversized output chunk.");
        States[id].Processor.Process(Convert.FromBase64String(base64));
    }
    [JSExport] public static void Resize(int id, int columns, int rows, int width, int height)
    {
        CheckSize(columns, rows);
        if (width < 0 || height < 0) throw new InvalidDataException("Invalid pixel size.");
        States[id].Processor.ResizeScreen(columns, rows, width, height);
    }
    private static void CheckSize(int columns, int rows)
    { if (columns is < 1 or > 512 || rows is < 1 or > 512) throw new InvalidDataException("Invalid replica geometry."); }

    [JSExport] public static string Paint(int id)
    {
        var r = States[id]; r.Processor.RefreshTimedState();
        // Preflight expansion of repeated untrusted hyperlink strings and graphemes before making the formatted string.
        long variableCharacters = 0;
        for (int y = 0; y < r.Screen.ViewportRows; ++y)
            foreach (ref readonly TerminalCell cell in r.Screen.GetViewportRow(y).ReadOnlyCells)
            {
                variableCharacters += cell.Grapheme?.Length ?? 0;
                if (cell.HyperlinkId != 0 && r.Screen.TryGetHyperlinkUrl(cell.HyperlinkId, out string? link)) variableCharacters += link?.Length ?? 0;
                if (variableCharacters > 1 << 20) throw new InvalidDataException("Viewport text exceeds its render budget.");
            }
        var selection = new TerminalSelectionRange(0, 0, r.Screen.Columns - 1, r.Screen.ViewportRows - 1);
        var extras = new TerminalSnapshotExportExtras(IncludeCursor:true, IncludeStyle:true, IncludeModes:true,
            IncludeKittyKeyboard:true, IncludeScrollingRegion:true, IncludeKeyboardModes:true, IncludePalette:true);
        if (!r.Processor.TryExportSnapshot(TerminalSnapshotExportFormat.StyledVt, new(Selection: selection, Extras: extras), out string vt) || vt.Length > 4 << 20)
            throw new InvalidDataException("The viewport exceeds its presentation budget.");
        return vt;
    }
}
