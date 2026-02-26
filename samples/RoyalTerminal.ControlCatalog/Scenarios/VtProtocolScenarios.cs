// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Avalonia.Rendering;

namespace RoyalTerminal.ControlCatalog;

internal sealed class VtModeCatalogScenario : ICatalogScenario
{
    public string Title => "VT mode catalog";

    public string Description => "ANSI + DEC mode inventory with DECRQM queries and toggle checks.";

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

                lines.Add($"[{probe.Name}]");

                for (int modeIndex = 0; modeIndex < CatalogConstants.AnsiModes.Length; modeIndex++)
                {
                    int mode = CatalogConstants.AnsiModes[modeIndex];
                    totalChecks++;

                    bool hasResponse = VtCatalogHelpers.TryQueryMode(probe, decPrivate: false, mode, out string response);
                    bool isValid = hasResponse && VtCatalogHelpers.IsModeQueryResponse(response, decPrivate: false, mode, out _);
                    if (isValid)
                    {
                        passedChecks++;
                    }

                    lines.Add($"  ANSI {mode,4}: {(isValid ? "ok" : "fail")} {ControlTextFormatter.FormatControl(response)}");
                }

                for (int modeIndex = 0; modeIndex < CatalogConstants.DecModes.Length; modeIndex++)
                {
                    int mode = CatalogConstants.DecModes[modeIndex];
                    totalChecks++;

                    bool hasResponse = VtCatalogHelpers.TryQueryMode(probe, decPrivate: true, mode, out string response);
                    bool isValid = hasResponse && VtCatalogHelpers.IsModeQueryResponse(response, decPrivate: true, mode, out _);
                    if (isValid)
                    {
                        passedChecks++;
                    }

                    lines.Add($"  DEC  {mode,4}: {(isValid ? "ok" : "fail")} {ControlTextFormatter.FormatControl(response)}");
                }

                for (int modeIndex = 0; modeIndex < CatalogConstants.ToggleModes.Length; modeIndex++)
                {
                    int mode = CatalogConstants.ToggleModes[modeIndex];
                    totalChecks++;

                    bool toggled = VtCatalogHelpers.TryToggleDecMode(
                        probe,
                        mode,
                        out int setStatus,
                        out int resetStatus,
                        out string setResponse,
                        out string resetResponse);

                    if (toggled)
                    {
                        passedChecks++;
                    }

                    lines.Add(
                        $"  TOGGLE {mode,4}: {(toggled ? "ok" : "fail")} " +
                        $"set={setStatus} reset={resetStatus} " +
                        $"setRsp={ControlTextFormatter.FormatControl(setResponse)} resetRsp={ControlTextFormatter.FormatControl(resetResponse)}");
                }

                lines.Add($"  Summary: {passedChecks}/{totalChecks} checks passed.");
                lines.Add(string.Empty);

                if (requiredForSuccess && passedChecks != totalChecks)
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
}

internal sealed class VtQueryCatalogScenario : ICatalogScenario
{
    public string Title => "VT query catalog";

    public string Description => "DSR/DA/window-size report coverage.";

    public bool IncludeInFullSweep => true;

