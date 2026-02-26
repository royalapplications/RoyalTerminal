// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Terminal;

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

    public string Description => "Live mode/query status board with color badges for user verification.";

    public bool IncludeInFullSweep => true;

    public CatalogScenarioResult Execute()
    {
        List<string> lines = [];
        bool success = true;
        List<VtProcessorProbe> probes = VtProbeFactory.CreateProbes(lines);

        try
        {
            for (int i = 0; i < probes.Count; i++)
            {
                VtProcessorProbe probe = probes[i];
                bool requiredForSuccess = string.Equals(probe.Name, CatalogConstants.ManagedProbeName, StringComparison.Ordinal);
                int totalChecks = 0;
                int passedChecks = 0;

                lines.Add($"\x1b[1;37m[{probe.Name}]\x1b[0m");
                lines.Add("Feature board (green=ok, red=fail):");

                totalChecks++;
                bool altToggleOk = VtCatalogHelpers.TryToggleDecMode(probe, 1049, out _, out _, out _, out _);
                if (altToggleOk)
                {
                    passedChecks++;
                }
                lines.Add($"  Alternate screen (1049): {Badge(altToggleOk)}");

                totalChecks++;
                bool pasteToggleOk = VtCatalogHelpers.TryToggleDecMode(probe, 2004, out _, out _, out _, out _);
                if (pasteToggleOk)
                {
                    passedChecks++;
                }
                lines.Add($"  Bracketed paste (2004): {Badge(pasteToggleOk)}");

                totalChecks++;
                bool focusToggleOk = VtCatalogHelpers.TryToggleDecMode(probe, 1004, out _, out _, out _, out _);
                if (focusToggleOk)
                {
                    passedChecks++;
                }
                lines.Add($"  Focus events (1004): {Badge(focusToggleOk)}");

                totalChecks++;
                bool mouseToggleOk =
                    VtCatalogHelpers.TryToggleDecMode(probe, 1000, out _, out _, out _, out _) &&
                    VtCatalogHelpers.TryToggleDecMode(probe, 1002, out _, out _, out _, out _) &&
                    VtCatalogHelpers.TryToggleDecMode(probe, 1006, out _, out _, out _, out _);
                if (mouseToggleOk)
                {
                    passedChecks++;
                }
                lines.Add($"  Mouse modes (1000/1002/1006): {Badge(mouseToggleOk)}");

                totalChecks++;
                bool dsrCursorOk = VtCatalogHelpers.TrySingleResponse(
                    probe,
                    "\x1b[6n",
                    static r => r.StartsWith("\x1b[", StringComparison.Ordinal) && r.EndsWith("R", StringComparison.Ordinal),
                    out string dsrCursorResponse);
                if (dsrCursorOk)
                {
                    passedChecks++;
                }
                lines.Add($"  DSR cursor report: {Badge(dsrCursorOk)} {ControlTextFormatter.FormatControl(dsrCursorResponse)}");

                totalChecks++;
                bool da1Ok = VtCatalogHelpers.TrySingleResponse(
                    probe,
                    "\x1b[c",
                    static r => r.StartsWith("\x1b[?", StringComparison.Ordinal) && r.EndsWith("c", StringComparison.Ordinal),
                    out string da1Response);
                if (da1Ok)
                {
                    passedChecks++;
                }
                lines.Add($"  DA1 device attrs: {Badge(da1Ok)} {ControlTextFormatter.FormatControl(da1Response)}");

                totalChecks++;
                bool kittyQueryOk = VtCatalogHelpers.TrySingleResponse(
                    probe,
                    "\x1b[?u",
                    static r => r.StartsWith("\x1b[?", StringComparison.Ordinal) && r.EndsWith("u", StringComparison.Ordinal),
                    out string kittyResponse);
                if (kittyQueryOk)
                {
                    passedChecks++;
                }
                lines.Add($"  Kitty keyboard query: {Badge(kittyQueryOk)} {ControlTextFormatter.FormatControl(kittyResponse)}");

                bool probeSuccess = passedChecks == totalChecks;
                lines.Add($"  Summary: {passedChecks}/{totalChecks} checks passed.");
                lines.Add(string.Empty);

                if (requiredForSuccess && !probeSuccess)
                {
                    success = false;
                }
            }
        }
        finally
        {
            VtProbeFactory.DisposeProbes(probes);
        }

        return new CatalogScenarioResult(Title, success, lines);
    }

    private static string Badge(bool ok)
    {
        return ok
            ? "\x1b[30;42m OK \x1b[0m"
            : "\x1b[37;41mFAIL\x1b[0m";
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
            .Replace(": ok", $": {Badge(ok: true)}", StringComparison.Ordinal)
            .Replace(": fail", $": {Badge(ok: false)}", StringComparison.Ordinal);
    }

    private static string Badge(bool ok)
    {
        return ok
            ? "\x1b[30;42m OK \x1b[0m"
            : "\x1b[37;41mFAIL\x1b[0m";
    }
}

internal sealed class VisualPtyTranscriptScenario : ICatalogScenario
{
    private const string TokenDone = "__RT_VISUAL_TRANSCRIPT_DONE__";

    public string Title => "Visual PTY transcript viewer";

    public string Description => "Runs a realistic PTY workload and displays a styled transcript preview for manual inspection.";

    public bool IncludeInFullSweep => true;

