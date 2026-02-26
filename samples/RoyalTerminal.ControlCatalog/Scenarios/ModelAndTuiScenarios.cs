// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;

namespace RoyalTerminal.ControlCatalog;

internal sealed class TrackerCatalogScenario : ICatalogScenario
{
    public string Title => "Mouse/focus/win32 trackers";

    public string Description => "Mode trackers and protocol encoder checks.";

    public bool IncludeInFullSweep => true;

    public CatalogScenarioResult Execute()
    {
        List<string> lines = [];
        int totalChecks = 0;
        int passedChecks = 0;

        TerminalMouseModeTracker mouseTracker = new();
        TerminalWin32InputModeTracker win32Tracker = new();

        totalChecks++;
        bool mouseSetChanged = mouseTracker.Process("\x1b[?1002h\x1b[?1006h"u8);
        bool mouseSetOk =
            mouseSetChanged &&
            mouseTracker.ModeState.TrackingMode == TerminalMouseTrackingMode.ButtonMotion &&
            mouseTracker.ModeState.Encoding == TerminalMouseEncoding.Sgr;
        if (mouseSetOk)
        {
            passedChecks++;
        }

        lines.Add($"Mouse tracker set modes: {(mouseSetOk ? "ok" : "fail")} state={mouseTracker.ModeState}");

        totalChecks++;
        TerminalPointerEvent pressEvent = new(
            Kind: TerminalPointerEventKind.Button,
            X: 0,
            Y: 0,
            Button: TerminalMouseButton.Left,
            Action: TerminalInputAction.Press,
            Modifiers: TerminalModifiers.None);
        bool sgrEncodeOk = TerminalMouseProtocolEncoder.TryEncode(
            pressEvent,
            mouseTracker.ModeState,
            column: 10,
            row: 4,
            out byte[] sgrSequence) &&
            string.Equals(Encoding.ASCII.GetString(sgrSequence), "\x1b[<0;10;4M", StringComparison.Ordinal);
        if (sgrEncodeOk)
        {
            passedChecks++;
        }

        lines.Add($"Mouse SGR encode: {(sgrEncodeOk ? "ok" : "fail")} sequence={ControlTextFormatter.FormatControl(Encoding.ASCII.GetString(sgrSequence))}");

        totalChecks++;
        _ = mouseTracker.Process("\x1b[?1002l\x1b[?1006l\x1b[?9h"u8);
        bool x10StateOk =
            mouseTracker.ModeState.TrackingMode == TerminalMouseTrackingMode.X10Press &&
            mouseTracker.ModeState.Encoding == TerminalMouseEncoding.Default;
        if (x10StateOk)
        {
            passedChecks++;
        }

        lines.Add($"Mouse tracker X10 fallback: {(x10StateOk ? "ok" : "fail")} state={mouseTracker.ModeState}");

        totalChecks++;
        TerminalPointerEvent releaseEvent = new(
            Kind: TerminalPointerEventKind.Button,
            X: 0,
            Y: 0,
            Button: TerminalMouseButton.Left,
            Action: TerminalInputAction.Release,
            Modifiers: TerminalModifiers.None);
        bool x10ReleaseSuppressed = !TerminalMouseProtocolEncoder.TryEncode(
            releaseEvent,
            mouseTracker.ModeState,
            column: 10,
            row: 4,
            out _);
        if (x10ReleaseSuppressed)
        {
            passedChecks++;
        }

        lines.Add($"Mouse X10 release suppression: {(x10ReleaseSuppressed ? "ok" : "fail")}");

        totalChecks++;
        bool win32SetChanged = win32Tracker.Process("\x1b[?9001h\x1b[?1004h"u8);
        bool win32SetOk = win32SetChanged && win32Tracker.Win32InputMode && win32Tracker.FocusEventMode;
        if (win32SetOk)
        {
            passedChecks++;
        }

        lines.Add($"Win32/focus tracker set: {(win32SetOk ? "ok" : "fail")} win32={win32Tracker.Win32InputMode} focus={win32Tracker.FocusEventMode}");

        totalChecks++;
        bool win32ResetChanged = win32Tracker.Process("\x1b[!p"u8);
        bool win32ResetOk = win32ResetChanged && !win32Tracker.Win32InputMode && !win32Tracker.FocusEventMode;
        if (win32ResetOk)
        {
            passedChecks++;
        }

        lines.Add($"Win32/focus tracker reset: {(win32ResetOk ? "ok" : "fail")} win32={win32Tracker.Win32InputMode} focus={win32Tracker.FocusEventMode}");

        bool success = passedChecks == totalChecks;
        lines.Add($"Summary: {passedChecks}/{totalChecks} checks passed.");
        return new CatalogScenarioResult(Title, success, lines);
    }
}

internal sealed class InteractiveInputWindowCatalogScenario : ICatalogScenario
{
    public string Title => "Main interactive input/window tests";

    public string Description => "Interactive mouse, cursor, keyboard input, and window interaction protocol checks.";

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

                TerminalMouseModeTracker mouseTracker = new();

