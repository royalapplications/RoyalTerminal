// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Terminal;

namespace RoyalTerminal.ControlCatalog;

internal sealed class InteractiveLiveInputPlaygroundScenario : ICatalogScenario
{
    private const int MinWidth = 20;
    private const int MaxWidth = 80;
    private const int MinHeight = 8;
    private const int MaxHeight = 24;

    public string Title => "Interactive live input playground";

    public string Description => "Rendered playground where users interact with mouse/cursor/keyboard/window features.";

    public bool IncludeInFullSweep => false;

    public CatalogScenarioResult Execute()
    {
        if (Console.IsInputRedirected || Console.IsOutputRedirected)
        {
            return new CatalogScenarioResult(
                Title,
                true,
                ["Interactive playground is available only in an interactive terminal session."]);
        }

        List<string> lines = [];
        bool success = true;
        int width = 40;
        int height = 14;
        int cursorColumn = width / 2;
        int cursorRow = height / 2;
        int actionCount = 0;
        string lastAction = "initialized";
        string lastMouseSequence = "<none>";
        string lastWheelSequence = "<none>";
        string lastCursorReport = "<none>";
        string windowCellReport = "<none>";
        string windowPixelReport = "<none>";
        string windowCellPixelReport = "<none>";

        try
        {
            using VtProcessorProbe probe = VtProcessorProbe.CreateManaged(columns: 120, rows: 40);
            TerminalMouseModeTracker mouseTracker = new();

            UpdateWindowReports(
                probe,
                width,
                height,
                windowCellReport,
                windowPixelReport,
                windowCellPixelReport,
                out windowCellReport,
                out windowPixelReport,
                out windowCellPixelReport);
            UpdateCursorReport(probe, cursorColumn, cursorRow, out lastCursorReport);

            bool running = true;
            while (running)
            {
                RenderInteractiveFrame(
                    probe,
                    mouseTracker,
                    width,
                    height,
                    cursorColumn,
                    cursorRow,
                    actionCount,
                    lastAction,
                    lastMouseSequence,
                    lastWheelSequence,
                    lastCursorReport,
                    windowCellReport,
                    windowPixelReport,
                    windowCellPixelReport);

                ConsoleKeyInfo key = Console.ReadKey(intercept: true);

                if (key.Key is ConsoleKey.Enter or ConsoleKey.Escape or ConsoleKey.Q)
                {
                    running = false;
                    lastAction = key.Key == ConsoleKey.Enter ? "finished" : "quit";
                    continue;
                }

                if (IsGrowKey(key))
                {
                    width = Math.Min(MaxWidth, width + 2);
                    height = Math.Min(MaxHeight, height + 1);
                    cursorColumn = Math.Min(cursorColumn, width - 1);
                    cursorRow = Math.Min(cursorRow, height - 1);
                    UpdateWindowReports(
                        probe,
                        width,
                        height,
                        windowCellReport,
                        windowPixelReport,
                        windowCellPixelReport,
                        out windowCellReport,
                        out windowPixelReport,
                        out windowCellPixelReport);
                    lastAction = "window grow";
                    actionCount++;
                    continue;
                }

                if (IsShrinkKey(key))
                {
                    width = Math.Max(MinWidth, width - 2);
                    height = Math.Max(MinHeight, height - 1);
                    cursorColumn = Math.Min(cursorColumn, width - 1);
                    cursorRow = Math.Min(cursorRow, height - 1);
                    UpdateWindowReports(
                        probe,
                        width,
                        height,
                        windowCellReport,
                        windowPixelReport,
                        windowCellPixelReport,
                        out windowCellReport,
                        out windowPixelReport,
                        out windowCellPixelReport);
                    lastAction = "window shrink";
                    actionCount++;
                    continue;
                }

                switch (key.Key)
                {
                    case ConsoleKey.UpArrow:
                        cursorRow = Math.Max(0, cursorRow - 1);
                        UpdateCursorReport(probe, cursorColumn, cursorRow, out lastCursorReport);
                        lastAction = "cursor up";
                        actionCount++;
                        continue;

                    case ConsoleKey.DownArrow:
                        cursorRow = Math.Min(height - 1, cursorRow + 1);
                        UpdateCursorReport(probe, cursorColumn, cursorRow, out lastCursorReport);
                        lastAction = "cursor down";
                        actionCount++;
                        continue;

                    case ConsoleKey.LeftArrow:
                        cursorColumn = Math.Max(0, cursorColumn - 1);
                        UpdateCursorReport(probe, cursorColumn, cursorRow, out lastCursorReport);
                        lastAction = "cursor left";
                        actionCount++;
                        continue;

                    case ConsoleKey.RightArrow:
                        cursorColumn = Math.Min(width - 1, cursorColumn + 1);
                        UpdateCursorReport(probe, cursorColumn, cursorRow, out lastCursorReport);
                        lastAction = "cursor right";
                        actionCount++;
                        continue;

                    case ConsoleKey.M:
                    {
                        bool enableMouse = mouseTracker.ModeState.TrackingMode == TerminalMouseTrackingMode.None;
                        _ = mouseTracker.Process(enableMouse ? "\x1b[?1003h\x1b[?1006h"u8 : "\x1b[?1003l\x1b[?1006l"u8);
                        lastAction = enableMouse ? "mouse mode enabled" : "mouse mode disabled";
                        actionCount++;
                        continue;
                    }

                    case ConsoleKey.Spacebar:
                    {
                        TerminalPointerEvent clickEvent = new(
                            Kind: TerminalPointerEventKind.Button,
                            X: 0,
                            Y: 0,
                            Button: TerminalMouseButton.Left,
                            Action: TerminalInputAction.Press,
                            Modifiers: TerminalModifiers.None);
                        lastMouseSequence = TerminalMouseProtocolEncoder.TryEncode(
                            clickEvent,
                            mouseTracker.ModeState,
                            column: cursorColumn + 1,
                            row: cursorRow + 1,
                            out byte[] clickSequence)
                            ? ControlTextFormatter.FormatControl(Encoding.ASCII.GetString(clickSequence))
                            : "<suppressed>";
                        lastAction = "mouse click";
                        actionCount++;
                        continue;
                    }

                    case ConsoleKey.PageUp:
                    case ConsoleKey.PageDown:
                    {
                        double deltaY = key.Key == ConsoleKey.PageUp ? 1d : -1d;
                        TerminalPointerEvent wheelEvent = new(
                            Kind: TerminalPointerEventKind.Scroll,
                            X: 0,
                            Y: 0,
                            Button: TerminalMouseButton.None,
                            Action: TerminalInputAction.Press,
                            Modifiers: TerminalModifiers.Control,
                            DeltaX: 0,
                            DeltaY: deltaY);
                        lastWheelSequence = TerminalMouseProtocolEncoder.TryEncode(
                            wheelEvent,
                            mouseTracker.ModeState,
                            column: cursorColumn + 1,
                            row: cursorRow + 1,
                            out byte[] wheelSequence)
                            ? ControlTextFormatter.FormatControl(Encoding.ASCII.GetString(wheelSequence))
                            : "<suppressed>";
                        lastAction = deltaY > 0 ? "wheel up" : "wheel down";
                        actionCount++;
                        continue;
                    }

                    case ConsoleKey.C:
                    {
                        bool nextVisible = !probe.Processor.ModeState.CursorVisible;
                        probe.Send(nextVisible ? "\x1b[?25h" : "\x1b[?25l");
                        lastAction = nextVisible ? "cursor shown" : "cursor hidden";
                        actionCount++;
                        continue;
                    }

                    case ConsoleKey.A:
                    {
                        bool enableAppCursor = !probe.Processor.ModeState.ApplicationCursorKeys;
                        probe.Send(enableAppCursor ? "\x1b[?1h" : "\x1b[?1l");
                        lastAction = enableAppCursor ? "app-cursor enabled" : "app-cursor disabled";
                        actionCount++;
                        continue;
                    }

                    case ConsoleKey.P:
                    {
                        bool enableAppKeypad = !probe.Processor.ModeState.ApplicationKeypad;
                        probe.Send(enableAppKeypad ? "\x1b=" : "\x1b>");
                        lastAction = enableAppKeypad ? "app-keypad enabled" : "app-keypad disabled";
                        actionCount++;
                        continue;
                    }

                    case ConsoleKey.B:
                    {
                        bool enableBracketedPaste = !probe.Processor.ModeState.BracketedPaste;
                        probe.Send(enableBracketedPaste ? "\x1b[?2004h" : "\x1b[?2004l");
                        lastAction = enableBracketedPaste ? "bracketed paste enabled" : "bracketed paste disabled";
                        actionCount++;
                        continue;
                    }

                    case ConsoleKey.F:
                    {
                        bool focusEnabled = probe.Processor is ITerminalFocusEventModeSource focusSource && focusSource.FocusEventsEnabled;
                        probe.Send(focusEnabled ? "\x1b[?1004l" : "\x1b[?1004h");
                        lastAction = focusEnabled ? "focus events disabled" : "focus events enabled";
                        actionCount++;
                        continue;
                    }

                    case ConsoleKey.K:
                    {
                        if (probe.Processor is IKittyKeyboardStateSource kittySource)
                        {
                            if (kittySource.KittyKeyboardFlags == 0)
                            {
                                probe.Send("\x1b[=1;1u");
                                probe.Send("\x1b[=4;2u");
                            }
                            else
                            {
                                probe.Send("\x1b[=1;3u");
                                probe.Send("\x1b[=4;3u");
                            }

                            if (VtCatalogHelpers.TrySingleResponse(
                                probe,
                                "\x1b[?u",
                                static response => response.StartsWith("\x1b[?", StringComparison.Ordinal) && response.EndsWith("u", StringComparison.Ordinal),
                                out string kittyResponse))
                            {
                                lastAction = $"kitty flags={kittySource.KittyKeyboardFlags} rsp={ControlTextFormatter.FormatControl(kittyResponse)}";
                            }
                            else
                            {
                                lastAction = $"kitty flags={kittySource.KittyKeyboardFlags}";
                            }
                        }
                        else
                        {
                            lastAction = "kitty keyboard unsupported";
                        }

                        actionCount++;
                        continue;
                    }

                    case ConsoleKey.R:
                        UpdateWindowReports(
                            probe,
                            width,
                            height,
                            windowCellReport,
                            windowPixelReport,
                            windowCellPixelReport,
                            out windowCellReport,
                            out windowPixelReport,
                            out windowCellPixelReport);
                        UpdateCursorReport(probe, cursorColumn, cursorRow, out lastCursorReport);
                        lastAction = "window/cursor refreshed";
                        actionCount++;
                        continue;
                }
            }

            lines.Add($"Interactive session completed. actions={actionCount}");
            lines.Add($"Final board: {width}x{height} cursor=({cursorColumn + 1},{cursorRow + 1})");
            lines.Add(
                $"Final modes: cursorVisible={probe.Processor.ModeState.CursorVisible} " +
                $"appCursor={probe.Processor.ModeState.ApplicationCursorKeys} " +
                $"appKeypad={probe.Processor.ModeState.ApplicationKeypad} " +
                $"bracketedPaste={probe.Processor.ModeState.BracketedPaste}");
            lines.Add($"Mouse tracker: {mouseTracker.ModeState}");
            lines.Add($"Last mouse sequence: {lastMouseSequence}");
            lines.Add($"Last wheel sequence: {lastWheelSequence}");
            lines.Add($"Cursor report: {lastCursorReport}");
            lines.Add($"Window 18t: {windowCellReport}");
            lines.Add($"Window 14t: {windowPixelReport}");
            lines.Add($"Window 16t: {windowCellPixelReport}");
        }
        catch (Exception ex)
        {
            success = false;
            lines.Add($"Interactive playground failed: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            Console.Write("\x1b[0m");
        }

        return new CatalogScenarioResult(Title, success, lines);
    }

    private static void RenderInteractiveFrame(
        VtProcessorProbe probe,
        TerminalMouseModeTracker mouseTracker,
        int width,
        int height,
        int cursorColumn,
        int cursorRow,
        int actionCount,
        string lastAction,
        string lastMouseSequence,
        string lastWheelSequence,
        string lastCursorReport,
        string windowCellReport,
        string windowPixelReport,
        string windowCellPixelReport)
    {
        bool focusEventsEnabled = probe.Processor is ITerminalFocusEventModeSource focusSource && focusSource.FocusEventsEnabled;
        int kittyFlags = probe.Processor is IKittyKeyboardStateSource kittySource ? kittySource.KittyKeyboardFlags : -1;

        StringBuilder frame = new(capacity: 8_192);
        frame.Append("\x1b[H\x1b[2J");
        frame.AppendLine("\x1b[1;35mInteractive live input playground\x1b[0m");
        frame.AppendLine("Arrows move cursor | M mouse mode | Space click | PgUp/PgDn wheel | C cursor | A app-cursor | P keypad");
        frame.AppendLine("B bracketed-paste | F focus-events | K kitty | +/- resize board | R refresh reports | Enter/Q/Esc exit");
        frame.AppendLine();

        frame.AppendLine($"Board: {width}x{height}  Cursor: ({cursorColumn + 1},{cursorRow + 1})  Actions: {actionCount}");
        frame.AppendLine(
            $"Modes: cursorVisible={probe.Processor.ModeState.CursorVisible} appCursor={probe.Processor.ModeState.ApplicationCursorKeys} " +
            $"appKeypad={probe.Processor.ModeState.ApplicationKeypad} bracketedPaste={probe.Processor.ModeState.BracketedPaste} " +
            $"focusEvents={focusEventsEnabled} kittyFlags={kittyFlags}");
        frame.AppendLine($"Mouse tracker: {mouseTracker.ModeState}");
        frame.AppendLine($"Last action : {lastAction}");
        frame.AppendLine($"Last mouse  : {lastMouseSequence}");
        frame.AppendLine($"Last wheel  : {lastWheelSequence}");
        frame.AppendLine($"Cursor DSR  : {lastCursorReport}");
        frame.AppendLine($"Window 18t  : {windowCellReport}");
        frame.AppendLine($"Window 14t  : {windowPixelReport}");
        frame.AppendLine($"Window 16t  : {windowCellPixelReport}");
        frame.AppendLine();

        frame.Append('┌').Append(new string('─', width)).AppendLine("┐");
        for (int row = 0; row < height; row++)
        {
            frame.Append('│');
            for (int column = 0; column < width; column++)
            {
                if (row == cursorRow && column == cursorColumn)
                {
                    frame.Append(probe.Processor.ModeState.CursorVisible
                        ? "\x1b[30;46m@\x1b[0m"
                        : "\x1b[30;47mx\x1b[0m");
                    continue;
                }

                frame.Append((row + column) % 8 == 0 ? '·' : ' ');
            }

            frame.AppendLine("│");
        }

        frame.Append('└').Append(new string('─', width)).AppendLine("┘");
        Console.Write(frame.ToString());
    }

    private static void UpdateCursorReport(VtProcessorProbe probe, int cursorColumn, int cursorRow, out string cursorReport)
    {
        probe.Send($"\x1b[{cursorRow + 1};{cursorColumn + 1}H");
        bool reportOk = VtCatalogHelpers.TrySingleResponse(
            probe,
            "\x1b[6n",
            static response => response.StartsWith("\x1b[", StringComparison.Ordinal) && response.EndsWith("R", StringComparison.Ordinal),
            out string cursorResponse);
        cursorReport = reportOk
            ? ControlTextFormatter.FormatControl(cursorResponse)
            : "<no response>";
    }

    private static void UpdateWindowReports(
        VtProcessorProbe probe,
        int columns,
        int rows,
        string previousCellReport,
        string previousPixelReport,
        string previousCellPixelReport,
        out string cellReport,
        out string pixelReport,
        out string cellPixelReport)
    {
        probe.NotifyResize(columns, rows, widthPx: columns * 10, heightPx: rows * 20);

        bool cellOk = VtCatalogHelpers.TrySingleResponse(
            probe,
            "\x1b[18t",
            static response => response.StartsWith("\x1b[8;", StringComparison.Ordinal) && response.EndsWith("t", StringComparison.Ordinal),
            out string cellResponse);
        cellReport = cellOk
            ? ControlTextFormatter.FormatControl(cellResponse)
            : previousCellReport;

        bool pixelOk = VtCatalogHelpers.TrySingleResponse(
            probe,
            "\x1b[14t",
            static response => response.StartsWith("\x1b[4;", StringComparison.Ordinal) && response.EndsWith("t", StringComparison.Ordinal),
            out string pixelResponse);
        pixelReport = pixelOk
            ? ControlTextFormatter.FormatControl(pixelResponse)
            : previousPixelReport;

        bool cellPixelOk = VtCatalogHelpers.TrySingleResponse(
            probe,
            "\x1b[16t",
            static response => response.StartsWith("\x1b[6;", StringComparison.Ordinal) && response.EndsWith("t", StringComparison.Ordinal),
            out string cellPixelResponse);
        cellPixelReport = cellPixelOk
            ? ControlTextFormatter.FormatControl(cellPixelResponse)
            : previousCellPixelReport;
    }

    private static bool IsGrowKey(ConsoleKeyInfo key)
    {
        return key.Key is ConsoleKey.Add or ConsoleKey.OemPlus || key.KeyChar == '+';
    }

    private static bool IsShrinkKey(ConsoleKeyInfo key)
    {
        return key.Key is ConsoleKey.Subtract or ConsoleKey.OemMinus || key.KeyChar == '-';
    }
}
