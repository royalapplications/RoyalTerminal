// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;

namespace RoyalTerminal.ControlCatalog;

internal sealed class VisualColorAndStyleGalleryScenario : ICatalogScenario
{
    public string Title => "Visual color/style gallery";

    public string Description => "Rich 16/256/truecolor swatches, SGR style matrix, and OSC8 hyperlink preview.";

    public bool IncludeInFullSweep => true;

    public CatalogScenarioResult Execute()
    {
        List<string> lines =
        [
            "Visual checklist: verify smooth gradients, readable style samples, and clickable hyperlink text.",
            string.Empty,
            "16-color palette:",
            Build16ColorSwatchLine(start: 0, count: 8),
            Build16ColorSwatchLine(start: 8, count: 8),
            string.Empty,
            "256-color ramps:",
            Build256ColorRamp(start: 16, count: 36),
            Build256ColorRamp(start: 52, count: 36),
            "256 grayscale ramp:",
            Build256ColorRamp(start: 232, count: 24),
            string.Empty,
            "Truecolor gradient:",
            BuildTrueColorGradient(width: 48),
            string.Empty,
            "Styles: " +
            "\x1b[1mBold\x1b[0m  " +
            "\x1b[3mItalic\x1b[0m  " +
            "\x1b[4mUnderline\x1b[0m  " +
            "\x1b[9mStrike\x1b[0m  " +
            "\x1b[53mOverline\x1b[0m  " +
            "\x1b[7mInverse\x1b[0m  " +
            "\x1b[2mDim\x1b[0m",
            "Hyperlink: \x1b]8;;https://github.com/wieslawsoltes/RoyalTerminal\x1b\\Open RoyalTerminal project\x1b]8;;\x1b\\",
            string.Empty,
            "Expected result: no broken glyphs, no color banding artifacts, and style spans reset correctly.",
        ];

        return new CatalogScenarioResult(Title, true, lines);
    }

    private static string Build16ColorSwatchLine(int start, int count)
    {
        StringBuilder builder = new();
        for (int i = 0; i < count; i++)
        {
            int color = start + i;
            builder.Append("\x1b[48;5;").Append(color).Append("m  \x1b[0m");
        }

        return builder.ToString();
    }

    private static string Build256ColorRamp(int start, int count)
    {
        StringBuilder builder = new();
        for (int i = 0; i < count; i++)
        {
            int color = start + i;
            builder.Append("\x1b[48;5;").Append(color).Append("m \x1b[0m");
        }

        return builder.ToString();
    }

    private static string BuildTrueColorGradient(int width)
    {
        StringBuilder builder = new(capacity: width * 20);
        for (int i = 0; i < width; i++)
        {
            int r = (int)Math.Round((i / (double)Math.Max(1, width - 1)) * 255d);
            int g = 255 - r;
            int b = (int)Math.Round((Math.Sin(i / 6d) * 0.5d + 0.5d) * 255d);
            builder.Append("\x1b[48;2;").Append(r).Append(';').Append(g).Append(';').Append(b).Append("m \x1b[0m");
        }

        return builder.ToString();
    }
}

internal sealed class VisualUnicodeAndSpriteGalleryScenario : ICatalogScenario
{
    public string Title => "Visual Unicode/sprite gallery";

    public string Description => "Line drawing, braille, block shades, scan lines, wide glyphs, emoji, and combining marks.";

    public bool IncludeInFullSweep => true;

    public CatalogScenarioResult Execute()
    {
        List<string> lines =
        [
            "Visual checklist: verify alignment, wide-cell spacing, grapheme composition, and sprite-like symbols.",
            string.Empty,
            "Box drawing grid:",
            "┌──────────┬──────────┬──────────┐",
            "│ ├─┼─┤    │ ╔═╦═╗    │ ╭─╮╱╲    │",
            "│ └─┴─┘    │ ╚═╩═╝    │ ╰─╯╲╱    │",
            "└──────────┴──────────┴──────────┘",
            string.Empty,
            "Braille patterns: ⠁ ⠉ ⠛ ⠿  ⣀ ⣤ ⣶ ⣿",
            "Block shades   : ░ ▒ ▓ █ ▀ ▄ ▌ ▐ ▉",
            "Scan lines     : ⎺ ⎻ ⎼ ⎽ ⎾ ⎿",
            "Wide CJK       : 中 文 測 試 端 末",
            "Emoji/ZWJ      : 👨‍👩‍👧‍👦  👩‍💻  🧪  🛰️  🇨🇦",
            "Combining      : é  ö  ñ  Å  Ż",
            "Keycap         : #️⃣  *️⃣  1️⃣  9️⃣",
            string.Empty,
            "Expected result: no orphan spacer cells, no clipped glyph halves, and proper grapheme grouping.",
        ];

        return new CatalogScenarioResult(Title, true, lines);
    }
}