    public CatalogScenarioResult Execute()
    {
        List<string> lines = [];
        bool success = true;
        string outputSnapshot = string.Empty;

        try
        {
            using IPty pty = new DefaultPtyFactory().Create();
            using ManualResetEventSlim doneSeen = new(initialState: false);
            using ManualResetEventSlim exitSeen = new(initialState: false);
            StringBuilder outputBuffer = new();
            int exitCode = int.MinValue;
            (string shell, IReadOnlyList<string> arguments) workload = BuildWorkloadCommand();

            pty.DataReceived += (buffer, count) =>
            {
                string chunk = Encoding.UTF8.GetString(buffer, 0, count);
                lock (outputBuffer)
                {
                    outputBuffer.Append(chunk);
                    outputSnapshot = outputBuffer.ToString();
                    if (outputSnapshot.Contains(TokenDone, StringComparison.Ordinal))
                    {
                        doneSeen.Set();
                    }
                }
            };

            pty.ProcessExited += code =>
            {
                exitCode = code;
                exitSeen.Set();
            };

            pty.Start(
                shell: workload.shell,
                columns: 100,
                rows: 30,
                workingDirectory: Environment.CurrentDirectory,
                environment: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["TERM"] = "xterm-256color",
                },
                arguments: workload.arguments);

            bool tokenObserved = doneSeen.Wait(TimeSpan.FromSeconds(8));
            bool exitObserved = exitSeen.Wait(TimeSpan.FromSeconds(8));
            success = tokenObserved && exitObserved && exitCode == 0;

            lines.Add("Visual checklist: inspect transcript colors, block bars, and log readability.");
            lines.Add($"PTY status: {(success ? "\x1b[30;42m OK \x1b[0m" : "\x1b[37;41mFAIL\x1b[0m")} token={tokenObserved} exit={exitObserved} code={exitCode}");
            lines.Add(string.Empty);
            lines.Add("\x1b[1mTranscript preview:\x1b[0m");

            IReadOnlyList<string> previewLines = ExtractPreviewLines(outputSnapshot);
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

            lines.Add(string.Empty);
            lines.Add("Expected result: colored status labels and bars should appear without corrupted control sequences.");
        }
        catch (Exception ex)
        {
            success = false;
            lines.Add($"Visual PTY transcript failed: {ex.GetType().Name}: {ex.Message}");
        }

        return new CatalogScenarioResult(Title, success, lines);
    }

    private static (string shell, IReadOnlyList<string> arguments) BuildWorkloadCommand()
    {
        if (OperatingSystem.IsWindows())
        {
            string? shellFromEnvironment = Environment.GetEnvironmentVariable("ComSpec");
            string shell = string.IsNullOrWhiteSpace(shellFromEnvironment)
                ? "cmd.exe"
                : shellFromEnvironment;

            return (shell, ["/Q", "/D", "/C", BuildWindowsWorkloadScript()]);
        }

        return ("/bin/sh", ["-c", BuildUnixWorkloadScript()]);
    }

    private static string BuildWindowsWorkloadScript()
    {
        return string.Join(" && ", [
            "echo [session] starting workload",
            "echo [ok] connected to endpoint",
            "echo cpu: 89%% mem: 58%% disk: 71%%",
            "echo warning: latency spike on pty lane",
            "echo ok: recovered",
            $"echo {TokenDone}",
        ]);
    }

    private static string BuildUnixWorkloadScript()
    {
        return string.Join(" ; ", [
            "printf '\\033[1;36m[session]\\033[0m starting workload\\n'",
            "printf '\\033[32m[ok]\\033[0m connected to endpoint\\n'",
            "printf 'cpu  : \\033[38;5;82m█████████████\\033[38;5;240m███\\033[0m 89%%\\n'",
            "printf 'mem  : \\033[38;5;45m████████\\033[38;5;240m████\\033[0m 58%%\\n'",
            "printf 'disk : \\033[38;5;220m██████████\\033[38;5;240m████\\033[0m 71%%\\n'",
            "printf '\\033[33mwarning\\033[0m latency spike on pty lane\\n'",
            "printf '\\033[32mok\\033[0m recovered\\n'",
            $"printf '{TokenDone}\\n'",
        ]);
    }

    private static IReadOnlyList<string> ExtractPreviewLines(string outputSnapshot)
    {
        string[] rawLines = outputSnapshot
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        List<string> previewLines = [];
        HashSet<string> seenNormalized = new(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < rawLines.Length; i++)
        {
            string line = rawLines[i];
            string normalized = StripAnsi(line).Trim();

            if (normalized.Length == 0 ||
                normalized.Contains(TokenDone, StringComparison.Ordinal) ||
                !IsExpectedTranscriptLine(normalized) ||
                !seenNormalized.Add(normalized))
            {
                continue;
            }

            previewLines.Add(line);
            if (previewLines.Count >= 10)
            {
                break;
            }
        }

        return previewLines;
    }

    private static bool IsExpectedTranscriptLine(string line)
    {
        return line.Contains("[session] starting workload", StringComparison.OrdinalIgnoreCase) ||
               line.Contains("[ok] connected to endpoint", StringComparison.OrdinalIgnoreCase) ||
               line.StartsWith("cpu", StringComparison.OrdinalIgnoreCase) ||
               line.StartsWith("mem", StringComparison.OrdinalIgnoreCase) ||
               line.StartsWith("disk", StringComparison.OrdinalIgnoreCase) ||
               line.Contains("warning", StringComparison.OrdinalIgnoreCase) ||
               line.EndsWith("recovered", StringComparison.OrdinalIgnoreCase);
    }

    private static string StripAnsi(string text)
    {
        StringBuilder builder = new(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\x1b')
            {
                i = SkipAnsiSequence(text, i);
                continue;
            }

            builder.Append(text[i]);
        }

        return builder.ToString();
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
