// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Tests — Cross-platform PTY contract tests.

using System.Text;
using System.Text.RegularExpressions;
using System.Diagnostics;
using System.Reflection;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

[CollectionDefinition("PtyContractTests", DisableParallelization = true)]
public sealed class PtyContractTestCollection
{
}

[Collection("PtyContractTests")]
public class PtyContractTests
{
    [Fact]
    public void UnixPty_BeforeStart_Operations_DoNotThrow()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        using UnixPty pty = new();
        pty.Write("echo test\n");
        pty.Write("x"u8.ToArray(), 0, 1);
        pty.Resize(100, 30);
        pty.Stop();
    }

    [Fact]
    public void WindowsPty_BeforeStart_Operations_DoNotThrow()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using WindowsPty pty = new();
        pty.Write("echo test\r\n");
        pty.Write("x"u8.ToArray(), 0, 1);
        pty.Resize(100, 30);
        pty.Stop();
    }

    [Fact]
    public void UnixPty_StartWriteReadExit_Contract()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        using UnixPty pty = new();
        using ManualResetEventSlim sawMarker = new(false);
        StringBuilder output = new();
        string marker = "__ROYALTERMINAL_UNIX_PTY_CONTRACT__";

        pty.DataReceived += (data, length) =>
        {
            string text = Encoding.UTF8.GetString(data, 0, length);
            lock (output)
            {
                output.Append(text);
                if (output.ToString().Contains(marker, StringComparison.Ordinal))
                {
                    sawMarker.Set();
                }
            }
        };
        pty.Start(shell: "/bin/sh", columns: 80, rows: 24, workingDirectory: Environment.CurrentDirectory);
        Assert.True(pty.IsRunning);

        pty.Write($"echo {marker}\nexit\n");

        Assert.True(sawMarker.Wait(TimeSpan.FromSeconds(10)),
            $"Did not observe PTY marker in output. Current output: {output}");
        pty.Stop();
        Assert.False(pty.IsRunning);
    }

    [Fact]
    public void UnixPty_StartWithArguments_ExecutesCommand()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        using UnixPty pty = new();
        using ManualResetEventSlim sawMarker = new(false);
        using ManualResetEventSlim sawExit = new(false);
        StringBuilder output = new();
        string marker = "__ROYALTERMINAL_UNIX_PTY_ARGS__";

        pty.DataReceived += (data, length) =>
        {
            string text = Encoding.UTF8.GetString(data, 0, length);
            lock (output)
            {
                output.Append(text);
                if (output.ToString().Contains(marker, StringComparison.Ordinal))
                {
                    sawMarker.Set();
                }
            }
        };

        pty.ProcessExited += _ => sawExit.Set();

        pty.Start(
            shell: "/bin/sh",
            columns: 80,
            rows: 24,
            workingDirectory: Environment.CurrentDirectory,
            arguments:
            [
                "-c",
                $"i=0; while [ $i -lt 5 ]; do printf '{marker}\\n'; i=$((i+1)); sleep 0.2; done",
            ]);

        Assert.True(
            sawMarker.Wait(TimeSpan.FromSeconds(10)),
            $"Did not observe argument-driven command output. Current output: {output}");
        Assert.True(sawExit.Wait(TimeSpan.FromSeconds(10)), "PTY child did not exit after argument-driven command.");
    }

    [Fact]
    public void WindowsPty_StartWriteReadExit_Contract()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using WindowsPty pty = new();
        using ManualResetEventSlim sawMarker = new(false);
        StringBuilder output = new();
        string marker = "__ROYALTERMINAL_WINDOWS_PTY_CONTRACT__";

        pty.DataReceived += (data, length) =>
        {
            string text = Encoding.UTF8.GetString(data, 0, length);
            lock (output)
            {
                output.Append(text);
                if (output.ToString().Contains(marker, StringComparison.Ordinal))
                {
                    sawMarker.Set();
                }
            }
        };
        pty.Start(shell: null, columns: 80, rows: 24, workingDirectory: Environment.CurrentDirectory);
        Assert.True(pty.IsRunning);

        pty.Write($"echo {marker}\r\nexit\r\n");

        Assert.True(sawMarker.Wait(TimeSpan.FromSeconds(10)),
            $"Did not observe PTY marker in output. Current output: {output}");
        pty.Stop();
        Assert.False(pty.IsRunning);
    }

    [Fact]
    public void WindowsPty_StartWithArguments_ExecutesCommand()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using WindowsPty pty = new();
        using ManualResetEventSlim sawMarker = new(false);
        using ManualResetEventSlim sawExit = new(false);
        StringBuilder output = new();
        string marker = "__ROYALTERMINAL_WINDOWS_PTY_ARGS__";

        pty.DataReceived += (data, length) =>
        {
            string text = Encoding.UTF8.GetString(data, 0, length);
            lock (output)
            {
                output.Append(text);
                if (output.ToString().Contains(marker, StringComparison.Ordinal))
                {
                    sawMarker.Set();
                }
            }
        };

        pty.ProcessExited += _ => sawExit.Set();

        pty.Start(
            shell: "cmd.exe",
            columns: 80,
            rows: 24,
            workingDirectory: Environment.CurrentDirectory,
            arguments:
            [
                "/c",
                $"echo {marker}",
            ]);

        Assert.True(
            sawMarker.Wait(TimeSpan.FromSeconds(10)),
            $"Did not observe argument-driven command output. Current output: {output}");
        Assert.True(sawExit.Wait(TimeSpan.FromSeconds(10)), "PTY child did not exit after argument-driven command.");
    }

    [Fact]
    public void UnixPty_Resize_UpdatesTerminalSize()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        using UnixPty pty = new();
        pty.Start(shell: "/bin/sh", columns: 80, rows: 24, workingDirectory: Environment.CurrentDirectory);

        string? slavePath = (string?)typeof(UnixPty)
            .GetField("_slavePtyPath", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(pty);
        Assert.False(string.IsNullOrWhiteSpace(slavePath));

        static bool TryParseSize(string text, out (int Rows, int Cols) size)
        {
            MatchCollection matches = Regex.Matches(text, @"(?<rows>\d+)\s+(?<cols>\d+)");
            if (matches.Count == 0)
            {
                size = default;
                return false;
            }

            Match match = matches[^1];
            size = (
                int.Parse(match.Groups["rows"].Value),
                int.Parse(match.Groups["cols"].Value));
            return true;
        }

        (int Rows, int Cols) QueryPtySize(string path)
        {
            string sttyPath = File.Exists("/bin/stty") ? "/bin/stty" : "stty";
            ProcessStartInfo startInfo = new(sttyPath)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            startInfo.ArgumentList.Add(OperatingSystem.IsMacOS() ? "-f" : "-F");
            startInfo.ArgumentList.Add(path);
            startInfo.ArgumentList.Add("size");

            using Process? process = Process.Start(startInfo);
            Assert.NotNull(process);
            Assert.True(process.WaitForExit(5000), "stty size command timed out.");

            string stdOut = process.StandardOutput.ReadToEnd();
            string stdErr = process.StandardError.ReadToEnd();
            Assert.Equal(0, process.ExitCode);
            Assert.True(
                TryParseSize(stdOut, out (int Rows, int Cols) parsed),
                $"Could not parse stty size output. stdout: {stdOut} stderr: {stdErr}");

            return parsed;
        }

        static (int Rows, int Cols) WaitForAnyNonZeroSize(
            Func<(int Rows, int Cols)> query,
            TimeSpan timeout)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            (int Rows, int Cols) last = default;
            while (DateTime.UtcNow < deadline)
            {
                last = query();
                if (last.Rows > 0 && last.Cols > 0)
                {
                    return last;
                }

                Thread.Sleep(50);
            }

            return last;
        }

        static (bool Matched, int Rows, int Cols) WaitForExpectedSize(
            Func<(int Rows, int Cols)> query,
            int expectedRows,
            int expectedCols,
            TimeSpan timeout)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            (int Rows, int Cols) last = default;
            while (DateTime.UtcNow < deadline)
            {
                last = query();
                if (last.Rows == expectedRows && last.Cols == expectedCols)
                {
                    return (true, last.Rows, last.Cols);
                }

                Thread.Sleep(50);
            }

            return (false, last.Rows, last.Cols);
        }

        (int initialRows, int initialCols) = WaitForAnyNonZeroSize(
            () => QueryPtySize(slavePath!),
            TimeSpan.FromSeconds(3));
        Assert.Equal(24, initialRows);
        Assert.Equal(80, initialCols);

        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            pty.Resize(140, 45);
            (bool matched, int resizedRows, int resizedCols) = WaitForExpectedSize(
                () => QueryPtySize(slavePath!),
                expectedRows: 45,
                expectedCols: 140,
                timeout: TimeSpan.FromSeconds(3));
            Assert.True(
                matched,
                $"PTY resize did not converge to expected size. Observed {resizedRows}x{resizedCols}.");
        }
    }
}