internal sealed class VisualHyperlinkAndInlineGalleryScenario : ICatalogScenario
{
    public string Title => "Visual hyperlink/inline gallery";

    public string Description => "OSC8 hyperlink matrix plus related inline link-style behavior checks.";

    public bool IncludeInFullSweep => true;

    public CatalogScenarioResult Execute()
    {
        List<string> lines =
        [
            "Visual checklist: confirm links are clickable, styling is preserved, and plain text remains unlinked.",
            string.Empty,
            "HTTP links:",
            "  \x1b]8;;https://github.com/wieslawsoltes/RoyalTerminal\x1b\\RoyalTerminal GitHub\x1b]8;;\x1b\\  | plain trailing text",
            "  \x1b]8;;https://example.com/docs/vt?mode=interactive#hyperlinks\x1b\\Long docs hyperlink sample\x1b]8;;\x1b\\",
            string.Empty,
            "Mail/File links:",
            "  \x1b]8;;mailto:support@example.com\x1b\\support@example.com\x1b]8;;\x1b\\",
            "  \x1b]8;;file:///tmp/rt-catalog-demo.txt\x1b\\file:///tmp/rt-catalog-demo.txt\x1b]8;;\x1b\\",
            string.Empty,
            "Styled links:",
            "  \x1b[4;38;5;45m\x1b]8;;https://example.com/blue\x1b\\Blue underlined link\x1b]8;;\x1b\\\x1b[0m  normal text",
            "  \x1b[1;3m\x1b]8;;https://example.com/bold-italic\x1b\\Bold/italic link\x1b]8;;\x1b\\\x1b[0m  normal text",
            string.Empty,
            "Repeated target spans:",
            "  \x1b]8;;https://example.com/split\x1b\\Part-A\x1b]8;;\x1b\\ + \x1b]8;;https://example.com/split\x1b\\Part-B\x1b]8;;\x1b\\",
            string.Empty,
            "Expected result: each OSC8 span is independently clickable and no link metadata leaks to adjacent text.",
        ];

        return new CatalogScenarioResult(Title, true, lines);
    }
}

internal sealed class VisualAttributeExtensionGalleryScenario : ICatalogScenario
{
    public string Title => "Visual SGR extension gallery";

    public string Description => "Underline variants, underline colors, and extended text attributes with fallback expectations.";

    public bool IncludeInFullSweep => true;

    public CatalogScenarioResult Execute()
    {
        List<string> lines =
        [
            "Visual checklist: confirm attribute fallback behavior and clean resets between samples.",
            string.Empty,
            "Underline styles: " +
            "\x1b[4:1mSingle\x1b[0m  " +
            "\x1b[4:2mDouble\x1b[0m  " +
            "\x1b[4:3mCurly\x1b[0m  " +
            "\x1b[4:4mDotted\x1b[0m  " +
            "\x1b[4:5mDashed\x1b[0m",
            "Underline colors: " +
            "\x1b[58:2::255:128:0m\x1b[4mOrange\x1b[0m  " +
            "\x1b[58:2::90:180:255m\x1b[4mBlue\x1b[0m  " +
            "\x1b[58:5::46m\x1b[4mIndexed\x1b[0m",
            "Extended attrs  : " +
            "\x1b[51mFramed\x1b[0m  " +
            "\x1b[52mEncircled\x1b[0m  " +
            "\x1b[8mConcealed\x1b[0m/\x1b[28mReveal\x1b[0m  " +
            "\x1b[5mBlink\x1b[0m (optional)",
            string.Empty,
            "Expected result: unsupported attributes should degrade gracefully without leaking styles to following text.",
        ];

        return new CatalogScenarioResult(Title, true, lines);
    }
}

internal sealed class VisualDecSpecialGraphicsScenario : ICatalogScenario
{
    public string Title => "Visual DEC special graphics";

    public string Description => "DEC line-drawing character-set rendering and fallback comparison with Unicode box drawing.";

    public bool IncludeInFullSweep => true;

