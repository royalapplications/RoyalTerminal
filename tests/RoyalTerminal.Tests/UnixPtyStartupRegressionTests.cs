// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using System.Text;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

[Collection("PtyContractTests")]
public sealed class UnixPtyStartupRegressionTests
{
    [Fact]
    public async Task ConcurrentStartsKeepDirectoriesAndTerminalStreamsIsolated()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return;
        // As in Ghostty and node-pty, cwd and terminal ownership belong to each
        // child. Windows Terminal supplies cwd to CreateProcess instead.
        string parentDirectory = Environment.CurrentDirectory;
        string root = Directory.CreateTempSubdirectory("royal-concurrent-pty-").FullName;
        try
        {
            const int sessionCount = 6;
            for (int round = 0; round < 4; round++)
            {
                using Barrier start = new(sessionCount);
                Task<string>[] sessions = new Task<string>[sessionCount];
                for (int index = 0; index < sessionCount; index++)
                {
                    string id = $"{round}-{index}";
                    string directory = Directory.CreateDirectory(Path.Combine(root, id)).FullName;
                    File.WriteAllText(Path.Combine(directory, "identity"), id);
                    sessions[index] = Task.Factory.StartNew(
                        () => OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()
                            ? RunConcurrentSession(start, directory, id, parentDirectory) : string.Empty,
                        CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                }
                string[] terminals = await Task.WhenAll(sessions);
                Assert.Equal(sessionCount, new HashSet<string>(terminals, StringComparer.Ordinal).Count);
                Assert.Equal(parentDirectory, Environment.CurrentDirectory);
            }
        }
        finally
        {
            // Also contain cwd mutations if this test is run against a regressed implementation.
            Environment.CurrentDirectory = parentDirectory;
            Directory.Delete(root, recursive: true);
        }
    }

    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("macos")]
    private static string RunConcurrentSession(Barrier start, string directory, string id, string parentDirectory)
    {
        using UnixPty pty = new();
        using ManualResetEventSlim ready = new(false), complete = new(false), exited = new(false);
        StringBuilder output = new();
        pty.DataReceived += (bytes, count) =>
        {
            lock (output)
            {
                output.Append(Encoding.UTF8.GetString(bytes, 0, count));
                if (output.ToString().Contains($"READY:{id}\r\n", StringComparison.Ordinal)) ready.Set();
                if (output.ToString().Contains($"ERR:{id}\r\n", StringComparison.Ordinal)) complete.Set();
            }
        };
        pty.ProcessExited += _ => exited.Set();
        Assert.True(start.SignalAndWait(TimeSpan.FromSeconds(10)), "Concurrent startup barrier timed out.");
        pty.Start("/bin/sh", workingDirectory: directory, arguments: ["-c",
            "stty -echo || exit 11; tty; printf 'READY:%s\\n' \"$(cat identity)\"; " +
            "IFS= read -r reply || exit 12; printf 'OUT:%s:%s\\n' \"$(cat identity)\" \"$reply\"; " +
            "printf 'ERR:%s\\n' \"$(cat identity)\" >&2; IFS= read -r finish"]);
        Assert.Equal(parentDirectory, Environment.CurrentDirectory);
        Assert.True(ready.Wait(TimeSpan.FromSeconds(8)), $"Session {id} did not start in its own directory.");
        // Keep every child alive until all slave terminals have been allocated,
        // otherwise the OS may legitimately recycle an earlier slave's name.
        Assert.True(start.SignalAndWait(TimeSpan.FromSeconds(10)), "Concurrent readiness barrier timed out.");
        pty.Write($"input-{id}\n");
        // Validate startup and IO while the slave is still open. Final-output
        // draining during process exit is covered by the gather tests.
        bool received = complete.Wait(TimeSpan.FromSeconds(8));
        string transcript;
        lock (output) transcript = output.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.True(received, $"Session {id} did not complete its roundtrip: {transcript}");
        string[] lines = transcript.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.True(lines.Length == 4, $"Session {id} expected four output lines, got {lines.Length}: {transcript}");
        Assert.StartsWith("/dev/", lines[0]);
        Assert.Equal($"READY:{id}", lines[1]);
        Assert.Equal($"OUT:{id}:input-{id}", lines[2]);
        Assert.Equal($"ERR:{id}", lines[3]);
        pty.Write("finish\n");
        Assert.True(exited.Wait(TimeSpan.FromSeconds(8)), $"Session {id} did not exit.");
        return lines[0];
    }

    [Theory]
    [InlineData(1)] // SIGHUP
    [InlineData(2)] // SIGINT
    [InlineData(13)] // SIGPIPE
    [InlineData(15)] // SIGTERM (same numbers on Linux and Darwin)
    public Task SpawnResetsInheritedIgnoredSignals(int signal)
        => RunIsolatedProbe("ignored-signal", signal);

    [Fact]
    public Task SpawnResetsInheritedIgnoredSuspendSignal()
        => RunIsolatedProbe("ignored-signal", OperatingSystem.IsMacOS() ? 18 : 20);

    [Theory]
    [InlineData(1)] // stdin
    [InlineData(2)] // stdout
    [InlineData(3)] // stdin + stdout
    [InlineData(4)] // stderr
    [InlineData(5)] // stdin + stderr
    [InlineData(6)] // stdout + stderr
    [InlineData(7)] // all standard descriptors
    public Task SpawnPreservesChildStreamsWithClosedParentDescriptors(int mask)
        => RunIsolatedProbe("closed-stdio", mask);

    private static async Task RunIsolatedProbe(string scenario, int value)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return;
        DirectoryInfo output = new(AppContext.BaseDirectory);
        DirectoryInfo? repository = output;
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "RoyalTerminal.slnx")))
            repository = repository.Parent;
        Assert.NotNull(repository);
        string executable = Path.Combine(repository.FullName, "tests", "RoyalTerminal.PtyHarness", "bin",
            output.Parent!.Name, output.Name, "RoyalTerminal.PtyHarness");
        Assert.True(File.Exists(executable), $"Missing harness for this configuration/framework: {executable}");
        string resultFile = Path.GetTempFileName();
        ProcessStartInfo info = new(executable)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        info.ArgumentList.Add("unix-spawn-probe");
        info.ArgumentList.Add(scenario);
        info.ArgumentList.Add(value.ToString(CultureInfo.InvariantCulture));
        info.ArgumentList.Add(resultFile);
        using Process process = new() { StartInfo = info };
        bool started = false;
        try
        {
            started = process.Start();
            Assert.True(started);
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(25));
            await process.WaitForExitAsync(timeout.Token);
            string result = File.ReadAllText(resultFile);
            Assert.True(process.ExitCode == 0, $"{scenario} ({value}) exited {process.ExitCode}: {result}");
            Assert.Equal("PASS", result);
        }
        finally
        {
            if (started && !process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
            File.Delete(resultFile);
        }
    }
}