                totalChecks++;
                bool mouseModeSetChanged = mouseTracker.Process("\x1b[?1003h\x1b[?1006h"u8);
                bool mouseModeSetOk =
                    mouseModeSetChanged &&
                    mouseTracker.ModeState.TrackingMode == TerminalMouseTrackingMode.AnyMotion &&
                    mouseTracker.ModeState.Encoding == TerminalMouseEncoding.Sgr;
                if (mouseModeSetOk)
                {
                    passedChecks++;
                }

                lines.Add($"  Mouse interactive mode set (1003/1006): {(mouseModeSetOk ? "ok" : "fail")} state={mouseTracker.ModeState}");

                totalChecks++;
                TerminalPointerEvent motionEvent = new(
                    Kind: TerminalPointerEventKind.Move,
                    X: 0,
                    Y: 0,
                    Button: TerminalMouseButton.Left,
                    Action: TerminalInputAction.Press,
                    Modifiers: TerminalModifiers.Shift);
                bool mouseMotionEncodeOk = TerminalMouseProtocolEncoder.TryEncode(
                    motionEvent,
                    mouseTracker.ModeState,
                    column: 40,
                    row: 12,
                    out byte[] mouseMotionSequence) &&
                    string.Equals(Encoding.ASCII.GetString(mouseMotionSequence), "\x1b[<36;40;12M", StringComparison.Ordinal);
                if (mouseMotionEncodeOk)
                {
                    passedChecks++;
                }

                lines.Add($"  Mouse move encode (SGR+shift): {(mouseMotionEncodeOk ? "ok" : "fail")} seq={ControlTextFormatter.FormatControl(Encoding.ASCII.GetString(mouseMotionSequence))}");

                totalChecks++;
                TerminalPointerEvent wheelEvent = new(
                    Kind: TerminalPointerEventKind.Scroll,
                    X: 0,
                    Y: 0,
                    Button: TerminalMouseButton.None,
                    Action: TerminalInputAction.Press,
                    Modifiers: TerminalModifiers.Control,
                    DeltaX: 0,
                    DeltaY: 1);
                bool mouseWheelEncodeOk = TerminalMouseProtocolEncoder.TryEncode(
                    wheelEvent,
                    mouseTracker.ModeState,
                    column: 40,
                    row: 12,
                    out byte[] mouseWheelSequence) &&
                    string.Equals(Encoding.ASCII.GetString(mouseWheelSequence), "\x1b[<80;40;12M", StringComparison.Ordinal);
                if (mouseWheelEncodeOk)
                {
                    passedChecks++;
                }

                lines.Add($"  Mouse wheel encode (ctrl): {(mouseWheelEncodeOk ? "ok" : "fail")} seq={ControlTextFormatter.FormatControl(Encoding.ASCII.GetString(mouseWheelSequence))}");

                totalChecks++;
                TerminalPointerEvent releaseEvent = new(
                    Kind: TerminalPointerEventKind.Button,
                    X: 0,
                    Y: 0,
                    Button: TerminalMouseButton.Left,
                    Action: TerminalInputAction.Release,
                    Modifiers: TerminalModifiers.None);
                bool mouseReleaseEncodeOk = TerminalMouseProtocolEncoder.TryEncode(
                    releaseEvent,
                    mouseTracker.ModeState,
                    column: 40,
                    row: 12,
                    out byte[] mouseReleaseSequence) &&
                    string.Equals(Encoding.ASCII.GetString(mouseReleaseSequence), "\x1b[<0;40;12m", StringComparison.Ordinal);
                if (mouseReleaseEncodeOk)
                {
                    passedChecks++;
                }

                lines.Add($"  Mouse release encode: {(mouseReleaseEncodeOk ? "ok" : "fail")} seq={ControlTextFormatter.FormatControl(Encoding.ASCII.GetString(mouseReleaseSequence))}");

                totalChecks++;
                bool mouseModeResetChanged = mouseTracker.Process("\x1b[?1003l\x1b[?1006l"u8);
                bool mouseModeResetOk =
                    mouseModeResetChanged &&
                    mouseTracker.ModeState.TrackingMode == TerminalMouseTrackingMode.None &&
                    mouseTracker.ModeState.Encoding == TerminalMouseEncoding.Default;
                if (mouseModeResetOk)
                {
                    passedChecks++;
                }

                lines.Add($"  Mouse interactive mode reset: {(mouseModeResetOk ? "ok" : "fail")} state={mouseTracker.ModeState}");

                totalChecks++;
                probe.Send("\x1b[5;9H");
                bool cursorStateOk = probe.Processor.CursorRow == 4 && probe.Processor.CursorCol == 8;
                bool cursorReportOk = VtCatalogHelpers.TrySingleResponse(
                    probe,
                    "\x1b[6n",
                    response => IsCursorReport(response, expectedRow: 5, expectedColumn: 9, strict: requiredForSuccess),
                    out string cursorReportResponse);
                bool cursorInteractionOk = cursorStateOk && cursorReportOk;
                if (cursorInteractionOk)
                {
                    passedChecks++;
                }

