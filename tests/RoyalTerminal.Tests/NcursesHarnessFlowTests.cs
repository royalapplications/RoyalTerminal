// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Tests — headless PTY flow tests using a real ncurses harness.

using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using RoyalTerminal.Avalonia.Controls;
using RoyalTerminal.Avalonia.Services;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Services;
using RoyalTerminal.Terminal.Transport.Ssh;
using RoyalTerminal.Terminal.Transport.Ssh.SshNet;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class NcursesHarnessFlowTests
{
    [AvaloniaFact]
    public async Task NcursesHarness_ManagedVt_HandlesKeyboardMouseAndResize()
    {
        await RunHarnessFlowAsync(VtProcessorPreference.Managed);
    }

    [AvaloniaFact]
    public async Task NcursesHarness_NativeVt_HandlesKeyboardMouseAndResize_WhenAvailable()
    {
        if (!GhosttyVtProcessor.IsAvailable())
        {
            return;
        }

        await RunHarnessFlowAsync(VtProcessorPreference.Native);
    }

    private static async Task RunHarnessFlowAsync(VtProcessorPreference preference)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        if (!TryFindPythonWithCurses(out string? pythonExe))
        {
            return;
        }

        string fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "NcursesHarness.py");
        Assert.True(File.Exists(fixturePath), $"Ncurses harness fixture not found: {fixturePath}");

        string tempDir = Path.Combine(Path.GetTempPath(), "RoyalTerminal", "NcursesHarness");
        Directory.CreateDirectory(tempDir);
        string logPath = Path.Combine(tempDir, $"harness-{Guid.NewGuid():N}.log");

        INativeVtProcessorProvider[] nativeProviders =
        [
            new GhosttyVtProcessorProvider(),
        ];

        TerminalControl control = new(
            new TerminalSessionService(),
            new DefaultTerminalInputAdapter(),
            new DefaultTerminalSelectionService(),
            new DefaultTerminalScrollService(),
            new DefaultVtProcessorFactory(nativeProviders),
            new DefaultPtyFactory(),
            new NullSshCredentialProvider(),
            new RejectAllSshHostKeyValidator(),
            transportFactory: null)
        {
            VtProcessorPreference = preference,
            Columns = 80,
            Rows = 24,
        };
        Window window = new()
        {
            Width = 960,
            Height = 640,
            Content = control,
        };
        window.Show();

        try
        {
            bool arranged = await WaitUntilAsync(
                () => control.Bounds.Width > 1 && control.Bounds.Height > 1,
                TimeSpan.FromSeconds(2));
            Assert.True(arranged, $"Terminal control was not arranged in time. Bounds={control.Bounds}");

            control.StartPty(shell: "/bin/sh", workingDirectory: Environment.CurrentDirectory);

            string command =
                $"RT_HARNESS_LOG={ShellQuote(logPath)} " +
                $"RT_HARNESS_TIMEOUT_SEC=30 " +
                $"TERM=xterm-256color " +
                $"{ShellQuote(pythonExe!)} {ShellQuote(fixturePath)}\n";
            control.SendInput(command);

            string ready = await WaitForLogLineAsync(
                logPath,
                static line => line.StartsWith("READY ", StringComparison.Ordinal),
                timeout: TimeSpan.FromSeconds(15));
            Assert.Contains("x", ready, StringComparison.Ordinal);

            control.SendInput("a");
            string key = await WaitForLogLineAsync(
                logPath,
                static line => line == "KEY code=97",
                timeout: TimeSpan.FromSeconds(8));
            Assert.Equal("KEY code=97", key);

            int pointerPressedCount = 0;
            control.AddHandler(
                InputElement.PointerPressedEvent,
                (_, _) => pointerPressedCount++,
                RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
                handledEventsToo: true);

            Point point = await GetInteractionPointAsync(control, window);
            RaiseMousePressRelease(control, window, point);
            Dispatcher.UIThread.RunJobs();
            Assert.True(pointerPressedCount > 0, "Headless pointer press was not routed to TerminalControl.");

            string mouse = await WaitForLogLineAsync(
                logPath,
                static line => line.StartsWith("MOUSE ", StringComparison.Ordinal),
                timeout: TimeSpan.FromSeconds(8));
            Assert.Contains("x=", mouse, StringComparison.Ordinal);

            control.Columns = 100;
            control.Rows = 40;

            string resize = await WaitForLogLineAsync(
                logPath,
                static line => line == "RESIZE 40x100",
                timeout: TimeSpan.FromSeconds(8));
            Assert.Equal("RESIZE 40x100", resize);

            control.SendInput("q");
            string exit = await WaitForLogLineAsync(
                logPath,
                static line => line == "EXIT quit",
                timeout: TimeSpan.FromSeconds(8));
            Assert.Equal("EXIT quit", exit);
        }
        finally
        {
            window.Close();
            try
            {
                control.StopPty();
            }
            catch
            {
                // Best effort cleanup in tests.
            }
        }
    }

    private static bool TryFindPythonWithCurses(out string? executable)
    {
        string[] candidates =
        [
            "python3",
            "python",
        ];

        for (int i = 0; i < candidates.Length; i++)
        {
            string candidate = candidates[i];
            ProcessStartInfo startInfo = new(candidate)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add("import curses");

            try
            {
                using Process? process = Process.Start(startInfo);
                if (process is null)
                {
                    continue;
                }

                if (!process.WaitForExit(3000))
                {
                    try { process.Kill(entireProcessTree: true); } catch { }
                    continue;
                }

                if (process.ExitCode == 0)
                {
                    executable = candidate;
                    return true;
                }
            }
            catch
            {
                // Probe next candidate.
            }
        }

        executable = null;
        return false;
    }

    private static async Task<Point> GetInteractionPointAsync(TerminalControl control, Window window)
    {
        bool arranged = await WaitUntilAsync(
            () => control.Bounds.Width > 1 && control.Bounds.Height > 1,
            TimeSpan.FromSeconds(2));
        Assert.True(arranged, $"Terminal control was not arranged in time. Bounds={control.Bounds}");

        Point local = new(control.Bounds.Width * 0.5, control.Bounds.Height * 0.5);
        Point? translated = control.TranslatePoint(local, window);
        Assert.True(translated.HasValue, "Failed to translate interaction point to window coordinates.");
        return translated!.Value;
    }

    private static void RaiseMousePressRelease(TerminalControl control, Window window, Point windowPoint)
    {
        _ = control;
        window.MouseMove(windowPoint, RawInputModifiers.None);
        window.MouseDown(windowPoint, MouseButton.Left, RawInputModifiers.LeftMouseButton);
        window.MouseUp(windowPoint, MouseButton.Left, RawInputModifiers.None);
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> predicate, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            if (predicate())
            {
                return true;
            }

            await Task.Delay(25);
        }

        Dispatcher.UIThread.RunJobs();
        return predicate();
    }

    private static async Task<string> WaitForLogLineAsync(
        string path,
        Func<string, bool> predicate,
        TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        int lastSeenLineCount = 0;
        string[] lines = Array.Empty<string>();

        while (DateTime.UtcNow < deadline)
        {
            if (File.Exists(path))
            {
                lines = await File.ReadAllLinesAsync(path).ConfigureAwait(false);
                for (int i = lastSeenLineCount; i < lines.Length; i++)
                {
                    if (predicate(lines[i]))
                    {
                        return lines[i];
                    }
                }

                lastSeenLineCount = lines.Length;
            }

            await Task.Delay(50).ConfigureAwait(false);
        }

        string snapshot = lines.Length == 0 ? "<empty>" : string.Join("\n", lines);
        throw new Xunit.Sdk.XunitException(
            $"Timed out waiting for harness log line. File: {path}\nObserved log:\n{snapshot}");
    }

    private static string ShellQuote(string value)
    {
        return "'" + value.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'";
    }
}