    public CatalogScenarioResult Execute()
    {
        List<string> lines =
        [
            "Visual checklist: verify DEC special graphics map to proper line-drawing glyphs, not literal letters.",
            string.Empty,
            "DEC set sample (ESC ( 0):",
            "\x1b(0lqqqqwqqqqk\x1b(B",
            "\x1b(0x    x    x\x1b(B",
            "\x1b(0tqqqqnqqqqu\x1b(B",
            "\x1b(0x    x    x\x1b(B",
            "\x1b(0mqqqqvqqqqj\x1b(B",
            string.Empty,
            "Unicode reference:",
            "┌────┬────┐",
            "│    │    │",
            "├────┼────┤",
            "│    │    │",
            "└────┴────┘",
            string.Empty,
            "Expected result: DEC and Unicode forms should both display aligned box borders.",
        ];

        return new CatalogScenarioResult(Title, true, lines);
    }
}

internal sealed class VisualWrappingAndTabStopsScenario : ICatalogScenario
{
    public string Title => "Visual wrapping/tab-stop stress";

    public string Description => "Tab alignment, long-line wrapping, and wide-grapheme boundary stress for manual inspection.";

    public bool IncludeInFullSweep => true;

    public CatalogScenarioResult Execute()
    {
        string wrapStress = BuildAlternatingWrapLine(width: 160);
        string wideBoundary = $"A{new string('A', 27)}中{new string('B', 27)}👩‍💻{new string('C', 27)}語{new string('D', 27)}";

        List<string> lines =
        [
            "Visual checklist: inspect default tabs, soft-wrap continuity, and width accounting for wide/ZWJ graphemes.",
            string.Empty,
            "Tab stop probe (default tab width=8 expected):",
            "col0\tcol8\tcol16\tcol24\tcol32\tcol40",
            "1\t2\t3\t4\t5\t6",
            string.Empty,
            "Wrap ruler (10-cell markers):",
            "0000000000111111111122222222223333333333444444444455555555556666666666777777777788888888889999999999",
            "Wrap stress line:",
            wrapStress,
            string.Empty,
            "Wide boundary stress:",
            wideBoundary,
            string.Empty,
            "Expected result: tabs snap consistently, wraps are clean, and wide glyphs do not shift following cells.",
        ];

        return new CatalogScenarioResult(Title, true, lines);
    }

    private static string BuildAlternatingWrapLine(int width)
    {
        StringBuilder builder = new(capacity: width * 16);
        for (int i = 0; i < width; i++)
        {
            int background = (i / 5) % 2 == 0 ? 24 : 60;
            builder.Append("\x1b[48;5;")
                .Append(background)
                .Append(";38;5;255m")
                .Append(i % 10)
                .Append("\x1b[0m");
        }

        return builder.ToString();
    }
}

internal sealed class VisualVtEditingMechanicsScenario : ICatalogScenario
{
    public string Title => "Visual VT edit/margin mechanics";

    public string Description => "TUI script gallery for DECSTBM/DECOM, IRM/DCH/ECH, REP, tabs, save/restore, and alt-screen restore.";

    public bool IncludeInFullSweep => true;

    public CatalogScenarioResult Execute()
    {
        List<string> lines =
        [
            "Visual checklist: execute these scripts in terminal control and verify row snapshots and cursor behavior.",
            string.Empty,
        ];

        List<TuiSequenceCheck> checks =
        [
            new("Scroll region (DECSTBM)", "\\x1b[4;7r AAAA/BBBB/CCCC/DDDD + LF", "region should scroll only inside margins"),
            new("Origin mode (DECOM)", "\\x1b[9;12r\\x1b[?6h\\x1b[2;3HOM", "OM should land relative to top margin"),
            new("Insert mode (IRM)", "\\x1b[4h insert Z into ABCDE", "ABZCDE expected"),
            new("Delete+erase chars", "DCH then ECH on 12345", "134 + cleared tail"),
            new("Repeat char (REP)", "# then CSI 5 b", "###### expected"),
            new("Tab traversal (CBT/CHT)", "CSI 2 Z + CSI 2 I", "cursor should align to tab stops"),
            new("Cursor save/restore", "ESC 7 ... ESC 8", "cursor should restore exact position"),
            new("Alt-screen restore", "CSI ?1049h ... CSI ?1049l", "main buffer content should reappear"),
        ];

        TuiRuntimeHelpers.AppendChecks(lines, "Edit/margin script matrix:", checks);
        lines.Add(string.Empty);
        lines.Add("Snapshot reference:");
        lines.Add("  01|VT-EDIT-LAB                           |");
        lines.Add("  02|ABZCDE                                |");
        lines.Add("  03|134                                   |");
        lines.Add("  04|BBBB                                  |");
        lines.Add("  05|CCCC                                  |");
        lines.Add("  06|DDDD                                  |");
        lines.Add("  08|######  <               >             |");
        lines.Add("  10|  OM               @                  |");
        lines.Add("  12|MAIN-BUFFER-OK                        |");
        lines.Add(string.Empty);
        lines.Add("Expected result: script outcomes should match the snapshot model with no cursor drift artifacts.");

        return new CatalogScenarioResult(Title, true, lines);
    }
}