                lines.Add(
                    $"  Cursor move/report interaction: {(cursorInteractionOk ? "ok" : "fail")} " +
                    $"state=({probe.Processor.CursorRow + 1},{probe.Processor.CursorCol + 1}) rsp={ControlTextFormatter.FormatControl(cursorReportResponse)}");

                totalChecks++;
                bool cursorVisibilityOk = VtCatalogHelpers.TryToggleDecMode(
                    probe,
                    25,
                    out int cursorSetStatus,
                    out int cursorResetStatus,
                    out string cursorSetResponse,
                    out string cursorResetResponse);
                if (cursorVisibilityOk)
                {
                    passedChecks++;
                }

                lines.Add(
                    $"  Cursor visibility mode (25): {(cursorVisibilityOk ? "ok" : "fail")} " +
                    $"set={cursorSetStatus} reset={cursorResetStatus} " +
                    $"setRsp={ControlTextFormatter.FormatControl(cursorSetResponse)} resetRsp={ControlTextFormatter.FormatControl(cursorResetResponse)}");

                totalChecks++;
                bool applicationCursorKeysOk = VtCatalogHelpers.TryToggleDecMode(
                    probe,
                    1,
                    out int appCursorSetStatus,
                    out int appCursorResetStatus,
                    out string appCursorSetResponse,
                    out string appCursorResetResponse);
                if (applicationCursorKeysOk)
                {
                    passedChecks++;
                }

                lines.Add(
                    $"  Keyboard app-cursor mode (1): {(applicationCursorKeysOk ? "ok" : "fail")} " +
                    $"set={appCursorSetStatus} reset={appCursorResetStatus} " +
                    $"setRsp={ControlTextFormatter.FormatControl(appCursorSetResponse)} resetRsp={ControlTextFormatter.FormatControl(appCursorResetResponse)}");

                totalChecks++;
                probe.Send("\x1b=");
                bool keypadOn = probe.Processor.ModeState.ApplicationKeypad;
                probe.Send("\x1b>");
                bool keypadOff = !probe.Processor.ModeState.ApplicationKeypad;
                bool applicationKeypadOk = keypadOn && keypadOff;
                if (applicationKeypadOk)
                {
                    passedChecks++;
                }

                lines.Add($"  Keyboard app-keypad interaction: {(applicationKeypadOk ? "ok" : "fail")} on={keypadOn} off={keypadOff}");

                totalChecks++;
                bool bracketedPasteOk = VtCatalogHelpers.TryToggleDecMode(
                    probe,
                    2004,
                    out int pasteSetStatus,
                    out int pasteResetStatus,
                    out string pasteSetResponse,
                    out string pasteResetResponse);
                if (bracketedPasteOk)
                {
                    passedChecks++;
                }

                lines.Add(
                    $"  Keyboard bracketed-paste mode (2004): {(bracketedPasteOk ? "ok" : "fail")} " +
                    $"set={pasteSetStatus} reset={pasteResetStatus} " +
                    $"setRsp={ControlTextFormatter.FormatControl(pasteSetResponse)} resetRsp={ControlTextFormatter.FormatControl(pasteResetResponse)}");

                totalChecks++;
                bool kittyKeyboardOk;
                int kittyFlagsAfterSet = -1;
                int kittyFlagsAfterAndNot = -1;
                string kittyResponse = string.Empty;
                if (probe.Processor is IKittyKeyboardStateSource kittySource)
                {
                    probe.Send("\x1b[=1;1u\x1b[=4;2u");
                    kittyFlagsAfterSet = kittySource.KittyKeyboardFlags;
                    bool kittySetFlagsOk = requiredForSuccess ? kittyFlagsAfterSet == 5 : kittyFlagsAfterSet >= 0;
                    bool kittyQueryOk = VtCatalogHelpers.TrySingleResponse(
                        probe,
                        "\x1b[?u",
                        requiredForSuccess
                            ? static response => string.Equals(response, "\x1b[?5u", StringComparison.Ordinal)
                            : static response => response.StartsWith("\x1b[?", StringComparison.Ordinal) && response.EndsWith("u", StringComparison.Ordinal),
                        out kittyResponse);

                    probe.Send("\x1b[=1;3u");
                    kittyFlagsAfterAndNot = kittySource.KittyKeyboardFlags;
                    bool kittyAndNotOk = requiredForSuccess ? kittyFlagsAfterAndNot == 4 : kittyFlagsAfterAndNot >= 0;
                    kittyKeyboardOk = kittySetFlagsOk && kittyQueryOk && kittyAndNotOk;
                }
                else
                {
                    kittyKeyboardOk = !requiredForSuccess;
                }

                if (kittyKeyboardOk)
                {
                    passedChecks++;
                }

                lines.Add(
                    $"  Keyboard kitty interaction: {(kittyKeyboardOk ? "ok" : "fail")} " +
                    $"flagsSet={kittyFlagsAfterSet} flagsAndNot={kittyFlagsAfterAndNot} rsp={ControlTextFormatter.FormatControl(kittyResponse)}");

