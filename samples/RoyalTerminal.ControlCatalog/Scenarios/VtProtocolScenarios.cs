// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.ControlCatalog;

internal sealed class VtModeCatalogScenario : ICatalogScenario
{
    public string Title => "VT mode catalog";

    public string Description => "ANSI + DEC mode inventory with DECRQM queries and toggle scripts for TUI-host validation.";

    public bool IncludeInFullSweep => true;

    public CatalogScenarioResult Execute()
    {
        List<string> lines =
        [
            "TUI checklist: verify mode query responses inside your terminal control host.",
            "Expected format: ANSI => ESC [ Ps ; <status> $ y, DEC => ESC [ ? Ps ; <status> $ y.",
            string.Empty,
            "ANSI mode DECRQM scripts:",
        ];

        for (int i = 0; i < CatalogConstants.AnsiModes.Length; i++)
        {
            int mode = CatalogConstants.AnsiModes[i];
            string query = $"\x1b[{mode}$p";
            lines.Add($"  ANSI {mode,4}: send {ControlTextFormatter.FormatControl(query)} expect status for mode {mode}.");
        }

        lines.Add(string.Empty);
        lines.Add("DEC mode DECRQM scripts:");
        for (int i = 0; i < CatalogConstants.DecModes.Length; i++)
        {
            int mode = CatalogConstants.DecModes[i];
            string query = $"\x1b[?{mode}$p";
            lines.Add($"  DEC  {mode,4}: send {ControlTextFormatter.FormatControl(query)} expect status for mode {mode}.");
        }

        lines.Add(string.Empty);
        List<TuiSequenceCheck> toggles = [];
        for (int i = 0; i < CatalogConstants.ToggleModes.Length; i++)
        {
            int mode = CatalogConstants.ToggleModes[i];
            toggles.Add(new TuiSequenceCheck(
                $"DECSET {mode}",
                $"\x1b[?{mode}h\x1b[?{mode}$p",
                "query should report status=1"));
            toggles.Add(new TuiSequenceCheck(
                $"DECRST {mode}",
                $"\x1b[?{mode}l\x1b[?{mode}$p",
                "query should report status=2"));
        }

        TuiRuntimeHelpers.AppendChecks(lines, "Toggle scripts:", toggles);
        lines.Add(string.Empty);
        lines.Add("Expected result: responses should match VT DECRQM conventions with no malformed control bytes.");

        bool success = CatalogConstants.AnsiModes.Length > 0 && CatalogConstants.DecModes.Length > 0;
        return new CatalogScenarioResult(Title, success, lines);
    }
}

internal sealed class VtQueryCatalogScenario : ICatalogScenario
{
    public string Title => "VT query catalog";

    public string Description => "DSR/DA/window query script matrix for terminal control response verification.";

    public bool IncludeInFullSweep => true;

    public CatalogScenarioResult Execute()
    {
        List<TuiSequenceCheck> checks =
        [
            new("DSR 5", "\x1b[5n", "expect ESC [ 0 n"),
            new("DSR 6", "\x1b[6n", "expect ESC [ <row> ; <col> R"),
            new("DA1", "\x1b[c", "expect ESC [ ? ... c"),
            new("DA2", "\x1b[>c", "expect ESC [ > ... c"),
            new("DA3", "\x1b[=c", "expect DCS response with terminal id"),
            new("CSI 14 t", "\x1b[14t", "expect ESC [ 4 ; <hpx> ; <wpx> t"),
            new("CSI 16 t", "\x1b[16t", "expect ESC [ 6 ; <cellh> ; <cellw> t"),
            new("CSI 18 t", "\x1b[18t", "expect ESC [ 8 ; <rows> ; <cols> t"),
            new("CSI 21 t", "\x1b[21t", "expect OSC title report payload"),
        ];

        List<string> lines =
        [
            "TUI checklist: execute each query and verify returned control-sequence shape.",
            "This catalog intentionally keeps response checking host-driven so it can run under any terminal control implementation.",
            string.Empty,
        ];

        TuiRuntimeHelpers.AppendChecks(lines, "Query script matrix:", checks);
        lines.Add(string.Empty);
        lines.Add("Expected result: each query yields one coherent response with proper terminator (R/c/t/ST).");
        return new CatalogScenarioResult(Title, true, lines);
    }
}

