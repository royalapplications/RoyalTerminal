// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Diagnostics;
using System.Text;

namespace RoyalTerminal.ControlCatalog;

internal readonly record struct TuiSequenceCheck(
    string Name,
    string Sequence,
    string ExpectedResult,
    bool Optional = false);

internal static class TuiRuntimeHelpers
{
    public static string Badge(bool ok)
    {
        return ok
            ? "\x1b[30;42m OK \x1b[0m"
            : "\x1b[37;41mFAIL\x1b[0m";
    }

    public static void AppendChecks(List<string> lines, string heading, IReadOnlyList<TuiSequenceCheck> checks)
    {
        lines.Add(heading);
        for (int i = 0; i < checks.Count; i++)
        {
            TuiSequenceCheck check = checks[i];
            string optional = check.Optional ? " (optional)" : string.Empty;
            lines.Add(
                $"  {check.Name,-30} {ControlTextFormatter.FormatControl(check.Sequence),-28} => {check.ExpectedResult}{optional}");
        }
    }

    public static string EncodeSgrMouseButton(int buttonCode, int column, int row, bool release)
    {
        char terminator = release ? 'm' : 'M';
        return $"\x1b[<{Math.Max(0, buttonCode)};{Math.Max(1, column)};{Math.Max(1, row)}{terminator}";
    }

    public static string EncodeSgrMouseMove(int buttonCode, int column, int row, bool shift)
    {
        int cb = Math.Max(0, buttonCode) + 32;
        if (shift)
        {
            cb += 4;
        }

        return EncodeSgrMouseButton(cb, column, row, release: false);
    }

    public static string EncodeSgrMouseWheel(bool up, int column, int row, bool control)
    {
        int cb = up ? 64 : 65;
        if (control)
        {
            cb += 16;
        }

        return EncodeSgrMouseButton(cb, column, row, release: false);
    }

    public static string CursorReport(int row, int column)
    {
        return $"\x1b[{Math.Max(1, row)};{Math.Max(1, column)}R";
    }

    public static string WindowCellReport(int rows, int columns)
    {
        return $"\x1b[8;{Math.Max(1, rows)};{Math.Max(1, columns)}t";
    }

    public static string WindowPixelReport(int heightPx, int widthPx)
    {
        return $"\x1b[4;{Math.Max(0, heightPx)};{Math.Max(0, widthPx)}t";
    }

    public static string CellPixelReport(int cellHeightPx, int cellWidthPx)
    {
        return $"\x1b[6;{Math.Max(1, cellHeightPx)};{Math.Max(1, cellWidthPx)}t";
    }

    public static bool TryFindCommand(string commandName, out string output)
    {
        output = string.Empty;
        if (string.IsNullOrWhiteSpace(commandName))
        {
            return false;
        }

        string probeCommand = OperatingSystem.IsWindows()
            ? $"where {commandName}"
            : $"command -v {commandName}";
        if (!TryRunShellCommand(probeCommand, TimeSpan.FromSeconds(3), out string stdOut, out _, out int exitCode))
        {
            return false;
        }

        output = stdOut.Trim();
        return exitCode == 0 && output.Length > 0;
    }

    public static bool TryRunShellCommand(
        string command,
        TimeSpan timeout,
        out string standardOutput,
        out string standardError,
        out int exitCode)
    {
        standardOutput = string.Empty;
        standardError = string.Empty;
        exitCode = int.MinValue;

        try
        {
            ProcessStartInfo startInfo = new()
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            if (OperatingSystem.IsWindows())
            {
                startInfo.FileName = "cmd.exe";
                startInfo.ArgumentList.Add("/c");
                startInfo.ArgumentList.Add(command);
            }
            else
            {
                startInfo.FileName = "/bin/sh";
                startInfo.ArgumentList.Add("-lc");
                startInfo.ArgumentList.Add(command);
            }

            using Process process = new() { StartInfo = startInfo };
            if (!process.Start())
            {
                return false;
            }

            Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
            Task<string> errorTask = process.StandardError.ReadToEndAsync();

            int timeoutMs = Math.Max(1000, (int)timeout.TotalMilliseconds);
            if (!process.WaitForExit(timeoutMs))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Best effort.
                }

                return false;
            }

            Task.WaitAll([outputTask, errorTask], timeoutMs);
            standardOutput = outputTask.Result;
            standardError = errorTask.Result;
            exitCode = process.ExitCode;
            return true;
        }
        catch (Exception ex)
        {
            standardError = ex.Message;
            return false;
        }
    }

    public static IReadOnlyList<string> ExtractInterestingLines(string output, params string[] prefixes)
    {
        List<string> lines = [];
        if (string.IsNullOrEmpty(output))
        {
            return lines;
        }

        string[] rawLines = output.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n');
        for (int i = 0; i < rawLines.Length; i++)
        {
            string line = rawLines[i];
            for (int prefixIndex = 0; prefixIndex < prefixes.Length; prefixIndex++)
            {
                if (line.StartsWith(prefixes[prefixIndex], StringComparison.Ordinal))
                {
                    lines.Add(line);
                    break;
                }
            }
        }

        return lines;
    }

    public static string BuildColorSwatch(uint argb)
    {
        int red = (int)((argb >> 16) & 0xFF);
        int green = (int)((argb >> 8) & 0xFF);
        int blue = (int)(argb & 0xFF);
        return $"\x1b[48;2;{red};{green};{blue}m   \x1b[0m";
    }
}