                totalChecks++;
                bool focusEventsOk = VtCatalogHelpers.TryToggleDecMode(
                    probe,
                    1004,
                    out int focusSetStatus,
                    out int focusResetStatus,
                    out string focusSetResponse,
                    out string focusResetResponse);
                if (focusEventsOk)
                {
                    passedChecks++;
                }

                lines.Add(
                    $"  Focus/window interaction mode (1004): {(focusEventsOk ? "ok" : "fail")} " +
                    $"set={focusSetStatus} reset={focusResetStatus} " +
                    $"setRsp={ControlTextFormatter.FormatControl(focusSetResponse)} resetRsp={ControlTextFormatter.FormatControl(focusResetResponse)}");

                probe.NotifyResize(columns: 132, rows: 42, widthPx: 1320, heightPx: 840);

                totalChecks++;
                int expectedRows = probe.Screen.ViewportRows;
                int expectedColumns = probe.Screen.Columns;
                bool windowCellReportOk = VtCatalogHelpers.TrySingleResponse(
                    probe,
                    "\x1b[18t",
                    response => IsWindowCellReport(response, expectedRows, expectedColumns, strict: requiredForSuccess),
                    out string windowCellResponse);
                if (windowCellReportOk)
                {
                    passedChecks++;
                }

                lines.Add($"  Window cell-size interaction (18t): {(windowCellReportOk ? "ok" : "fail")} rsp={ControlTextFormatter.FormatControl(windowCellResponse)}");

                totalChecks++;
                bool windowPixelReportOk = VtCatalogHelpers.TrySingleResponse(
                    probe,
                    "\x1b[14t",
                    response => IsWindowPixelReport(response, expectedHeightPx: 840, expectedWidthPx: 1320, strict: requiredForSuccess),
                    out string windowPixelResponse);
                if (windowPixelReportOk)
                {
                    passedChecks++;
                }

                lines.Add($"  Window pixel-size interaction (14t): {(windowPixelReportOk ? "ok" : "fail")} rsp={ControlTextFormatter.FormatControl(windowPixelResponse)}");

                totalChecks++;
                bool windowCellPixelReportOk = VtCatalogHelpers.TrySingleResponse(
                    probe,
                    "\x1b[16t",
                    static response => IsCellPixelReport(response),
                    out string windowCellPixelResponse);
                if (windowCellPixelReportOk)
                {
                    passedChecks++;
                }

                lines.Add($"  Window cell-pixel report (16t): {(windowCellPixelReportOk ? "ok" : "fail")} rsp={ControlTextFormatter.FormatControl(windowCellPixelResponse)}");

                totalChecks++;
                probe.Send("\x1b]2;rt-interaction-window\x1b\\");
                bool titleCallbackOk = string.Equals(probe.LastTitle, "rt-interaction-window", StringComparison.Ordinal);
                bool titleReportOk = VtCatalogHelpers.TrySingleResponse(
                    probe,
                    "\x1b[21t",
                    static response => response.StartsWith("\x1b]l", StringComparison.Ordinal),
                    out string titleResponse);
                bool titleInteractionOk = titleCallbackOk && titleReportOk;
                if (titleInteractionOk)
                {
                    passedChecks++;
                }

                lines.Add($"  Window title interaction (OSC2/21t): {(titleInteractionOk ? "ok" : "fail")} rsp={ControlTextFormatter.FormatControl(titleResponse)}");

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

    private static bool IsCursorReport(string response, int expectedRow, int expectedColumn, bool strict)
    {
        if (!response.StartsWith("\x1b[", StringComparison.Ordinal) ||
            !response.EndsWith("R", StringComparison.Ordinal))
        {
            return false;
        }

        return !strict || string.Equals(response, $"\x1b[{expectedRow};{expectedColumn}R", StringComparison.Ordinal);
    }

    private static bool IsWindowCellReport(string response, int expectedRows, int expectedColumns, bool strict)
    {
        if (!TryParseWindowMetricResponse(response, prefix: "\x1b[8;", out int rows, out int columns))
        {
            return false;
        }

        return !strict || (rows == expectedRows && columns == expectedColumns);
    }

    private static bool IsWindowPixelReport(string response, int expectedHeightPx, int expectedWidthPx, bool strict)
    {
        if (!TryParseWindowMetricResponse(response, prefix: "\x1b[4;", out int heightPx, out int widthPx))
        {
            return false;
        }

        return !strict || (heightPx == expectedHeightPx && widthPx == expectedWidthPx);
    }

    private static bool IsCellPixelReport(string response)
    {
        return TryParseWindowMetricResponse(response, prefix: "\x1b[6;", out int heightPx, out int widthPx) &&
               heightPx > 0 &&
               widthPx > 0;
    }