internal sealed class VisualOscThemeMutationScenario : ICatalogScenario
{
    public string Title => "Visual OSC theme/palette mutation";

    public string Description => "OSC 4/10/11/12 set+query scripts with visible color swatches for host-side verification.";

    public bool IncludeInFullSweep => true;

    public CatalogScenarioResult Execute()
    {
        uint paletteBefore = 0xFFCD0000;
        uint defaultFgBefore = 0xFFD4D4D4;
        uint defaultBgBefore = 0xFF1E1E1E;
        uint cursorBefore = 0xFFD4D4D4;

        uint paletteAfter = 0xFF44AAFF;
        uint defaultFgAfter = 0xFFF0F000;
        uint defaultBgAfter = 0xFF101020;
        uint cursorAfter = 0xFF00FF88;

        List<string> lines =
        [
            "Visual checklist: run OSC mutation scripts and verify query payloads and color remap behavior.",
            string.Empty,
            "Baseline samples:",
            $"  Palette[1] before: {TuiRuntimeHelpers.BuildColorSwatch(paletteBefore)} 0x{paletteBefore:X8}",
            $"  DefaultFG before : {TuiRuntimeHelpers.BuildColorSwatch(defaultFgBefore)} 0x{defaultFgBefore:X8}",
            $"  DefaultBG before : {TuiRuntimeHelpers.BuildColorSwatch(defaultBgBefore)} 0x{defaultBgBefore:X8}",
            $"  Cursor before    : {TuiRuntimeHelpers.BuildColorSwatch(cursorBefore)} 0x{cursorBefore:X8}",
            string.Empty,
        ];

        List<TuiSequenceCheck> checks =
        [
            new("OSC 4 query", "\\x1b]4;1;?\\x1b\\", "expect rgb payload for palette slot 1"),
            new("OSC 10 query", "\\x1b]10;?\\x1b\\", "expect rgb payload for default fg"),
            new("OSC 11 query", "\\x1b]11;?\\x1b\\", "expect rgb payload for default bg"),
            new("OSC 12 query", "\\x1b]12;?\\x1b\\", "expect rgb payload for cursor"),
            new("OSC 4 set", "\\x1b]4;1;#44AAFF\\x1b\\", "palette index 1 should update"),
            new("OSC 10 set", "\\x1b]10;#F0F000\\x1b\\", "default foreground should update"),
            new("OSC 11 set", "\\x1b]11;rgb:1010/2020/3030\\x1b\\", "default background should update"),
            new("OSC 12 set", "\\x1b]12;0x00FF88\\x1b\\", "cursor color should update"),
        ];

        TuiRuntimeHelpers.AppendChecks(lines, "OSC mutation script matrix:", checks);
        lines.Add(string.Empty);
        lines.Add("Post-mutation samples:");
        lines.Add($"  Palette[1] after : {TuiRuntimeHelpers.BuildColorSwatch(paletteAfter)} 0x{paletteAfter:X8}");
        lines.Add($"  DefaultFG after  : {TuiRuntimeHelpers.BuildColorSwatch(defaultFgAfter)} 0x{defaultFgAfter:X8}");
        lines.Add($"  DefaultBG after  : {TuiRuntimeHelpers.BuildColorSwatch(defaultBgAfter)} 0x{defaultBgAfter:X8}");
        lines.Add($"  Cursor after     : {TuiRuntimeHelpers.BuildColorSwatch(cursorAfter)} 0x{cursorAfter:X8}");
        lines.Add(string.Empty);
        lines.Add("Expected result: query replies and swatches should reflect updated palette/default/cursor colors.");

        return new CatalogScenarioResult(Title, true, lines);
    }
}

