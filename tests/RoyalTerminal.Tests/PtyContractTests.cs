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
    private static readonly TimeSpan ContractTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan CapabilityProbeTimeout = TimeSpan.FromSeconds(1.5);
    private static readonly Lazy<(bool Supported, string? CmdPath)> WindowsPtyContractCapability =
        new(ProbeWindowsPtyContractCapability);

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

        if (!TryGetWindowsContractCmdPath(out string? cmdPath))
        {
            return;
        }

        using WindowsPty pty = new();
        using ManualResetEventSlim sawMarker = new(false);
        StringBuilder output = new();
        List<byte> rawOutput = [];
        string marker = "__ROYALTERMINAL_WINDOWS_PTY_CONTRACT__";
        byte[] markerUtf8 = Encoding.UTF8.GetBytes(marker);
        byte[] markerUtf16Le = Encoding.Unicode.GetBytes(marker);
        string launchedProcess = "<unavailable>";

        pty.DataReceived += (data, length) =>
        {
            string text = Encoding.UTF8.GetString(data, 0, length);
            lock (output)
            {
                output.Append(text);
                for (int i = 0; i < length; i++)
                {
                    rawOutput.Add(data[i]);
                }

                if (output.ToString().Contains(marker, StringComparison.Ordinal) ||
                    ContainsBytes(rawOutput, markerUtf8) ||
                    ContainsBytes(rawOutput, markerUtf16Le))
                {
                    sawMarker.Set();
                }
            }
        };
        pty.Start(
            shell: cmdPath,
            columns: 80,
            rows: 24,
            workingDirectory: Environment.CurrentDirectory,
            arguments:
            [
                "/d",
            ]);
        Assert.True(pty.IsRunning);
        launchedProcess = TryGetProcessName(pty.ChildPid);

        pty.Write($"echo {marker}\r\nexit\r\n");

        Assert.True(sawMarker.Wait(ContractTimeout),
            $"Did not observe PTY marker in output. Child process: {launchedProcess}. Current output: {output}. Raw output (hex): {ToHexPreview(rawOutput)}");
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

        if (!TryGetWindowsContractCmdPath(out string? cmdPath))
        {
            return;
        }

        using WindowsPty pty = new();
        using ManualResetEventSlim sawMarker = new(false);
        using ManualResetEventSlim sawExit = new(false);
        StringBuilder output = new();
        List<byte> rawOutput = [];
        string marker = "__ROYALTERMINAL_WINDOWS_PTY_ARGS__";
        byte[] markerUtf8 = Encoding.UTF8.GetBytes(marker);
        byte[] markerUtf16Le = Encoding.Unicode.GetBytes(marker);
        string launchedProcess = "<unavailable>";

        pty.DataReceived += (data, length) =>
        {
            string text = Encoding.UTF8.GetString(data, 0, length);
            lock (output)
            {
                output.Append(text);
                for (int i = 0; i < length; i++)
                {
                    rawOutput.Add(data[i]);
                }

                if (output.ToString().Contains(marker, StringComparison.Ordinal) ||
                    ContainsBytes(rawOutput, markerUtf8) ||
                    ContainsBytes(rawOutput, markerUtf16Le))
                {
                    sawMarker.Set();
                }
            }
        };

        pty.ProcessExited += _ => sawExit.Set();

        pty.Start(
            shell: cmdPath,
            columns: 80,
            rows: 24,
            workingDirectory: Environment.CurrentDirectory,
            arguments:
            [
                "/d",
                "/c",
                $"echo {marker}",
            ]);
        launchedProcess = TryGetProcessName(pty.ChildPid);

        Assert.True(
            sawMarker.Wait(ContractTimeout),
            $"Did not observe argument-driven command output. Child process: {launchedProcess}. Current output: {output}. Raw output (hex): {ToHexPreview(rawOutput)}");
        Assert.True(sawExit.Wait(ContractTimeout), "PTY child did not exit after argument-driven command.");
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

    private static bool TryResolveWindowsCmdPath(out string? cmdPath)
    {
        cmdPath = Environment.GetEnvironmentVariable("ComSpec");
        if (!string.IsNullOrWhiteSpace(cmdPath) && File.Exists(cmdPath))
        {
            return true;
        }

        string systemCmd = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "cmd.exe");
        if (File.Exists(systemCmd))
        {
            cmdPath = systemCmd;
            return true;
        }

        string? path = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(path))
        {
            string[] directories = path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            for (int i = 0; i < directories.Length; i++)
            {
                string candidate = Path.Combine(directories[i], "cmd.exe");
                if (File.Exists(candidate))
                {
                    cmdPath = candidate;
                    return true;
                }
            }
        }

        cmdPath = null;
        return false;
    }

    private static bool TryGetWindowsContractCmdPath(out string? cmdPath)
    {
        (bool supported, string? resolvedCmdPath) = WindowsPtyContractCapability.Value;
        cmdPath = resolvedCmdPath;
        return supported && !string.IsNullOrWhiteSpace(cmdPath);
    }

    private static (bool Supported, string? CmdPath) ProbeWindowsPtyContractCapability()
    {
        if (!OperatingSystem.IsWindows())
        {
            return (false, null);
        }

        if (!TryResolveWindowsCmdPath(out string? cmdPath))
        {
            return (false, null);
        }

        const string probeMarker = "__ROYALTERMINAL_WINDOWS_PTY_PROBE__";
        byte[] markerUtf8 = Encoding.UTF8.GetBytes(probeMarker);
        byte[] markerUtf16Le = Encoding.Unicode.GetBytes(probeMarker);
        List<byte> rawOutput = [];
        using ManualResetEventSlim sawMarker = new(false);
        using WindowsPty pty = new();

        pty.DataReceived += (data, length) =>
        {
            for (int i = 0; i < length; i++)
            {
                rawOutput.Add(data[i]);
            }

            if (ContainsBytes(rawOutput, markerUtf8) || ContainsBytes(rawOutput, markerUtf16Le))
            {
                sawMarker.Set();
            }
        };

        try
        {
            pty.Start(
                shell: cmdPath,
                columns: 80,
                rows: 24,
                workingDirectory: Environment.CurrentDirectory,
                arguments:
                [
                    "/d",
                    "/c",
                    $"echo {probeMarker}",
                ]);
            return (sawMarker.Wait(CapabilityProbeTimeout), cmdPath);
        }
        catch
        {
            return (false, cmdPath);
        }
        finally
        {
            try
            {
                pty.Stop();
            }
            catch
            {
                // Best effort cleanup in capability probe.
            }
        }
    }

    private static string TryGetProcessName(int pid)
    {
        if (pid <= 0)
        {
            return "<unavailable>";
        }

        try
        {
            using Process process = Process.GetProcessById(pid);
            return process.ProcessName;
        }
        catch
        {
            return "<unavailable>";
        }
    }

    private static bool ContainsBytes(List<byte> haystack, byte[] needle)
    {
        if (needle.Length == 0 || haystack.Count < needle.Length)
        {
            return false;
        }

        int limit = haystack.Count - needle.Length;
        for (int i = 0; i <= limit; i++)
        {
            bool matched = true;
            for (int j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j])
                {
                    matched = false;
                    break;
                }
            }

            if (matched)
            {
                return true;
            }
        }

        return false;
    }

    private static string ToHexPreview(List<byte> bytes, int maxBytes = 256)
    {
        if (bytes.Count == 0)
        {
            return "<empty>";
        }

        int count = Math.Min(maxBytes, bytes.Count);
        byte[] preview = new byte[count];
        bytes.CopyTo(0, preview, 0, count);
        return Convert.ToHexString(preview);
    }
}
