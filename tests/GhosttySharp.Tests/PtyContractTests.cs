// Licensed under the MIT License.
// GhosttySharp.Tests — Cross-platform PTY contract tests.

using System.Text;
using GhosttySharp.Avalonia.Terminal;
using Xunit;

namespace GhosttySharp.Tests;

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
        string marker = "__GHOSTTYSHARP_UNIX_PTY_CONTRACT__";

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
    public void WindowsPty_StartWriteReadExit_Contract()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using WindowsPty pty = new();
        using ManualResetEventSlim sawMarker = new(false);
        StringBuilder output = new();
        string marker = "__GHOSTTYSHARP_WINDOWS_PTY_CONTRACT__";

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
}