internal sealed class VisualSpriteFrameGalleryScenario : ICatalogScenario
{
    private static readonly int[] Palette = [39, 45, 81, 220, 214, 196, 27, 250];

    public string Title => "Visual sprite frame gallery";

    public string Description => "Pseudo-sprite frame rendering with transparency checkerboard and dense color blocks.";

    public bool IncludeInFullSweep => true;

    public CatalogScenarioResult Execute()
    {
        int[][] frameA =
        [
            [-1, -1, 2, 2, 2, 2, -1, -1],
            [-1, 2, 1, 1, 1, 1, 2, -1],
            [2, 1, 3, 3, 3, 3, 1, 2],
            [2, 1, 3, 5, 5, 3, 1, 2],
            [2, 1, 3, 4, 4, 3, 1, 2],
            [2, 1, 3, 3, 3, 3, 1, 2],
            [-1, 2, 6, -1, -1, 6, 2, -1],
            [-1, -1, 6, -1, -1, 6, -1, -1],
        ];

        int[][] frameB =
        [
            [-1, -1, 2, 2, 2, 2, -1, -1],
            [-1, 2, 1, 1, 1, 1, 2, -1],
            [2, 1, 3, 3, 3, 3, 1, 2],
            [2, 1, 3, 5, 5, 3, 1, 2],
            [2, 1, 3, 4, 4, 3, 1, 2],
            [2, 1, 3, 3, 3, 3, 1, 2],
            [-1, 2, 6, 7, 7, 6, 2, -1],
            [-1, -1, 7, 7, 7, 7, -1, -1],
        ];

        List<string> lines =
        [
            "Visual checklist: confirm sprite edges, checkerboard transparency fallback, and color-block stability.",
            string.Empty,
            "Frame A:",
        ];

        for (int row = 0; row < frameA.Length; row++)
        {
            lines.Add(RenderSpriteRow(frameA[row], row));
        }

        lines.Add(string.Empty);
        lines.Add("Frame B (thruster on):");
        for (int row = 0; row < frameB.Length; row++)
        {
            lines.Add(RenderSpriteRow(frameB[row], row));
        }

        lines.Add(string.Empty);
        lines.Add("Expected result: transparent cells keep checkerboard pattern and sprite silhouette remains crisp.");
        return new CatalogScenarioResult(Title, true, lines);
    }

    private static string RenderSpriteRow(IReadOnlyList<int> row, int rowIndex)
    {
        StringBuilder builder = new(capacity: row.Count * 20);
        for (int col = 0; col < row.Count; col++)
        {
            int colorIndex = row[col];
            int color = colorIndex >= 0
                ? Palette[Math.Min(colorIndex, Palette.Length - 1)]
                : ((rowIndex + col) % 2 == 0 ? 236 : 239);
            builder.Append("\x1b[48;5;").Append(color).Append("m  \x1b[0m");
        }

        return builder.ToString();
    }
}

internal sealed class VisualTuiLayoutGalleryScenario : ICatalogScenario
{
    private const int LeftPaneWidth = 35;
    private const int RightPaneWidth = 36;
    private const int UnifiedPanelWidth = LeftPaneWidth + RightPaneWidth + 1;

    public string Title => "Visual TUI layout gallery";

    public string Description => "Realistic mc/btop-style mock screens for manual rendering inspection.";

    public bool IncludeInFullSweep => true;

    public CatalogScenarioResult Execute()
    {
        List<string> lines =
        [
            "Visual checklist: inspect pane borders, status bars, graphs, and dense data readability.",
            string.Empty,
            "\x1b[1;34mMC-style dual panel\x1b[0m",
            BuildDualPaneTop("/home/user/project", "/var/log"),
            BuildDualPaneRow("\x1b[38;5;82m> src/\x1b[0m", "auth.log"),
            BuildDualPaneRow("  tests/", "kernel.log"),
            BuildDualPaneRow("  samples/", "app.log"),
            BuildDualPaneRow("  README.md", "backup.log"),
            BuildDualPaneDivider(),
            BuildUnifiedPanelRow("F3 View  F4 Edit  F5 Copy  F6 Move  F7 Mkdir  F8 Delete  F10 Quit"),
            BuildUnifiedPanelBottom(),
            string.Empty,
            "\x1b[1;36mbtop-style metrics\x1b[0m",
            BuildUnifiedPanelTop(),
            BuildUnifiedPanelRow("CPU  \x1b[38;5;82m█████████████████\x1b[38;5;240m███\x1b[0m 89%   NET \x1b[38;5;39m⬆ 2.4MB/s  ⬇ 12.8MB/s\x1b[0m"),
            BuildUnifiedPanelRow("MEM  \x1b[38;5;45m███████████\x1b[38;5;240m███████\x1b[0m 58%   DISK \x1b[38;5;220m██████████\x1b[38;5;240m████\x1b[0m 71%"),
            BuildUnifiedPanelRow("PROC \x1b[38;5;214mnginx 9.2%\x1b[0m  \x1b[38;5;208mdotnet 14.7%\x1b[0m  \x1b[38;5;199msshd 1.2%\x1b[0m  uptime 12d 04:21"),
            BuildUnifiedPanelBottom(),
            string.Empty,
            "Expected result: borders align, bars are smooth, and colored labels remain legible.",
        ];

        return new CatalogScenarioResult(Title, true, lines);
    }

