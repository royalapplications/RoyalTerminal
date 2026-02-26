// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;

namespace RoyalTerminal.ControlCatalog;

internal sealed class InteractiveLiveInputPlaygroundScenario : ICatalogScenario
{
    private const int MinWidth = 20;
    private const int MaxWidth = 80;
    private const int MinHeight = 8;
    private const int MaxHeight = 24;

    public string Title => "Interactive live input playground";

    public string Description => "Rendered TUI playground with live cursor/mouse/keyboard/window interaction simulation.";

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

        bool cursorVisible = true;
        bool applicationCursorKeys = false;
        bool applicationKeypad = false;
        bool bracketedPaste = false;
        bool focusEvents = false;
        bool mouseMode = false;
        int kittyFlags = 0;

        string cursorReport = BuildCursorReport(cursorColumn, cursorRow);
        string windowCellReport = BuildWindowCellReport(width, height);
        string windowPixelReport = BuildWindowPixelReport(width, height);
        string windowCellPixelReport = BuildWindowCellPixelReport();

        try
        {
            bool running = true;
            while (running)
            {
                RenderInteractiveFrame(
                    width,
                    height,
                    cursorColumn,
                    cursorRow,
                    actionCount,
                    lastAction,
                    lastMouseSequence,
                    lastWheelSequence,
                    cursorReport,
                    windowCellReport,
                    windowPixelReport,
                    windowCellPixelReport,
                    cursorVisible,
                    applicationCursorKeys,
                    applicationKeypad,
                    bracketedPaste,
                    focusEvents,
                    mouseMode,
                    kittyFlags);

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
                    windowCellReport = BuildWindowCellReport(width, height);
                    windowPixelReport = BuildWindowPixelReport(width, height);
                    windowCellPixelReport = BuildWindowCellPixelReport();
                    cursorReport = BuildCursorReport(cursorColumn, cursorRow);
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
                    windowCellReport = BuildWindowCellReport(width, height);
                    windowPixelReport = BuildWindowPixelReport(width, height);
                    windowCellPixelReport = BuildWindowCellPixelReport();
                    cursorReport = BuildCursorReport(cursorColumn, cursorRow);
                    lastAction = "window shrink";
                    actionCount++;
                    continue;
                }

                switch (key.Key)
                {
                    case ConsoleKey.UpArrow:
                        cursorRow = Math.Max(0, cursorRow - 1);
                        cursorReport = BuildCursorReport(cursorColumn, cursorRow);
                        lastAction = "cursor up";
                        actionCount++;
                        continue;

                    case ConsoleKey.DownArrow:
                        cursorRow = Math.Min(height - 1, cursorRow + 1);
                        cursorReport = BuildCursorReport(cursorColumn, cursorRow);
                        lastAction = "cursor down";
                        actionCount++;
                        continue;

                    case ConsoleKey.LeftArrow:
                        cursorColumn = Math.Max(0, cursorColumn - 1);
                        cursorReport = BuildCursorReport(cursorColumn, cursorRow);
                        lastAction = "cursor left";
                        actionCount++;
                        continue;

                    case ConsoleKey.RightArrow:
                        cursorColumn = Math.Min(width - 1, cursorColumn + 1);
                        cursorReport = BuildCursorReport(cursorColumn, cursorRow);
                        lastAction = "cursor right";
                        actionCount++;
                        continue;

                    case ConsoleKey.M:
                        mouseMode = !mouseMode;
                        lastAction = mouseMode ? "mouse mode enabled" : "mouse mode disabled";
                        actionCount++;
                        continue;

                    case ConsoleKey.Spacebar:
                    {
                        string clickSequence = TuiRuntimeHelpers.EncodeSgrMouseButton(
                            buttonCode: 0,
                            column: cursorColumn + 1,
                            row: cursorRow + 1,
                            release: false);
                        lastMouseSequence = mouseMode
                            ? ControlTextFormatter.FormatControl(clickSequence)
                            : "<suppressed>";
                        lastAction = "mouse click";
                        actionCount++;
                        continue;
                    }

                    case ConsoleKey.PageUp:
                    case ConsoleKey.PageDown:
                    {
                        bool up = key.Key == ConsoleKey.PageUp;
                        string wheelSequence = TuiRuntimeHelpers.EncodeSgrMouseWheel(
                            up,
                            column: cursorColumn + 1,
                            row: cursorRow + 1,
                            control: true);
                        lastWheelSequence = mouseMode
                            ? ControlTextFormatter.FormatControl(wheelSequence)
                            : "<suppressed>";
                        lastAction = up ? "wheel up" : "wheel down";
                        actionCount++;
                        continue;
                    }

                    case ConsoleKey.C:
                        cursorVisible = !cursorVisible;
                        lastAction = cursorVisible ? "cursor shown" : "cursor hidden";
                        actionCount++;
                        continue;

                    case ConsoleKey.A:
                        applicationCursorKeys = !applicationCursorKeys;
                        lastAction = applicationCursorKeys ? "app-cursor enabled" : "app-cursor disabled";
                        actionCount++;
                        continue;

                    case ConsoleKey.P:
                        applicationKeypad = !applicationKeypad;
                        lastAction = applicationKeypad ? "app-keypad enabled" : "app-keypad disabled";
                        actionCount++;
                        continue;

                    case ConsoleKey.B:
                        bracketedPaste = !bracketedPaste;
                        lastAction = bracketedPaste ? "bracketed paste enabled" : "bracketed paste disabled";
                        actionCount++;
                        continue;

                    case ConsoleKey.F:
                        focusEvents = !focusEvents;
                        lastAction = focusEvents ? "focus events enabled" : "focus events disabled";
                        actionCount++;
                        continue;

                    case ConsoleKey.K:
                        kittyFlags = kittyFlags == 0 ? 5 : 4;
                        lastAction = $"kitty flags={kittyFlags} rsp={ControlTextFormatter.FormatControl($"\\x1b[?{kittyFlags}u")}";
                        actionCount++;
                        continue;

                    case ConsoleKey.R:
                        cursorReport = BuildCursorReport(cursorColumn, cursorRow);
                        windowCellReport = BuildWindowCellReport(width, height);
                        windowPixelReport = BuildWindowPixelReport(width, height);
                        windowCellPixelReport = BuildWindowCellPixelReport();
                        lastAction = "window/cursor refreshed";
                        actionCount++;
                        continue;
                }
            }

            lines.Add($"Interactive session completed. actions={actionCount}");
            lines.Add($"Final board: {width}x{height} cursor=({cursorColumn + 1},{cursorRow + 1})");
            lines.Add(
                $"Final modes: cursorVisible={cursorVisible} " +
                $"appCursor={applicationCursorKeys} " +
                $"appKeypad={applicationKeypad} " +
                $"bracketedPaste={bracketedPaste} focusEvents={focusEvents} mouseMode={mouseMode} kittyFlags={kittyFlags}");
            lines.Add($"Last mouse sequence: {lastMouseSequence}");
            lines.Add($"Last wheel sequence: {lastWheelSequence}");
            lines.Add($"Cursor report: {cursorReport}");
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
        int width,
        int height,
        int cursorColumn,
        int cursorRow,
        int actionCount,
        string lastAction,
        string lastMouseSequence,
        string lastWheelSequence,
        string cursorReport,
        string windowCellReport,
        string windowPixelReport,
        string windowCellPixelReport,
        bool cursorVisible,
        bool applicationCursorKeys,
        bool applicationKeypad,
        bool bracketedPaste,
        bool focusEvents,
        bool mouseMode,
        int kittyFlags)
    {
        StringBuilder frame = new(capacity: 8_192);
        frame.Append("\x1b[H\x1b[2J");
        frame.AppendLine("\x1b[1;35mInteractive live input playground\x1b[0m");
        frame.AppendLine("Arrows move cursor | M mouse mode | Space click | PgUp/PgDn wheel | C cursor | A app-cursor | P keypad");
        frame.AppendLine("B bracketed-paste | F focus-events | K kitty | +/- resize board | R refresh reports | Enter/Q/Esc exit");
        frame.AppendLine();

        frame.AppendLine($"Board: {width}x{height}  Cursor: ({cursorColumn + 1},{cursorRow + 1})  Actions: {actionCount}");
        frame.AppendLine(
            $"Modes: cursorVisible={cursorVisible} appCursor={applicationCursorKeys} " +
            $"appKeypad={applicationKeypad} bracketedPaste={bracketedPaste} focusEvents={focusEvents} mouseMode={mouseMode} kittyFlags={kittyFlags}");
        frame.AppendLine($"Last action : {lastAction}");
        frame.AppendLine($"Last mouse  : {lastMouseSequence}");
        frame.AppendLine($"Last wheel  : {lastWheelSequence}");
        frame.AppendLine($"Cursor DSR  : {cursorReport}");
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
                    frame.Append(cursorVisible
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

    private static string BuildCursorReport(int cursorColumn, int cursorRow)
    {
        return ControlTextFormatter.FormatControl(TuiRuntimeHelpers.CursorReport(cursorRow + 1, cursorColumn + 1));
    }

    private static string BuildWindowCellReport(int columns, int rows)
    {
        return ControlTextFormatter.FormatControl(TuiRuntimeHelpers.WindowCellReport(rows, columns));
    }

    private static string BuildWindowPixelReport(int columns, int rows)
    {
        return ControlTextFormatter.FormatControl(TuiRuntimeHelpers.WindowPixelReport(rows * 20, columns * 10));
    }

    private static string BuildWindowCellPixelReport()
    {
        return ControlTextFormatter.FormatControl(TuiRuntimeHelpers.CellPixelReport(cellHeightPx: 20, cellWidthPx: 10));
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
