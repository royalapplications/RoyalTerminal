// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

[Collection("PtyContractTests")]
public sealed class UnixPtyProcessTests
{
    [Fact]
    public void CtrlZSuspendReachesForegroundJob()
    {
        if (!OperatingSystem.IsMacOS() || !File.Exists("/bin/zsh")) return;
        using UnixPty pty = new();
        using ManualResetEventSlim ready = new(false), active = new(false), recovered = new(false);
        StringBuilder output = new();
        pty.DataReceived += (bytes, count) =>
        {
            lock (output)
            {
                output.Append(Encoding.UTF8.GetString(bytes, 0, count));
                string text = output.ToString();
                if (text.Contains("__READY__", StringComparison.Ordinal)) ready.Set();
                if (text.Contains("__ACTIVE__", StringComparison.Ordinal)) active.Set();
                if (text.Contains("__RECOVERED__", StringComparison.Ordinal)) recovered.Set();
            }
        };
        pty.Start("/bin/zsh", arguments: ["-f", "-i"]);
        pty.Write(UnixPtyTestCommands.PrintMarker("__READY__"));
        Assert.True(ready.Wait(TimeSpan.FromSeconds(5)));
        pty.Write("/bin/sh -c 'printf \"__%s__\\n\" ACTIVE; while :; do sleep 1; done'\n");
        Assert.True(active.Wait(TimeSpan.FromSeconds(5)));
        pty.Write("\u001a");
        pty.Write(UnixPtyTestCommands.PrintMarker("__RECOVERED__"));
        bool completed = recovered.Wait(TimeSpan.FromSeconds(2));
        string transcript;
        lock (output) transcript = output.ToString();
        Assert.True(completed, transcript);
    }

    [Fact]
    public void SpawnFailureLeavesInstanceReusableAndParentDirectoryUnchanged()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return;
        string cwd = Environment.CurrentDirectory;
        using UnixPty pty = new();
        Assert.Throws<IOException>(() =>
        {
            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
                pty.Start("/royalterminal-nonexistent-executable");
        });
        Assert.False(pty.IsRunning);
        Assert.Equal(-1, pty.ChildPid);
        using ManualResetEventSlim received = new(false);
        pty.DataReceived += (_, count) => { if (count > 0) received.Set(); };
        pty.Start("/bin/sh", workingDirectory: "/", arguments: ["-c", "printf recovered"]);
        Assert.True(received.Wait(TimeSpan.FromSeconds(5)));
        Assert.Equal(cwd, Environment.CurrentDirectory);
    }

    [Fact]
    public void ChildPathWorkingDirectoryArgumentsAndScriptFallbackArePreserved()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return;
        string directory = Directory.CreateTempSubdirectory("royalterminal-spawn-").FullName;
        try
        {
            string script = Path.Combine(directory, "royal-script");
            File.WriteAllText(script, "printf '%s|%s|%s' \"$PWD\" \"$1\" \"$TERM\"\n");
            File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            using UnixPty pty = new();
            using ManualResetEventSlim received = new(false);
            StringBuilder output = new();
            const string argument = "space ' quote $()";
            string expected = $"{directory}|{argument}|royal-spawn";
            pty.DataReceived += (bytes, count) =>
            {
                lock (output)
                {
                    output.Append(Encoding.UTF8.GetString(bytes, 0, count));
                    if (output.ToString().Contains(expected, StringComparison.Ordinal)) received.Set();
                }
            };
            pty.Start("royal-script", workingDirectory: directory, arguments: [argument],
                environment: new Dictionary<string, string> { ["PATH"] = ".", ["TERM"] = "royal-spawn" });
            Assert.True(received.Wait(TimeSpan.FromSeconds(5)), "Child did not preserve PATH/cwd/argv/TERM.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