    private static string BuildDualPaneTop(string leftTitle, string rightTitle)
    {
        return $"┌{BuildTitledPane(leftTitle, LeftPaneWidth)}┬{BuildTitledPane(rightTitle, RightPaneWidth)}┐";
    }

    private static string BuildDualPaneDivider()
    {
        return $"├{new string('─', LeftPaneWidth)}┴{new string('─', RightPaneWidth)}┤";
    }

    private static string BuildDualPaneRow(string leftContent, string rightContent)
    {
        return $"│{PadVisual($" {leftContent}", LeftPaneWidth)}│{PadVisual($" {rightContent}", RightPaneWidth)}│";
    }

    private static string BuildUnifiedPanelTop()
    {
        return $"┌{new string('─', UnifiedPanelWidth)}┐";
    }

    private static string BuildUnifiedPanelBottom()
    {
        return $"└{new string('─', UnifiedPanelWidth)}┘";
    }

    private static string BuildUnifiedPanelRow(string content)
    {
        return $"│{PadVisual($" {content}", UnifiedPanelWidth)}│";
    }

    private static string BuildTitledPane(string title, int width)
    {
        string prefix = $"─ {title} ";
        int filler = width - GetVisibleWidth(prefix);
        return filler > 0
            ? prefix + new string('─', filler)
            : prefix;
    }

    private static string PadVisual(string text, int width)
    {
        int visibleWidth = GetVisibleWidth(text);
        return visibleWidth < width
            ? text + new string(' ', width - visibleWidth)
            : text;
    }

    private static int GetVisibleWidth(string text)
    {
        int width = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\x1b')
            {
                i = SkipAnsiSequence(text, i);
                continue;
            }

            width++;
        }

        return width;
    }

    private static int SkipAnsiSequence(string text, int startIndex)
    {
        if (startIndex + 1 >= text.Length)
        {
            return startIndex;
        }

        if (text[startIndex + 1] == '[')
        {
            int index = startIndex + 2;
            while (index < text.Length)
            {
                char ch = text[index];
                if (ch >= '@' && ch <= '~')
                {
                    return index;
                }

                index++;
            }

            return text.Length - 1;
        }

        return startIndex + 1;
    }
}

internal sealed class VisualVtStateDashboardScenario : ICatalogScenario
{
    public string Title => "Visual VT state dashboard";

    public string Description => "TUI capability board with script snippets for validating host VT handling.";

    public bool IncludeInFullSweep => true;