internal sealed class OscCatalogScenario : ICatalogScenario
{
    public string Title => "OSC catalog";

    public string Description => "Title, dynamic color, palette, and hyperlink OSC scripts for visual/behavior validation.";

    public bool IncludeInFullSweep => true;

    public CatalogScenarioResult Execute()
    {
        List<TuiSequenceCheck> checks =
        [
            new("OSC title set", "\x1b]2;rt-control-catalog\x1b\\", "window/tab title should update"),
            new("OSC 10 query", "\x1b]10;?\x1b\\", "expect OSC 10 rgb response"),
            new("OSC 11 query", "\x1b]11;?\x1b\\", "expect OSC 11 rgb response"),
            new("OSC 12 query", "\x1b]12;?\x1b\\", "expect OSC 12 rgb response"),
            new("OSC 4 query", "\x1b]4;1;?\x1b\\", "expect OSC 4;1 rgb response"),
            new("OSC 8 hyperlink", "\x1b]8;;https://example.com/catalog\x1b\\AB\x1b]8;;\x1b\\C", "A/B should be linked, C should be plain"),
        ];

        List<string> lines =
        [
            "TUI checklist: verify OSC handling in the hosting terminal control.",
            string.Empty,
        ];

        TuiRuntimeHelpers.AppendChecks(lines, "OSC script matrix:", checks);
        lines.Add(string.Empty);
        lines.Add("Hyperlink sample (should be clickable only on linked span):");
        lines.Add("  \x1b]8;;https://example.com/catalog\x1b\\Catalog link span\x1b]8;;\x1b\\ | plain text");
        lines.Add(string.Empty);
        lines.Add("Expected result: OSC state should not leak between segments and query responses should remain parseable.");
        return new CatalogScenarioResult(Title, true, lines);
    }
}

internal sealed class DcsAndKittyCatalogScenario : ICatalogScenario
{
    public string Title => "DCS + kitty catalog";

    public string Description => "DECRQSS and kitty keyboard scripts rendered for host-side VT feature validation.";

    public bool IncludeInFullSweep => true;

    public CatalogScenarioResult Execute()
    {
        List<TuiSequenceCheck> checks =
        [
            new("DECRQSS SGR", "\x1bP$qm\x1b\\", "expect DCS 1$r ... m ST"),
            new("DECRQSS margins", "\x1bP$qr\x1b\\", "expect DCS 1$r <top>;<bottom>r ST"),
            new("DECRQSS cursor", "\x1bP$q q\x1b\\", "expect DCS 1$r <style> q ST"),
            new("Kitty query", "\x1b[?u", "expect ESC [ ? <flags> u"),
            new("Kitty set/or", "\x1b[=1;1u\x1b[=4;2u\x1b[?u", "flags should include 1|4"),
            new("Kitty and-not", "\x1b[=1;3u\x1b[?u", "flag 1 should be cleared"),
            new("Kitty push/pop", "\x1b[>5u\x1b[<1u\x1b[?u", "stacked state should round-trip", Optional: true),
        ];

        List<string> lines =
        [
            "TUI checklist: run DCS/kitty commands and validate response framing and flag transitions.",
            string.Empty,
        ];

        TuiRuntimeHelpers.AppendChecks(lines, "DCS + kitty matrix:", checks);
        lines.Add(string.Empty);
        lines.Add("Expected result: DCS answers use ST terminator and kitty keyboard reports stay internally consistent.");
        return new CatalogScenarioResult(Title, true, lines);
    }
}
