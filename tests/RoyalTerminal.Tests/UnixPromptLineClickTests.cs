// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Diagnostics;
using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

[Collection("PtyContractTests")]
public sealed class UnixPromptLineClickTests
{
    [Theory]
    [InlineData(false, "/bin/bash")]
    [InlineData(true, "/bin/bash")]
    [InlineData(false, "/bin/zsh")]
    [InlineData(true, "/bin/zsh")]
    public void ShellLineEditorAcceptsLeftAndRightPromptClicks(bool native, string shell)
    {
        if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsLinux())
        {
            Assert.Skip("Unix PTY required.");
            return;
        }
        Assert.SkipUnless(File.Exists(shell), "Shell unavailable.");
        Assert.SkipWhen(native && !GhosttyVtProcessor.IsAvailable(), "Native unavailable.");
        TerminalScreen screen = new(80, 12);
        using IVtProcessor processor = native ? new GhosttyVtProcessor(screen) : new BasicVtProcessor(screen);
        using ManualResetEventSlim changed = new(false);
        using UnixPty pty = new();
        StringBuilder output = new();
        processor.ResponseCallback = bytes =>
        {
            if (OperatingSystem.IsMacOS() || OperatingSystem.IsLinux()) pty.Write(bytes);
        };
        pty.DataReceived += (bytes, length) =>
        {
            lock (output)
            {
                processor.Process(bytes.AsSpan(0, length));
                output.Append(Encoding.UTF8.GetString(bytes, 0, length));
                changed.Set();
            }
        };
        bool bash = shell.EndsWith("/bash", StringComparison.Ordinal);
        pty.Start(shell, columns: 80, rows: 12,
            arguments: bash ? ["--noprofile", "--norc", "-i"] : ["-f", "-i"],
            environment: new Dictionary<string, string>
            {
                ["TERM"] = "xterm-256color", ["LC_ALL"] = "C",
                ["HISTFILE"] = "/dev/null", ["INPUTRC"] = "/dev/null",
            });
        try
        {
            // Only configure prompt markers and ordinary Emacs editing. Neither
            // shell has a mouse handler, custom widget or a user rc/inputrc file.
            pty.Write(bash
                ? "PS1='\\[\\e]133;A;cl=line\\a\\]> \\[\\e]133;B\\a\\]'; bind 'set editing-mode emacs'\r"
                : "PROMPT=$'%{\\e]133;A;cl=line\\a%}> %{\\e]133;B\\a%}'; RPROMPT=''; bindkey -e\r");
            WaitFor(() => ((ITerminalPromptStateSource)processor).PromptState is
                { Click: TerminalPromptClick.Line, Content: TerminalSemanticContent.Input });
            const string command = "printf '<%s>' abcdef";
            pty.Write(command);
            WaitFor(() => CurrentLine().Contains(command, StringComparison.Ordinal));
            ClickAtCursorOffset(-3);
            pty.Write("X");
            WaitFor(() => CurrentLine().Contains("abcXdef", StringComparison.Ordinal));
            ClickAtCursorOffset(2);
            pty.Write("Y");
            WaitFor(() => CurrentLine().Contains("abcXdeYf", StringComparison.Ordinal));
            pty.Write("\r");
            WaitFor(() => output.ToString().Contains("<abcXdeYf>", StringComparison.Ordinal));
        }
        finally
        {
            pty.Stop();
        }

        string CurrentLine()
        {
            TerminalRow row = screen.GetRow(screen.TotalRows - screen.ViewportRows + processor.CursorRow);
            StringBuilder text = new();
            foreach (TerminalCell cell in row.ReadOnlyCells)
                text.Append(cell.Codepoint == 0 ? ' ' : (char)cell.Codepoint);
            return text.ToString();
        }

        void ClickAtCursorOffset(int columns)
        {
            byte[] sequence;
            lock (output)
            {
                Assert.True(((ITerminalPromptClickEncoderSource)processor).TryEncodePromptClick(
                    processor.CursorCol + columns, processor.CursorRow, out sequence));
            }
            pty.Write(sequence);
        }

        void WaitFor(Func<bool> condition)
        {
            Stopwatch timer = Stopwatch.StartNew();
            while (true)
            {
                lock (output)
                {
                    if (condition()) return;
                    Assert.True(timer.Elapsed < TimeSpan.FromSeconds(10), output.ToString());
                    changed.Reset();
                }
                changed.Wait(TimeSpan.FromMilliseconds(100));
            }
        }
    }
}