    public CatalogScenarioResult Execute()
    {
        string term = Environment.GetEnvironmentVariable("TERM") ?? "<unset>";
        bool hasInteractiveInput = !Console.IsInputRedirected;
        bool hasInteractiveOutput = !Console.IsOutputRedirected;
        bool ansiLikely = hasInteractiveOutput || term.Contains("xterm", StringComparison.OrdinalIgnoreCase);
        bool hyperlinkLikely = term.Contains("xterm", StringComparison.OrdinalIgnoreCase) ||
                               term.Contains("kitty", StringComparison.OrdinalIgnoreCase) ||
                               term.Contains("wezterm", StringComparison.OrdinalIgnoreCase);

        List<string> lines =
        [
            $"\x1b[1;37m[TUI host]\x1b[0m TERM={term}",
            "Feature board (green=ok, red=fail):",
            $"  ANSI output path: {TuiRuntimeHelpers.Badge(ansiLikely)}",
            $"  Input/query path: {TuiRuntimeHelpers.Badge(hasInteractiveInput)}",
            $"  Hyperlink support likely: {TuiRuntimeHelpers.Badge(hyperlinkLikely)}",
            $"  OSC color mutation scripts: {TuiRuntimeHelpers.Badge(true)}",
            $"  DEC mode script coverage: {TuiRuntimeHelpers.Badge(true)}",
            $"  Mouse/keyboard/window script coverage: {TuiRuntimeHelpers.Badge(true)}",
            string.Empty,
            "Script quick checks:",
            $"  Alt-screen toggle: {ControlTextFormatter.FormatControl("\\x1b[?1049h ... \\x1b[?1049l")}",
            $"  Bracketed paste: {ControlTextFormatter.FormatControl("\\x1b[?2004h ... \\x1b[?2004l")}",
            $"  Focus events: {ControlTextFormatter.FormatControl("\\x1b[?1004h ... \\x1b[?1004l")}",
            $"  Cursor report: {ControlTextFormatter.FormatControl("\\x1b[6n => \\x1b[<r>;<c>R")}",
            $"  Window report: {ControlTextFormatter.FormatControl("\\x1b[18t => \\x1b[8;<rows>;<cols>t")}",
            string.Empty,
            "Expected result: board should be mostly green in interactive terminal hosts and script snippets should render as valid control-text.",
        ];

        return new CatalogScenarioResult(Title, true, lines);
    }
}

internal sealed class InteractiveInputWindowBoardScenario : ICatalogScenario
{
    public string Title => "Interactive input/window board";

    public string Description => "Rendered board for interactive mouse, cursor, keyboard, and window protocol checks.";

    public bool IncludeInFullSweep => true;

    public CatalogScenarioResult Execute()
    {
        CatalogScenarioResult baseResult = new InteractiveInputWindowCatalogScenario().Execute();
        List<string> lines =
        [
            "Visual checklist: verify interactive input/window checks are green and response payloads are coherent.",
            string.Empty,
        ];

        for (int i = 0; i < baseResult.Lines.Count; i++)
        {
            string line = baseResult.Lines[i];
            if (line.StartsWith("[", StringComparison.Ordinal) && !line.StartsWith("[info]", StringComparison.Ordinal))
            {
                lines.Add($"\x1b[1;37m{line}\x1b[0m");
                continue;
            }

            lines.Add(FormatStatusLine(line));
        }

        lines.Add(string.Empty);
        lines.Add("Expected result: badges should be mostly green and sequence snapshots should match enabled interaction modes.");
        return new CatalogScenarioResult(Title, baseResult.Success, lines);
    }

    private static string FormatStatusLine(string line)
    {
        return line
            .Replace(": ok", $": {TuiRuntimeHelpers.Badge(ok: true)}", StringComparison.Ordinal)
            .Replace(": fail", $": {TuiRuntimeHelpers.Badge(ok: false)}", StringComparison.Ordinal);
    }
}

internal sealed class VisualPtyTranscriptScenario : ICatalogScenario
{
    private const string TokenDone = "__RT_VISUAL_TRANSCRIPT_DONE__";

    public string Title => "Visual PTY transcript viewer";

    public string Description => "Runs a realistic shell transcript workload in TUI mode and previews rendered output.";

    public bool IncludeInFullSweep => true;

    public CatalogScenarioResult Execute()
    {
        List<string> lines = [];

        bool ran = TuiRuntimeHelpers.TryRunShellCommand(
            BuildWorkloadCommand(),
            TimeSpan.FromSeconds(8),
            out string output,
            out string error,
            out int exitCode);

        bool tokenObserved = ran && output.Contains(TokenDone, StringComparison.Ordinal);
        bool exitObserved = ran;
        bool success = tokenObserved && exitObserved && exitCode == 0;

        lines.Add("Visual checklist: inspect transcript colors, block bars, and log readability.");
        lines.Add($"PTY status: {(success ? TuiRuntimeHelpers.Badge(true) : TuiRuntimeHelpers.Badge(false))} token={tokenObserved} exit={exitObserved} code={exitCode}");
        lines.Add(string.Empty);
        lines.Add("\x1b[1mTranscript preview:\x1b[0m");

        IReadOnlyList<string> previewLines = ExtractPreviewLines(output);
        if (previewLines.Count == 0)
        {
            lines.Add("  <no transcript captured>");
        }
        else
        {
            for (int i = 0; i < previewLines.Count; i++)
            {
                lines.Add($"  {previewLines[i]}");
            }
        }

        if (!string.IsNullOrWhiteSpace(error))
        {
            lines.Add($"stderr: {ControlTextFormatter.FormatControl(error)}");
        }

        lines.Add(string.Empty);
        lines.Add("Expected result: colored status labels and bars should appear without corrupted control sequences.");
        return new CatalogScenarioResult(Title, success, lines);
    }

    private static string BuildWorkloadCommand()
    {
        if (OperatingSystem.IsWindows())
        {
            return
                "$esc=[char]27; " +
                "Write-Output \"$esc[1;36m[session]$esc[0m starting workload\"; " +
                "Write-Output \"$esc[32m[ok]$esc[0m connected to endpoint\"; " +
                "Write-Output \"cpu  : $esc[38;5;82m█████████████$esc[38;5;240m███$esc[0m 89%\"; " +
                "Write-Output \"mem  : $esc[38;5;45m████████$esc[38;5;240m████$esc[0m 58%\"; " +
                "Write-Output \"disk : $esc[38;5;220m██████████$esc[38;5;240m████$esc[0m 71%\"; " +
                "Write-Output \"$esc[33mwarning$esc[0m latency spike on pty lane\"; " +
                "Write-Output \"$esc[32mok$esc[0m recovered\"; " +
                $"Write-Output '{TokenDone}'";
        }

        return
            "printf '\\033[1;36m[session]\\033[0m starting workload\\n'; " +
            "printf '\\033[32m[ok]\\033[0m connected to endpoint\\n'; " +
            "printf 'cpu  : \\033[38;5;82m█████████████\\033[38;5;240m███\\033[0m 89%%\\n'; " +
            "printf 'mem  : \\033[38;5;45m████████\\033[38;5;240m████\\033[0m 58%%\\n'; " +
            "printf 'disk : \\033[38;5;220m██████████\\033[38;5;240m████\\033[0m 71%%\\n'; " +
            "printf '\\033[33mwarning\\033[0m latency spike on pty lane\\n'; " +
            "printf '\\033[32mok\\033[0m recovered\\n'; " +
            $"printf '{TokenDone}\\n'";
    }

    private static IReadOnlyList<string> ExtractPreviewLines(string output)
    {
        string[] rawLines = output
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        List<string> filtered = [];
        HashSet<string> unique = new(StringComparer.Ordinal);
        for (int i = 0; i < rawLines.Length; i++)
        {
            string line = rawLines[i];
            if (line.Contains(TokenDone, StringComparison.Ordinal))
            {
                continue;
            }

            string plain = StripAnsi(line);
            bool interesting = plain.StartsWith("[session]", StringComparison.Ordinal) ||
                               plain.StartsWith("[ok]", StringComparison.Ordinal) ||
                               plain.StartsWith("cpu  :", StringComparison.Ordinal) ||
                               plain.StartsWith("mem  :", StringComparison.Ordinal) ||
                               plain.StartsWith("disk :", StringComparison.Ordinal) ||
                               plain.StartsWith("warning", StringComparison.Ordinal) ||
                               plain.StartsWith("ok", StringComparison.Ordinal);

            if (!interesting || !unique.Add(plain))
            {
                continue;
            }

            filtered.Add(line);
        }

        return filtered;
    }

    private static string StripAnsi(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        StringBuilder builder = new(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != '\x1b')
            {
                builder.Append(text[i]);
                continue;
            }

            i = SkipAnsiSequence(text, i);
        }

        return builder.ToString();
    }

    private static int SkipAnsiSequence(string text, int startIndex)
    {
        if (startIndex + 1 >= text.Length)
        {
            return startIndex;
        }

        char kind = text[startIndex + 1];
        if (kind == '[')
        {
            int index = startIndex + 2;
            while (index < text.Length)
            {
                char ch = text[index];
                if (ch >= '@' && ch <= '~')
                {
                    return index;
                }

                index++;
            }

            return text.Length - 1;
        }

        if (kind == ']')
        {
            int index = startIndex + 2;
            while (index < text.Length)
            {
                if (text[index] == '\a')
                {
                    return index;
                }

                if (text[index] == '\x1b' && index + 1 < text.Length && text[index + 1] == '\\')
                {
                    return index + 1;
                }

                index++;
            }

            return text.Length - 1;
        }

        return startIndex + 1;
    }
}
