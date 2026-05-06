// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Tests — Cross-platform PTY contract tests.

using System.Text;
using System.Text.RegularExpressions;
using System.Diagnostics;
using System.Reflection;
using RoyalTerminal.Avalonia.Rendering;
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

        string scriptDirectory = Path.Combine(Path.GetTempPath(), "royalterminal-tests");
        Directory.CreateDirectory(scriptDirectory);
        string scriptPath = Path.Combine(scriptDirectory, Guid.NewGuid().ToString("N") + ".sh");

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

        File.WriteAllText(
            scriptPath,
            $$"""
              marker="{{marker}}"
              i=0
              while [ "$i" -lt 5 ]; do
                  printf '%s\n' "$marker"
                  i=$((i+1))
                  sleep 0.2
              done
              """);

        try
        {
            pty.Start(
                shell: "/bin/sh",
                columns: 80,
                rows: 24,
                workingDirectory: Environment.CurrentDirectory,
                arguments:
                [
                    scriptPath,
                ]);

            Assert.True(
                sawMarker.Wait(TimeSpan.FromSeconds(15)),
                $"Did not observe argument-driven command output. Current output: {output}");
            Assert.True(sawExit.Wait(TimeSpan.FromSeconds(15)), "PTY child did not exit after argument-driven command.");
        }
        finally
        {
            if (File.Exists(scriptPath))
            {
                File.Delete(scriptPath);
            }
        }
    }

    [Fact]
    public void UnixPty_CtrlC_InterruptsBusyLoop_WithinLatencyBudget()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        const string readyMarker = "__ROYALTERMINAL_UNIX_BUSY_READY__";
        const string postInterruptMarker = "__ROYALTERMINAL_UNIX_BUSY_AFTER_CTRL_C__";
        const string floodNeedle = "busy-output";
        using UnixPty pty = new();
        using ManualResetEventSlim sawReady = new(false);
        using ManualResetEventSlim sawFlood = new(false);
        using ManualResetEventSlim sawPostInterrupt = new(false);
        object sync = new();
        StringBuilder output = new();

        pty.DataReceived += (data, length) =>
        {
            string text = Encoding.UTF8.GetString(data, 0, length);
            lock (sync)
            {
                output.Append(text);
                if (output.Length > 16384)
                {
                    output.Remove(0, output.Length - 8192);
                }

                string snapshot = output.ToString();
                if (!sawReady.IsSet && snapshot.Contains(readyMarker, StringComparison.Ordinal))
                {
                    sawReady.Set();
                }

                if (!sawFlood.IsSet && snapshot.Contains(floodNeedle, StringComparison.Ordinal))
                {
                    sawFlood.Set();
                }

                if (!sawPostInterrupt.IsSet && snapshot.Contains(postInterruptMarker, StringComparison.Ordinal))
                {
                    sawPostInterrupt.Set();
                }
            }
        };

        pty.Start(shell: "/bin/sh", columns: 80, rows: 24, workingDirectory: Environment.CurrentDirectory);

        pty.Write($"echo {readyMarker}\n");
        Assert.True(
            sawReady.Wait(TimeSpan.FromSeconds(5)),
            "Did not observe Unix PTY readiness marker before starting busy loop.");

        pty.Write("while :; do printf 'busy-output\\n'; done\n");
        Assert.True(
            sawFlood.Wait(TimeSpan.FromSeconds(5)),
            "Did not observe Unix PTY flood output before sending Ctrl+C.");

        Stopwatch interruptLatency = Stopwatch.StartNew();
        pty.Write(new byte[] { 0x03 }, 0, 1);
        pty.Write($"echo {postInterruptMarker}\n");

        bool interrupted = sawPostInterrupt.Wait(TimeSpan.FromSeconds(3));
        if (!interrupted)
        {
            string recentOutput;
            lock (sync)
            {
                recentOutput = output.ToString();
            }

            Assert.Fail($"Did not observe post-interrupt marker within timeout. Recent output: {recentOutput}");
        }

        Assert.True(
            interruptLatency.Elapsed < TimeSpan.FromSeconds(2),
            $"Expected Ctrl+C to interrupt promptly, observed latency {interruptLatency.Elapsed}.");

        pty.Write("exit\n");
        pty.Stop();
    }

    [Fact]
    public void UnixPty_Stop_DuringContinuousOutput_DoesNotThrow()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        using UnixPty pty = new();
        using ManualResetEventSlim sawOutput = new(false);

        pty.DataReceived += (_, length) =>
        {
            if (length > 0)
            {
                sawOutput.Set();
            }
        };

        pty.Start(shell: "/bin/sh", columns: 80, rows: 24, workingDirectory: Environment.CurrentDirectory);
        pty.Write("while :; do printf 'x'; done\n");

        Assert.True(sawOutput.Wait(TimeSpan.FromSeconds(3)), "Expected to observe continuous PTY output.");

        pty.Stop();

        // Give the reader thread a short window to finish after stop.
        Thread.Sleep(200);
    }

    [Fact]
    public void UnixPty_Write_DoesNotBlockCaller_WhenChildNotReadingInput()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        using UnixPty pty = new();
        try
        {
            pty.Start(shell: "/bin/sh", columns: 80, rows: 24, workingDirectory: Environment.CurrentDirectory);
            pty.Write("while :; do :; done\n");
            Thread.Sleep(150);

            byte[] largeInput = new byte[8 * 1024 * 1024];
            Array.Fill(largeInput, (byte)'x');

            Exception? writeException = null;
            using ManualResetEventSlim writeCompleted = new(false);
            void PerformWrite()
            {
                try
                {
                    if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
                    {
                        pty.Write(largeInput, 0, largeInput.Length);
                    }
                }
                catch (Exception ex)
                {
                    writeException = ex;
                }
                finally
                {
                    writeCompleted.Set();
                }
            }

            Stopwatch writeLatency = Stopwatch.StartNew();
            Thread writeThread = new(PerformWrite)
            {
                IsBackground = true,
                Name = "UnixPty-Write-Latency-Probe",
            };
            writeThread.Start();
            bool completedQuickly = writeCompleted.Wait(TimeSpan.FromMilliseconds(750));

            Assert.True(
                completedQuickly,
                $"Expected UnixPty.Write caller path to return promptly even under backpressure. Elapsed={writeLatency.Elapsed}.");
            Assert.Null(writeException);
        }
        finally
        {
            pty.Stop();
        }
    }

    [Fact]
    public void WindowsPty_Write_DoesNotBlockCaller_WhenChildNotReadingInput()
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
                    "ping -t 127.0.0.1 >nul",
                ]);
            Thread.Sleep(150);

            byte[] largeInput = new byte[8 * 1024 * 1024];
            Array.Fill(largeInput, (byte)'x');

            Exception? writeException = null;
            using ManualResetEventSlim writeCompleted = new(false);
            void PerformWrite()
            {
                try
                {
                    if (OperatingSystem.IsWindows())
                    {
                        pty.Write(largeInput, 0, largeInput.Length);
                    }
                }
                catch (Exception ex)
                {
                    writeException = ex;
                }
                finally
                {
                    writeCompleted.Set();
                }
            }

            Stopwatch writeLatency = Stopwatch.StartNew();
            Thread writeThread = new(PerformWrite)
            {
                IsBackground = true,
                Name = "WindowsPty-Write-Latency-Probe",
            };
            writeThread.Start();
            bool completedQuickly = writeCompleted.Wait(TimeSpan.FromMilliseconds(750));

            Assert.True(
                completedQuickly,
                $"Expected WindowsPty.Write caller path to return promptly even under backpressure. Elapsed={writeLatency.Elapsed}.");
            Assert.Null(writeException);
        }
        finally
        {
            pty.Stop();
        }
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
    public void WindowsPty_Start_ProvidesWindowsTerminalEnvironment()
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
        string marker = "__ROYALTERMINAL_WINDOWS_PTY_ENV__";

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
            shell: cmdPath,
            columns: 80,
            rows: 24,
            workingDirectory: Environment.CurrentDirectory,
            environment: new Dictionary<string, string>
            {
                ["ROYALTERMINAL_CONPTY_ENV"] = "%SystemRoot%",
            },
            arguments:
            [
                "/d",
                "/c",
                "echo " + marker +
                "&echo WT_SESSION=%WT_SESSION%" +
                "&echo WT_PROFILE_ID=%WT_PROFILE_ID%" +
                "&echo WSLENV=%WSLENV%" +
                "&echo ROYALTERMINAL_CONPTY_ENV=%ROYALTERMINAL_CONPTY_ENV%",
            ]);

        Assert.True(sawMarker.Wait(ContractTimeout), $"Did not observe environment marker. Current output: {output}");
        Assert.True(sawExit.Wait(ContractTimeout), "PTY child did not exit after environment probe.");

        string snapshot;
        lock (output)
        {
            snapshot = output.ToString();
        }

        Assert.Contains(marker, snapshot, StringComparison.Ordinal);
        Assert.Matches(@"WT_SESSION=[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}", snapshot);
        Assert.Matches(@"WT_PROFILE_ID=\{[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\}", snapshot);
        Assert.Matches(@"WSLENV=.*WT_SESSION", snapshot);
        Assert.Matches(@"WSLENV=.*WT_PROFILE_ID", snapshot);
        Assert.Matches(@"WSLENV=.*ROYALTERMINAL_CONPTY_ENV", snapshot);
        Assert.Contains("ROYALTERMINAL_CONPTY_ENV=" + Environment.GetEnvironmentVariable("SystemRoot"), snapshot, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WindowsPty_PwshManagedProcessorResize_PreservesPowerShellTableAfterConptyResize()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        if (!TryResolvePwshPath(out string? pwshPath))
        {
            return;
        }

        const string marker = "__ROYALTERMINAL_PWSH_REFLOW_DONE__";
        TerminalScreen screen = new(133, 33, scrollbackLimit: 400);
        using BasicVtProcessor processor = new(screen);
        using WindowsPty pty = new();
        using ManualResetEventSlim sawMarker = new(false);
        object sync = new();
        StringBuilder rawOutput = new();
        int outputVersion = 0;

#pragma warning disable CA1416
        processor.ResponseCallback = bytes => pty.Write(bytes, 0, bytes.Length);
#pragma warning restore CA1416
        pty.DataReceived += (data, length) =>
        {
            lock (sync)
            {
                processor.Process(data.AsSpan(0, length));
                rawOutput.Append(Encoding.UTF8.GetString(data, 0, length));
                outputVersion++;
                if (rawOutput.ToString().Contains(marker, StringComparison.Ordinal))
                {
                    sawMarker.Set();
                }
            }
        };

        try
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            pty.Start(
                shell: pwshPath,
                columns: 133,
                rows: 33,
                workingDirectory: home,
                environment: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["POWERSHELL_UPDATECHECK"] = "Off",
                },
                arguments:
                [
                    "-NoLogo",
                    "-NoProfile",
                    "-NoExit",
                ]);

            pty.Write($"$PSStyle.OutputRendering='Ansi'; Get-ChildItem $HOME; Write-Output '{marker}'\r");

            Assert.True(
                sawMarker.Wait(TimeSpan.FromSeconds(10)),
                $"Did not observe PowerShell marker. Current output: {rawOutput}");
            WaitForStableOutput(() => Volatile.Read(ref outputVersion), TimeSpan.FromSeconds(2));

            string beforeResize;
            lock (sync)
            {
                beforeResize = ReadAllRows(screen);
            }

            if (!beforeResize.Contains("iCloudPhotos", StringComparison.Ordinal) ||
                !beforeResize.Contains("Music", StringComparison.Ordinal))
            {
                return;
            }

            string[] expectedRows = GetKnownPowerShellHomeRowsPresentIn(beforeResize);

            foreach (int width in new[] { 120, 100, 80, 60, 51 })
            {
                lock (sync)
                {
                    processor.ResizeScreen(
                        columns: width,
                        rows: 31,
                        widthPx: width * 10,
                        heightPx: 496,
                        reflowOnResize: true,
                        preserveViewportTopOnRowsIncrease: true);
                }

                pty.Resize(width, 31);
                WaitForStableOutput(() => Volatile.Read(ref outputVersion), TimeSpan.FromMilliseconds(500));
            }

            foreach (int width in new[] { 60, 80, 100, 120, 133 })
            {
                lock (sync)
                {
                    processor.ResizeScreen(
                        columns: width,
                        rows: 33,
                        widthPx: width * 10,
                        heightPx: 528,
                        reflowOnResize: true,
                        preserveViewportTopOnRowsIncrease: true);
                }

                pty.Resize(width, 33);
                WaitForStableOutput(() => Volatile.Read(ref outputVersion), TimeSpan.FromMilliseconds(500));
            }

            string afterResize;
            string styledSnapshot;
            int cursorRow;
            lock (sync)
            {
                afterResize = ReadAllRows(screen);
                cursorRow = processor.CursorRow;
                ITerminalSnapshotExportSource exporter = processor;
                Assert.True(
                    exporter.TryExportSnapshot(
                        TerminalSnapshotExportFormat.StyledVt,
                        CreateStyledSnapshotOptions(),
                        out styledSnapshot));
            }

            AssertPowerShellHomeRowsPreserved(expectedRows, afterResize);
            Assert.DoesNotContain("iCloudhotos", afterResize, StringComparison.Ordinal);
            Assert.DoesNotContain("Saved Games                                                    Searches", afterResize, StringComparison.Ordinal);
            AssertPowerShellHomeRowsPreserved(expectedRows, styledSnapshot);
            Assert.DoesNotContain("iCloudhotos", styledSnapshot, StringComparison.Ordinal);
            Assert.True(cursorRow >= 30, $"Expected cursor near bottom after resize, observed row {cursorRow}. Screen: {afterResize}");
        }
        finally
        {
            pty.Stop();
        }
    }

    private static string[] GetKnownPowerShellHomeRowsPresentIn(string text)
    {
        string[] knownRows =
        [
            ".android",
            ".cargo",
            ".codex",
            ".config",
            ".copilot",
            ".dbus-keyrings",
            ".dotnet",
            ".gnupg",
            ".ms-ad",
            ".nuget",
            ".rustup",
            ".skiko",
            ".templateengine",
            ".vscode",
            ".vscode-shared",
            "Contacts",
            "Documents",
            "dotnet",
            "dotTraceSnapshots",
            "Downloads",
            "Dropbox",
            "Favorites",
            "GitHub",
            "iCloudDrive",
            "iCloudPhotos",
            "Links",
            "Music",
            "OneDrive",
            "Pictures",
            "Saved Games",
            "Searches",
            "source",
            "Videos",
            ".bash_history",
            ".gitconfig",
            ".lesshst",
            "dotnet-install.sh",
            "java_error_in_rider_12460.log",
            "java_error_in_rider64_26852.log",
            "java_error_in_rider64.hprof",
        ];

        List<string> present = [];
        foreach (string row in knownRows)
        {
            if (text.Contains(row, StringComparison.Ordinal))
            {
                present.Add(row);
            }
        }

        return present.ToArray();
    }

    private static void AssertPowerShellHomeRowsPreserved(IReadOnlyList<string> expectedRows, string actual)
    {
        foreach (string row in expectedRows)
        {
            Assert.Contains(row, actual, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void WindowsPty_DataReceived_ProvidesStableBuffersAcrossReads()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        if (!TryGetWindowsContractCmdPath(out string? cmdPath))
        {
            return;
        }

        const string firstMarker = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
        const string secondMarker = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";

        using WindowsPty pty = new();
        using ManualResetEventSlim sawMultipleReads = new(false);
        byte[]? firstBuffer = null;
        byte[]? firstSnapshot = null;
        int firstLength = 0;
        int nonEmptyEventCount = 0;

        pty.DataReceived += (data, length) =>
        {
            if (length <= 0)
            {
                return;
            }

            if (firstBuffer is null)
            {
                firstBuffer = data;
                firstLength = length;
                firstSnapshot = data.AsSpan(0, length).ToArray();
                return;
            }

            if (Interlocked.Increment(ref nonEmptyEventCount) >= 1)
            {
                sawMultipleReads.Set();
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
                    $"echo {firstMarker}&& ping -n 2 127.0.0.1 >nul && echo {secondMarker}",
                ]);

            Assert.True(sawMultipleReads.Wait(ContractTimeout), "Expected multiple non-empty PTY read events.");
            Assert.NotNull(firstBuffer);
            Assert.NotNull(firstSnapshot);
            Assert.Equal(firstSnapshot, firstBuffer.AsSpan(0, firstLength).ToArray());
        }
        finally
        {
            pty.Stop();
        }
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

    private static bool TryResolvePwshPath(out string? pwshPath)
    {
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string installedPwsh = Path.Combine(programFiles, "PowerShell", "7", "pwsh.exe");
        if (File.Exists(installedPwsh))
        {
            pwshPath = installedPwsh;
            return true;
        }

        string? path = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(path))
        {
            string[] directories = path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            for (int i = 0; i < directories.Length; i++)
            {
                string candidate = Path.Combine(directories[i], "pwsh.exe");
                if (File.Exists(candidate))
                {
                    pwshPath = candidate;
                    return true;
                }
            }
        }

        pwshPath = null;
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

    private static void WaitForStableOutput(Func<int> getVersion, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        int previousVersion = getVersion();
        DateTime stableSince = DateTime.UtcNow;

        while (DateTime.UtcNow < deadline)
        {
            Thread.Sleep(25);
            int currentVersion = getVersion();
            if (currentVersion != previousVersion)
            {
                previousVersion = currentVersion;
                stableSince = DateTime.UtcNow;
                continue;
            }

            if (DateTime.UtcNow - stableSince >= TimeSpan.FromMilliseconds(150))
            {
                return;
            }
        }
    }

    private static string ReadAllRows(TerminalScreen screen)
    {
        StringBuilder builder = new();
        for (int row = 0; row < screen.TotalRows; row++)
        {
            AppendRowText(builder, screen.GetRow(row));
            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static void AppendRowText(StringBuilder builder, TerminalRow row)
    {
        ReadOnlySpan<TerminalCell> cells = row.ReadOnlyCells;
        for (int column = 0; column < cells.Length; column++)
        {
            TerminalCell cell = cells[column];
            if (cell.Width == 0)
            {
                continue;
            }

            if (!string.IsNullOrEmpty(cell.Grapheme))
            {
                builder.Append(cell.Grapheme);
            }
            else if (cell.Codepoint != 0 && Rune.IsValid(cell.Codepoint))
            {
                builder.Append(char.ConvertFromUtf32(cell.Codepoint));
            }
            else
            {
                builder.Append(' ');
            }
        }
    }

    private static TerminalSnapshotExportOptions CreateStyledSnapshotOptions()
    {
        return new TerminalSnapshotExportOptions(
            Unwrap: true,
            TrimTrailingWhitespace: true,
            Extras: new TerminalSnapshotExportExtras(
                IncludeCursor: true,
                IncludeStyle: true,
                IncludeHyperlinks: true,
                IncludeKittyKeyboard: true,
                IncludeCharsets: true,
                IncludePalette: true,
                IncludeModes: true,
                IncludeScrollingRegion: true,
                IncludeTabstops: true,
                IncludeKeyboardModes: true));
    }
}