    public CatalogScenarioResult Execute()
    {
        List<string> lines = [];
        bool success = true;
        List<VtProcessorProbe> probes = VtProbeFactory.CreateProbes(lines);

        QueryCheck[] checks =
        [
            new("DSR 5", "\x1b[5n", static r => string.Equals(r, "\x1b[0n", StringComparison.Ordinal)),
            new("DSR 6", "\x1b[6n", static r => r.StartsWith("\x1b[", StringComparison.Ordinal) && r.EndsWith("R", StringComparison.Ordinal)),
            new("DA1", "\x1b[c", static r => r.StartsWith("\x1b[?", StringComparison.Ordinal) && r.EndsWith("c", StringComparison.Ordinal)),
            new("DA2", "\x1b[>c", static r => r.StartsWith("\x1b[>", StringComparison.Ordinal) && r.EndsWith("c", StringComparison.Ordinal)),
            new("DA3", "\x1b[=c", static r => r.StartsWith("\x1bP!|", StringComparison.Ordinal) && r.EndsWith("\x1b\\", StringComparison.Ordinal)),
            new("CSI 14 t", "\x1b[14t", static r => r.StartsWith("\x1b[4;", StringComparison.Ordinal) && r.EndsWith("t", StringComparison.Ordinal)),
            new("CSI 16 t", "\x1b[16t", static r => r.StartsWith("\x1b[6;", StringComparison.Ordinal) && r.EndsWith("t", StringComparison.Ordinal)),
            new("CSI 18 t", "\x1b[18t", static r => r.StartsWith("\x1b[8;", StringComparison.Ordinal) && r.EndsWith("t", StringComparison.Ordinal)),
            new("CSI 21 t", "\x1b[21t", static r => r.StartsWith("\x1b]l", StringComparison.Ordinal)),
        ];

        try
        {
            for (int i = 0; i < probes.Count; i++)
            {
                VtProcessorProbe probe = probes[i];
                bool requiredForSuccess = string.Equals(probe.Name, CatalogConstants.ManagedProbeName, StringComparison.Ordinal);
                int totalChecks = 0;
                int passedChecks = 0;

                lines.Add($"[{probe.Name}]");
                probe.NotifyResize(columns: 80, rows: 24, widthPx: 800, heightPx: 480);

                for (int checkIndex = 0; checkIndex < checks.Length; checkIndex++)
                {
                    QueryCheck check = checks[checkIndex];
                    totalChecks++;

                    probe.ClearResponses();
                    probe.Send(check.Sequence);
                    bool hasResponse = probe.TryTakeResponse(out string response);
                    bool isValid = hasResponse && check.Validator(response);
                    if (isValid)
                    {
                        passedChecks++;
                    }

                    lines.Add($"  {check.Name,-10}: {(isValid ? "ok" : "fail")} {ControlTextFormatter.FormatControl(response)}");
                }

                lines.Add($"  Summary: {passedChecks}/{totalChecks} checks passed.");
                lines.Add(string.Empty);

                if (requiredForSuccess && passedChecks != totalChecks)
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
}

internal sealed class OscCatalogScenario : ICatalogScenario
{
    public string Title => "OSC catalog";

    public string Description => "Title, palette/foreground queries, and OSC8 hyperlink behavior.";

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

                lines.Add($"[{probe.Name}]");

                totalChecks++;
                probe.ResetSignals();
                probe.Send("\x1b]2;rt-control-catalog\x1b\\");
                bool titleOk = string.Equals(probe.LastTitle, "rt-control-catalog", StringComparison.Ordinal);
                if (titleOk)
                {
                    passedChecks++;
                }

                lines.Add($"  OSC title set/query: {(titleOk ? "ok" : "fail")} title={probe.LastTitle ?? "<none>"}");

                totalChecks++;
                bool osc10Ok = VtCatalogHelpers.TrySingleResponse(
                    probe,
                    "\x1b]10;?\x1b\\",
                    static r => r.StartsWith("\x1b]10;rgb:", StringComparison.Ordinal),
                    out string osc10Response);
                if (osc10Ok)
                {
                    passedChecks++;
                }

                lines.Add($"  OSC 10 query: {(osc10Ok ? "ok" : "fail")} {ControlTextFormatter.FormatControl(osc10Response)}");

                totalChecks++;
                bool osc4Ok = VtCatalogHelpers.TrySingleResponse(
                    probe,
                    "\x1b]4;1;?\x1b\\",
                    static r => r.StartsWith("\x1b]4;1;rgb:", StringComparison.Ordinal),
                    out string osc4Response);
                if (osc4Ok)
                {
                    passedChecks++;
                }

                lines.Add($"  OSC 4 query: {(osc4Ok ? "ok" : "fail")} {ControlTextFormatter.FormatControl(osc4Response)}");

                totalChecks++;
                probe.Send("\x1b]8;;https://example.com/catalog\x1b\\AB\x1b]8;;\x1b\\C");
                TerminalRow row = probe.Screen.GetViewportRow(0);
                int firstId = row[0].HyperlinkId;
                bool hyperlinkOk =
                    firstId > 0 &&
                    row[1].HyperlinkId == firstId &&
                    row[2].HyperlinkId == 0 &&
                    probe.Screen.TryGetHyperlinkUrl(firstId, out string? hyperlinkUrl) &&
                    string.Equals(hyperlinkUrl, "https://example.com/catalog", StringComparison.Ordinal);
                if (hyperlinkOk)
                {
                    passedChecks++;
                }

                lines.Add($"  OSC 8 hyperlink: {(hyperlinkOk ? "ok" : "fail")} hyperlinkId={firstId}");
                lines.Add($"  Summary: {passedChecks}/{totalChecks} checks passed.");
                lines.Add(string.Empty);

                if (requiredForSuccess && passedChecks != totalChecks)
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
}

internal sealed class DcsAndKittyCatalogScenario : ICatalogScenario
{
    public string Title => "DCS + kitty catalog";

    public string Description => "DECRQSS and kitty keyboard protocol surface.";

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

                lines.Add($"[{probe.Name}]");

                totalChecks++;
                probe.Send("\x1b[1;31m");
                bool sgrDcsOk = VtCatalogHelpers.TrySingleResponse(
                    probe,
                    "\x1bP$qm\x1b\\",
                    static r => r.StartsWith("\x1bP1$r", StringComparison.Ordinal) && r.EndsWith("m\x1b\\", StringComparison.Ordinal),
                    out string sgrDcsResponse);
                if (sgrDcsOk)
                {
                    passedChecks++;
                }

                lines.Add($"  DCS DECRQSS SGR: {(sgrDcsOk ? "ok" : "fail")} {ControlTextFormatter.FormatControl(sgrDcsResponse)}");

                totalChecks++;
                probe.Send("\x1b[2;10r");
                bool marginDcsOk = VtCatalogHelpers.TrySingleResponse(
                    probe,
                    "\x1bP$qr\x1b\\",
                    static r => r.StartsWith("\x1bP1$r2;10r", StringComparison.Ordinal),
                    out string marginDcsResponse);
                if (marginDcsOk)
                {
                    passedChecks++;
                }

                lines.Add($"  DCS DECRQSS margins: {(marginDcsOk ? "ok" : "fail")} {ControlTextFormatter.FormatControl(marginDcsResponse)}");

                totalChecks++;
                probe.Send("\x1b[5 q");
                bool cursorDcsOk = VtCatalogHelpers.TrySingleResponse(
                    probe,
                    "\x1bP$q q\x1b\\",
                    static r => r.StartsWith("\x1bP1$r5 q", StringComparison.Ordinal),
                    out string cursorDcsResponse);
                if (cursorDcsOk)
                {
                    passedChecks++;
                }

                lines.Add($"  DCS DECRQSS cursor: {(cursorDcsOk ? "ok" : "fail")} {ControlTextFormatter.FormatControl(cursorDcsResponse)}");

                totalChecks++;
                bool kittyDefaultOk = VtCatalogHelpers.TrySingleResponse(
                    probe,
                    "\x1b[?u",
                    static r => r.StartsWith("\x1b[?", StringComparison.Ordinal) && r.EndsWith("u", StringComparison.Ordinal),
                    out string kittyDefaultResponse);
                if (kittyDefaultOk)
                {
                    passedChecks++;
                }

                lines.Add($"  Kitty query default: {(kittyDefaultOk ? "ok" : "fail")} {ControlTextFormatter.FormatControl(kittyDefaultResponse)}");

                totalChecks++;
                probe.Send("\x1b[=1;1u");
                probe.Send("\x1b[=4;2u");
                bool kittySetOk = VtCatalogHelpers.TrySingleResponse(
                    probe,
                    "\x1b[?u",
                    requiredForSuccess
                        ? static r => string.Equals(r, "\x1b[?5u", StringComparison.Ordinal)
                        : static r => r.StartsWith("\x1b[?", StringComparison.Ordinal),
                    out string kittySetResponse);
                if (kittySetOk)
                {
                    passedChecks++;
                }

                lines.Add($"  Kitty set/or: {(kittySetOk ? "ok" : "fail")} {ControlTextFormatter.FormatControl(kittySetResponse)}");

                totalChecks++;
                probe.Send("\x1b[=1;3u");
                bool kittyAndNotOk = VtCatalogHelpers.TrySingleResponse(
                    probe,
                    "\x1b[?u",
                    requiredForSuccess
                        ? static r => string.Equals(r, "\x1b[?4u", StringComparison.Ordinal)
                        : static r => r.StartsWith("\x1b[?", StringComparison.Ordinal),
                    out string kittyAndNotResponse);
                if (kittyAndNotOk)
                {
                    passedChecks++;
                }

                lines.Add($"  Kitty and-not: {(kittyAndNotOk ? "ok" : "fail")} {ControlTextFormatter.FormatControl(kittyAndNotResponse)}");
                lines.Add($"  Summary: {passedChecks}/{totalChecks} checks passed.");
                lines.Add(string.Empty);

                if (requiredForSuccess && passedChecks != totalChecks)
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
}