    private static bool TryParseWindowMetricResponse(string response, string prefix, out int first, out int second)
    {
        first = 0;
        second = 0;

        if (!response.StartsWith(prefix, StringComparison.Ordinal) ||
            !response.EndsWith("t", StringComparison.Ordinal))
        {
            return false;
        }

        ReadOnlySpan<char> payload = response.AsSpan(prefix.Length, response.Length - prefix.Length - 1);
        int separatorIndex = payload.IndexOf(';');
        if (separatorIndex <= 0 || separatorIndex >= payload.Length - 1)
        {
            return false;
        }

        return int.TryParse(payload[..separatorIndex], out first) &&
               int.TryParse(payload[(separatorIndex + 1)..], out second);
    }
}

internal sealed class RenderingModelCatalogScenario : ICatalogScenario
{
    public string Title => "Rendering/text model catalog";

    public string Description => "Cursor style, SGR decorations, and grapheme behavior.";

    public bool IncludeInFullSweep => true;

    public CatalogScenarioResult Execute()
    {
        List<string> lines = [];
        int totalChecks = 0;
        int passedChecks = 0;

        using VtProcessorProbe probe = VtProcessorProbe.CreateManaged(columns: 32, rows: 8);

        totalChecks++;
        probe.Send("e\u0301");
        TerminalRow firstRow = probe.Screen.GetViewportRow(0);
        bool combiningOk =
            probe.Processor.CursorCol == 1 &&
            string.Equals(firstRow[0].Grapheme, "e\u0301", StringComparison.Ordinal);
        if (combiningOk)
        {
            passedChecks++;
        }

        lines.Add($"Combining grapheme model: {(combiningOk ? "ok" : "fail")} cell0={firstRow[0].Grapheme ?? "<null>"}");

        totalChecks++;
        ITerminalCursorStyleSource cursorStyleSource = (ITerminalCursorStyleSource)probe.Processor;
        probe.Send("\x1b[6 q");
        bool cursorStyleOk =
            cursorStyleSource.CursorStyle == TerminalCursorStyle.Bar &&
            !cursorStyleSource.CursorBlinking;
        if (cursorStyleOk)
        {
            passedChecks++;
        }

        lines.Add(
            $"Cursor style model: {(cursorStyleOk ? "ok" : "fail")} " +
            $"style={cursorStyleSource.CursorStyle} blinking={cursorStyleSource.CursorBlinking}");

        totalChecks++;
        probe.Send("\x1b[53mX");
        TerminalRow decoratedRow = probe.Screen.GetViewportRow(0);
        bool overlineOk = (decoratedRow[1].Decorations & CellDecorations.Overline) != 0;
        if (overlineOk)
        {
            passedChecks++;
        }

        lines.Add($"SGR decoration model: {(overlineOk ? "ok" : "fail")} overlineCellIndex=1");

        totalChecks++;
        probe.Send("\x1b[?2004h\x1b[?1049h");
        bool modeSnapshotOk = probe.Processor.ModeState.BracketedPaste && probe.Processor.ModeState.AlternateScreen;
        probe.Send("\x1b[?1049l\x1b[?2004l");
        if (modeSnapshotOk)
        {
            passedChecks++;
        }

        lines.Add(
            $"Mode snapshot model: {(modeSnapshotOk ? "ok" : "fail")} " +
            $"bracketedPaste={probe.Processor.ModeState.BracketedPaste} altScreen={probe.Processor.ModeState.AlternateScreen}");

        totalChecks++;
        ITerminalFocusEventModeSource focusModeSource = (ITerminalFocusEventModeSource)probe.Processor;
        probe.Send("\x1b[?1004h");
        bool focusModeOn = focusModeSource.FocusEventsEnabled;
        probe.Send("\x1b[?1004l");
        bool focusModeOff = !focusModeSource.FocusEventsEnabled;
        bool focusModeOk = focusModeOn && focusModeOff;
        if (focusModeOk)
        {
            passedChecks++;
        }

        lines.Add($"Focus mode model: {(focusModeOk ? "ok" : "fail")} enabledAfterReset={focusModeSource.FocusEventsEnabled}");

        bool success = passedChecks == totalChecks;
        lines.Add($"Summary: {passedChecks}/{totalChecks} checks passed.");
        return new CatalogScenarioResult(Title, success, lines);
    }
}

internal sealed class ComplexGlyphAndVtCatalogScenario : ICatalogScenario
{
    public string Title => "Ncurses/TUI rendering parity";

    public string Description => "App-style VT rendering: alt-screen, line drawing, wide graphemes, colors, REP/IRM, and scrolling regions.";

    public bool IncludeInFullSweep => true;

    public CatalogScenarioResult Execute()
    {
        List<string> lines = [];
        bool success = true;
        List<VtProcessorProbe> probes = VtProbeFactory.CreateProbes(lines);

        const string canadaFlag = "\U0001F1E8\U0001F1E6";
        const char scanLine = '\u23BA';

        try
        {
            for (int i = 0; i < probes.Count; i++)
            {
                VtProcessorProbe probe = probes[i];
                bool requiredForSuccess = string.Equals(probe.Name, CatalogConstants.ManagedProbeName, StringComparison.Ordinal);
                int totalChecks = 0;
                int passedChecks = 0;

                lines.Add($"[{probe.Name}]");
                probe.NotifyResize(columns: 120, rows: 40, widthPx: 1200, heightPx: 800);

                totalChecks++;
                probe.Send("\x1b[?1049h\x1b[?25l");
                bool altScreenOn = probe.Processor.ModeState.AlternateScreen && !probe.Processor.ModeState.CursorVisible;
                if (altScreenOn)
                {
                    passedChecks++;
                }

                lines.Add($"  Alt-screen + hidden cursor: {(altScreenOn ? "ok" : "fail")}");

                probe.Send("\x1b[2J\x1b[H");
                probe.Send("\x1b[1;1H┌────┐\x1b[2;1H│████│\x1b[3;1H│⣿█⎺│\x1b[4;1H└────┘");

                totalChecks++;
                TerminalRow frameTop = probe.Screen.GetViewportRow(0);
                TerminalRow frameMid = probe.Screen.GetViewportRow(1);
                TerminalRow frameGlyphs = probe.Screen.GetViewportRow(2);
                TerminalRow frameBottom = probe.Screen.GetViewportRow(3);
                bool frameGlyphsOk =
                    frameTop[0].Codepoint == '┌' &&
                    frameTop[5].Codepoint == '┐' &&
                    frameBottom[0].Codepoint == '└' &&
                    frameBottom[5].Codepoint == '┘' &&
                    frameMid[1].Codepoint == '█' &&
                    frameGlyphs[1].Codepoint == '⣿' &&
                    frameGlyphs[2].Codepoint == '█' &&
                    frameGlyphs[3].Codepoint == scanLine;
                if (frameGlyphsOk)
                {
                    passedChecks++;
                }

                lines.Add($"  Box/braille/block/scanline glyphs: {(frameGlyphsOk ? "ok" : "fail")}");

                totalChecks++;
                probe.Send("\x1b[6;1H\x1b[38;5;196;48;5;235mC\x1b[0m");
                TerminalRow colorRow = probe.Screen.GetViewportRow(5);
                bool ansi256ColorOk =
                    colorRow[0].Codepoint == 'C' &&
                    colorRow[0].Foreground != probe.Screen.DefaultForeground &&
                    colorRow[0].HasBackground;
                if (ansi256ColorOk)
                {
                    passedChecks++;
                }

                lines.Add($"  256-color attributes: {(ansi256ColorOk ? "ok" : "fail")} fg=0x{colorRow[0].Foreground:X8} bg=0x{colorRow[0].Background:X8}");

                totalChecks++;
                probe.Send("\x1b[6;3H\x1b[38;2;10;200;30;48;2;5;10;15mT\x1b[0m");
                colorRow = probe.Screen.GetViewportRow(5);
                bool trueColorOk =
                    colorRow[2].Codepoint == 'T' &&
                    colorRow[2].Foreground != probe.Screen.DefaultForeground &&
                    colorRow[2].HasBackground;
                if (trueColorOk)
                {
                    passedChecks++;
                }

                lines.Add($"  True-color attributes: {(trueColorOk ? "ok" : "fail")} fg=0x{colorRow[2].Foreground:X8} bg=0x{colorRow[2].Background:X8}");

                totalChecks++;
                probe.Send("\x1b[7;1H中");
                TerminalRow wideRow = probe.Screen.GetViewportRow(6);
                bool wideCharOk =
                    wideRow[0].Codepoint == '中' &&
                    wideRow[0].Width == 2 &&
                    wideRow[1].Width == 0;
                if (wideCharOk)
                {
                    passedChecks++;
                }

                lines.Add($"  Wide glyph spacing: {(wideCharOk ? "ok" : "fail")} widths={wideRow[0].Width}/{wideRow[1].Width}");

                totalChecks++;
                probe.Send("\x1b[7;4He\u0301");
                wideRow = probe.Screen.GetViewportRow(6);
                bool combiningOk = string.Equals(wideRow[3].Grapheme, "e\u0301", StringComparison.Ordinal);
                if (combiningOk)
                {
                    passedChecks++;
                }

                lines.Add($"  Combining grapheme: {(combiningOk ? "ok" : "fail")} value={wideRow[3].Grapheme ?? "<null>"}");

                totalChecks++;
                probe.Send($"\x1b[8;1H{canadaFlag}");
                TerminalRow flagRow = probe.Screen.GetViewportRow(7);
                bool regionalIndicatorOk =
                    string.Equals(flagRow[0].Grapheme, canadaFlag, StringComparison.Ordinal) &&
                    flagRow[0].Width == 2 &&
                    flagRow[1].Width == 0;
                if (regionalIndicatorOk)
                {
                    passedChecks++;
                }

                lines.Add($"  Regional-indicator grapheme: {(regionalIndicatorOk ? "ok" : "fail")} width={flagRow[0].Width}");

                totalChecks++;
                probe.Send("\x1b[10;1HAAAA");
                probe.Send("\x1b[11;1HBBBB");
                probe.Send("\x1b[12;1HCCCC");
                probe.Send("\x1b[13;1HDDDD");
                probe.Send("\x1b[10;13r\x1b[13;1H\n\x1b[r");
                TerminalRow scrollRowA = probe.Screen.GetViewportRow(9);
                TerminalRow scrollRowB = probe.Screen.GetViewportRow(10);
                TerminalRow scrollRowC = probe.Screen.GetViewportRow(11);
                TerminalRow scrollRowD = probe.Screen.GetViewportRow(12);
                bool scrollRegionOk =
                    scrollRowA[0].Codepoint == 'B' &&
                    scrollRowB[0].Codepoint == 'C' &&
                    scrollRowC[0].Codepoint == 'D' &&
                    !scrollRowD[0].HasContent;
                if (scrollRegionOk)
                {
                    passedChecks++;
                }

                lines.Add($"  Scroll region behavior: {(scrollRegionOk ? "ok" : "fail")}");

                totalChecks++;
                probe.Send("\x1b[15;1HABCD\x1b[15;2H\x1b[4hZ\x1b[4l");
                TerminalRow irmRow = probe.Screen.GetViewportRow(14);
                bool insertModeOk =
                    irmRow[0].Codepoint == 'A' &&
                    irmRow[1].Codepoint == 'Z' &&
                    irmRow[2].Codepoint == 'B' &&
                    irmRow[3].Codepoint == 'C' &&
                    irmRow[4].Codepoint == 'D';
                if (insertModeOk)
                {
                    passedChecks++;
                }

                lines.Add($"  Insert mode (IRM): {(insertModeOk ? "ok" : "fail")}");

                totalChecks++;
                probe.Send("\x1b[16;1HX\x1b[3b");
                TerminalRow repRow = probe.Screen.GetViewportRow(15);
                bool repOk =
                    repRow[0].Codepoint == 'X' &&
                    repRow[1].Codepoint == 'X' &&
                    repRow[2].Codepoint == 'X' &&
                    repRow[3].Codepoint == 'X';
                if (repOk)
                {
                    passedChecks++;
                }

                lines.Add($"  REP repeat behavior: {(repOk ? "ok" : "fail")}");

                totalChecks++;
                bool mouseModesOk =
                    VtCatalogHelpers.TryToggleDecMode(probe, 1000, out _, out _, out _, out _) &&
                    VtCatalogHelpers.TryToggleDecMode(probe, 1002, out _, out _, out _, out _) &&
                    VtCatalogHelpers.TryToggleDecMode(probe, 1003, out _, out _, out _, out _) &&
                    VtCatalogHelpers.TryToggleDecMode(probe, 1006, out _, out _, out _, out _);
                if (mouseModesOk)
                {
                    passedChecks++;
                }

                lines.Add($"  Ncurses mouse mode toggles (1000/1002/1003/1006): {(mouseModesOk ? "ok" : "fail")}");

                totalChecks++;
                bool bracketedPasteOk = VtCatalogHelpers.TryToggleDecMode(probe, 2004, out _, out _, out _, out _);
                if (bracketedPasteOk)
                {
                    passedChecks++;
                }

                lines.Add($"  Bracketed paste mode toggle: {(bracketedPasteOk ? "ok" : "fail")}");

                totalChecks++;
                probe.Send("\x1b[?25h\x1b[?1049l");
                bool altScreenOff = !probe.Processor.ModeState.AlternateScreen && probe.Processor.ModeState.CursorVisible;
                if (altScreenOff)
                {
                    passedChecks++;
                }

                lines.Add($"  Alt-screen restore: {(altScreenOff ? "ok" : "fail")}");

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

internal sealed class TuiAppCompatibilityCatalogScenario : ICatalogScenario
{
    private const string HasMcToken = "__RT_HAS_MC__";
    private const string NoMcToken = "__RT_NO_MC__";
    private const string HasBtopToken = "__RT_HAS_BTOP__";
    private const string NoBtopToken = "__RT_NO_BTOP__";
    private const string ScriptDoneToken = "__RT_TUI_SCRIPT_DONE__";

    public string Title => "TUI app compatibility probes";

    public string Description => "Optional mc/btop probes plus ncurses-style mode script in a real PTY.";

    public bool IncludeInFullSweep => true;

    public CatalogScenarioResult Execute()
    {
        List<string> lines = [];
        int totalChecks = 0;
        int passedChecks = 0;

        if (OperatingSystem.IsWindows())
        {
            lines.Add("Windows host detected: mc/btop shell probes are skipped (unix-oriented checks).");
            lines.Add("Summary: 0/0 checks passed.");
            return new CatalogScenarioResult(Title, true, lines);
        }

        string outputTail = string.Empty;
        string outputSnapshot = string.Empty;

        try
        {
            using IPty pty = new DefaultPtyFactory().Create();
            using ManualResetEventSlim scriptDone = new(initialState: false);
            using ManualResetEventSlim processExit = new(initialState: false);
            StringBuilder outputBuffer = new();
            int exitCode = int.MinValue;

            pty.DataReceived += (buffer, count) =>
            {
                string chunk = Encoding.UTF8.GetString(buffer, 0, count);
                lock (outputBuffer)
                {
                    outputBuffer.Append(chunk);
                    outputSnapshot = outputBuffer.ToString();
                    if (outputSnapshot.Contains(ScriptDoneToken, StringComparison.Ordinal))
                    {
                        scriptDone.Set();
                    }

                    outputTail = outputSnapshot.Length <= 500
                        ? outputSnapshot
                        : outputSnapshot[^500..];
                }
            };

            pty.ProcessExited += code =>
            {
                exitCode = code;
                processExit.Set();
            };

            Dictionary<string, string> environment = new(StringComparer.Ordinal)
            {
                ["TERM"] = "xterm-256color",
            };

            pty.Start(
                shell: "/bin/sh",
                columns: 100,
                rows: 30,
                workingDirectory: Environment.CurrentDirectory,
                environment: environment,
                arguments: null);

            pty.Write($"command -v mc >/dev/null 2>&1 && echo {HasMcToken} || echo {NoMcToken}\r");
            pty.Write($"command -v btop >/dev/null 2>&1 && echo {HasBtopToken} || echo {NoBtopToken}\r");
            pty.Write("if command -v mc >/dev/null 2>&1; then mc --version | head -n 1; fi\r");
            pty.Write("if command -v btop >/dev/null 2>&1; then btop --version | head -n 1; fi\r");
            pty.Write("printf '\\033[?1049h\\033[?25l\\033[?1002h\\033[?1006h\\033[?2004h\\033[?1004h'\r");
            pty.Write("printf 'TUI_STYLE_PROBE\\n'\r");
            pty.Write("printf '\\033[?1004l\\033[?2004l\\033[?1006l\\033[?1002l\\033[?25h\\033[?1049l'\r");
            pty.Write($"echo {ScriptDoneToken}\r");
            pty.Write("exit\r");

            _ = scriptDone.Wait(TimeSpan.FromSeconds(8));
            bool exitObserved = processExit.Wait(TimeSpan.FromSeconds(8));

            totalChecks++;
            bool detectionTokensOk =
                (outputSnapshot.Contains(HasMcToken, StringComparison.Ordinal) || outputSnapshot.Contains(NoMcToken, StringComparison.Ordinal)) &&
                (outputSnapshot.Contains(HasBtopToken, StringComparison.Ordinal) || outputSnapshot.Contains(NoBtopToken, StringComparison.Ordinal));
            if (detectionTokensOk)
            {
                passedChecks++;
            }

            lines.Add($"mc/btop detection tokens: {(detectionTokensOk ? "ok" : "fail")}");

            bool hasMc = outputSnapshot.Contains(HasMcToken, StringComparison.Ordinal);
            bool hasBtop = outputSnapshot.Contains(HasBtopToken, StringComparison.Ordinal);
            lines.Add($"  mc available: {hasMc}");
            lines.Add($"  btop available: {hasBtop}");

            bool styleScriptOk =
                outputSnapshot.Contains("\x1b[?1049h", StringComparison.Ordinal) &&
                outputSnapshot.Contains("\x1b[?1002h", StringComparison.Ordinal) &&
                outputSnapshot.Contains("\x1b[?1006h", StringComparison.Ordinal) &&
                outputSnapshot.Contains("\x1b[?2004h", StringComparison.Ordinal) &&
                outputSnapshot.Contains("\x1b[?1004h", StringComparison.Ordinal) &&
                outputSnapshot.Contains("TUI_STYLE_PROBE", StringComparison.Ordinal) &&
                outputSnapshot.Contains(ScriptDoneToken, StringComparison.Ordinal);
            lines.Add($"ncurses-style mode script: {(styleScriptOk ? "ok" : "warn")}");

            bool mcVersionOk = !hasMc ||
                               outputSnapshot.Contains("midnight commander", StringComparison.OrdinalIgnoreCase) ||
                               outputSnapshot.Contains("GNU Midnight Commander", StringComparison.Ordinal);
            lines.Add($"mc version probe: {(mcVersionOk ? "ok" : "warn")} {(hasMc ? "(installed)" : "(not installed)")}");

            bool btopVersionOk = !hasBtop || outputSnapshot.Contains("btop", StringComparison.OrdinalIgnoreCase);
            lines.Add($"btop version probe: {(btopVersionOk ? "ok" : "warn")} {(hasBtop ? "(installed)" : "(not installed)")}");

            totalChecks++;
            bool processExitOk = exitObserved && exitCode == 0;
            if (processExitOk)
            {
                passedChecks++;
            }

            lines.Add($"PTY exit status: {(processExitOk ? "ok" : "fail")} code={exitCode}");
            lines.Add($"Output tail: {ControlTextFormatter.FormatControl(outputTail)}");
        }
        catch (Exception ex)
        {
            lines.Add($"TUI app compatibility failed: {ex.GetType().Name}: {ex.Message}");
            lines.Add("Summary: compatibility probe is non-blocking in this environment.");
            return new CatalogScenarioResult(Title, true, lines);
        }

        bool success = true;
        lines.Add($"Summary: {passedChecks}/{totalChecks} checks passed (non-blocking compatibility probe).");
        return new CatalogScenarioResult(Title, success, lines);
    }
}
