// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using Avalonia.Input;
using RoyalTerminal.Avalonia.App.Services;
using RoyalTerminal.Avalonia.Services;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Services;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed partial class TerminalInputAdapterTests
{
    // Windows Terminal/conhost transports KEY_EVENT_RECORDs in mode 9001:
    // https://github.com/microsoft/terminal/blob/main/doc/specs/%234999%20-%20Improved%20keyboard%20handling%20in%20Conpty.md
    // PSReadLine uses Console.ReadKey, whose Windows filter recognizes generic
    // VK_SHIFT/CONTROL/MENU (0x10..0x12), not VK_LSHIFT..VK_RMENU (0xA0..0xA5).
    // PowerShell RawUI also consumes these console records via ReadConsoleInput.
    // https://github.com/dotnet/runtime/blob/main/src/libraries/System.Console/src/System/ConsolePal.Windows.cs
    // https://github.com/PowerShell/PSReadLine/blob/master/PSReadLine/ConsoleLib.cs
    // https://github.com/PowerShell/PowerShell/blob/master/src/Microsoft.PowerShell.ConsoleHost/host/msh/ConsoleHostRawUserInterface.cs
    // Outside mode 9001 retain Ghostty/xterm.js legacy/Kitty encoding behavior.
    [Theory]
    [InlineData(Key.LeftShift, 16, 42, 16, 0)]
    [InlineData(Key.RightShift, 16, 54, 16, 0)]
    [InlineData(Key.LeftCtrl, 17, 29, 8, 0)]
    [InlineData(Key.RightCtrl, 17, 29, 260, 256)]
    [InlineData(Key.LeftAlt, 18, 56, 2, 0)]
    [InlineData(Key.RightAlt, 18, 56, 257, 256)]
    [InlineData(Key.LWin, 91, 91, 256, 256)]
    [InlineData(Key.RWin, 92, 92, 256, 256)]
    public async Task Win32ModifiersUseConsoleRecords(Key key, int vk, int scan, int pressed, int released)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.False(TerminalWin32InputSequenceEncoder.TryEncode(key, KeyModifiers.None, null, true, out _));
            return;
        }
        await WithWin32Session((adapter, session, processor, transport) =>
        {
            // Missing press flags and stale release flags must both be normalized.
            Assert.True(adapter.HandleKeyDown(new() { Key = key, KeySymbol = "ignored" }, session, processor));
            Assert.Equal($"\u001b[{vk};{scan};0;1;{pressed};1_", Encoding.UTF8.GetString(transport.LastInput!));
            KeyModifiers stale = key switch
            {
                Key.LeftShift or Key.RightShift => KeyModifiers.Shift,
                Key.LeftCtrl or Key.RightCtrl => KeyModifiers.Control,
                Key.LeftAlt or Key.RightAlt => KeyModifiers.Alt,
                _ => KeyModifiers.Meta,
            };
            Assert.True(adapter.HandleKeyUp(new() { Key = key, KeyModifiers = stale }, session));
            Assert.Equal($"\u001b[{vk};{scan};0;0;{released};1_", Encoding.UTF8.GetString(transport.LastInput!));
            Assert.True(TerminalWin32InputSequenceEncoder.TryEncode(key, stale, "ignored", true, out string directPress));
            Assert.Equal($"\u001b[{vk};{scan};0;1;{pressed};1_", directPress);
            Assert.True(TerminalWin32InputSequenceEncoder.TryEncode(key, stale, null, false, out string directRelease));
            Assert.Equal($"\u001b[{vk};{scan};0;0;{released};1_", directRelease);
        });
    }

    [Fact]
    public void Win32ModifierStatePreservesBothSidesAndMergesReportedFlags()
    {
        TerminalWin32ModifierState state = new();
        state.Update(Key.LeftShift, true);
        state.Update(Key.RightShift, true);
        state.Update(Key.LeftShift, false);
        Assert.Equal(16u, state.GetControlState(Key.LeftShift, KeyModifiers.Shift));
        state.Update(Key.RightAlt, true);
        state.Update(Key.RightCtrl, true);
        Assert.Equal(21u, state.GetControlState(Key.A, KeyModifiers.Control | KeyModifiers.Alt));
        state.Update(Key.LeftAlt, true);
        Assert.Equal(23u, state.GetControlState(Key.A, KeyModifiers.None));
        state.Update(Key.RightAlt, false);
        Assert.Equal(22u, state.GetControlState(Key.RightAlt, KeyModifiers.Alt));
        state = default;
        Assert.Equal(10u, state.GetControlState(Key.A, KeyModifiers.Control | KeyModifiers.Alt));
    }

    [Fact]
    public async Task Win32TracksChordsAndClearsStateAfterReleaseOrReset()
    {
        if (!OperatingSystem.IsWindows()) return;
        await WithWin32Session((adapter, session, processor, transport) =>
        {
            adapter.HandleKeyDown(new() { Key = Key.RightAlt, KeyModifiers = KeyModifiers.Alt }, session, processor);
            adapter.HandleKeyDown(new() { Key = Key.A }, session, processor);
            string[] fields = ParseWin32InputFields(transport.LastInput!);
            Assert.Equal("1", fields[4]); // Right Alt only; never fabricate Left Alt.
            adapter.HandleKeyDown(new() { Key = Key.LeftShift }, session, processor);
            adapter.HandleKeyDown(new() { Key = Key.RightShift }, session, processor);
            adapter.HandleKeyUp(new() { Key = Key.LeftShift, KeyModifiers = KeyModifiers.Shift }, session);
            Assert.Equal("17", ParseWin32InputFields(transport.LastInput!)[4]);
            adapter.HandleKeyUp(new() { Key = Key.RightAlt, KeyModifiers = KeyModifiers.Alt }, session);
            adapter.HandleKeyDown(new() { Key = Key.A }, session, processor);
            fields = ParseWin32InputFields(transport.LastInput!);
            Assert.Equal("16", fields[4]);
            Assert.Equal("65", fields[2]); // Tracked Shift affects fallback text too.
            byte[] previous = transport.LastInput!;
            adapter.ResetInputState();
            Assert.Same(previous, transport.LastInput); // Reset synthesizes no input.
            adapter.HandleKeyDown(new() { Key = Key.A }, session, processor);
            Assert.Equal("0", ParseWin32InputFields(transport.LastInput!)[4]);
        });
    }

    [Fact]
    public async Task Win32ModeChangesUseCurrentModeWithoutLosingHeldKeys()
    {
        if (!OperatingSystem.IsWindows()) return;
        await WithWin32Session((adapter, session, processor, transport) =>
        {
            processor.SetModeState(processor.ModeState with { Win32InputMode = false });
            Assert.False(adapter.HandleKeyDown(new() { Key = Key.LeftShift }, session, processor));
            Assert.Null(transport.LastInput);
            processor.SetModeState(processor.ModeState with { Win32InputMode = true });
            adapter.HandleKeyDown(new() { Key = Key.A }, session, processor);
            Assert.Equal("16", ParseWin32InputFields(transport.LastInput!)[4]);
            processor.SetModeState(processor.ModeState with { Win32InputMode = false });
            adapter.HandleKeyUp(new() { Key = Key.LeftShift }, session);
            processor.SetModeState(processor.ModeState with { Win32InputMode = true });
            adapter.HandleKeyDown(new() { Key = Key.A }, session, processor);
            Assert.Equal("0", ParseWin32InputFields(transport.LastInput!)[4]);
        });
    }

    [Fact]
    public async Task Win32CompositionCommitsTextWithoutModifierOrLateReleaseRecords()
    {
        if (!OperatingSystem.IsWindows()) return;
        await WithWin32Session((adapter, session, processor, transport) =>
        {
            adapter.SetComposing(true);
            adapter.HandleKeyDown(new() { Key = Key.LeftShift }, session, processor);
            Assert.Null(transport.LastInput);
            adapter.SetComposing(false);
            adapter.HandleTextInput(new() { Text = "日本" }, session);
            byte[] committed = transport.LastInput!;
            Assert.Equal("日本", Encoding.UTF8.GetString(committed));
            adapter.HandleKeyUp(new() { Key = Key.LeftShift }, session);
            Assert.Same(committed, transport.LastInput);
            adapter.HandleKeyDown(new() { Key = Key.A }, session, processor);
            Assert.Equal("0", ParseWin32InputFields(transport.LastInput!)[4]);
        });
    }

    [Fact]
    public async Task Win32DeadKeysCommitTextWithoutRawRecords()
    {
        if (!OperatingSystem.IsWindows()) return;
        await WithWin32Session((_, session, processor, transport) =>
        {
            DefaultTerminalInputAdapter adapter = new(new PlainKeyboardLayout(dead: true));
            Assert.False(adapter.HandleKeyDown(new() { Key = Key.E }, session, processor));
            Assert.Null(transport.LastInput);
            Assert.True(adapter.HandleTextInput(new() { Text = "é" }, session));
            byte[] committed = transport.LastInput!;
            Assert.Equal("é", Encoding.UTF8.GetString(committed));
            Assert.True(adapter.HandleKeyUp(new() { Key = Key.E }, session));
            Assert.Same(committed, transport.LastInput);
        });
    }

    [Fact]
    public async Task Win32AppWrapperDelegatesExactlyOneRecord()
    {
        if (!OperatingSystem.IsWindows()) return;
        await WithWin32Session((adapter, session, processor, transport) =>
        {
            HandledInputSuppressingTerminalInputAdapter wrapper = new(adapter);
            KeyEventArgs press = new() { Key = Key.LeftShift, Handled = true };
            Assert.True(wrapper.HandleKeyDown(press, session, processor));
            Assert.Equal("\u001b[16;42;0;1;16;1_", Encoding.UTF8.GetString(transport.LastInput!));
            Assert.True(wrapper.HandleKeyUp(new() { Key = Key.LeftShift, Handled = true }, session));
            Assert.Equal("\u001b[16;42;0;0;0;1_", Encoding.UTF8.GetString(transport.LastInput!));
            Assert.Equal(2, transport.InputCount);
        });
    }

    [Fact]
    public async Task Win32AppWrapperReleasesTrackedStateAfterModeIsDisabled()
    {
        if (!OperatingSystem.IsWindows()) return;
        await WithWin32Session((adapter, session, processor, transport) =>
        {
            HandledInputSuppressingTerminalInputAdapter wrapper = new(adapter);
            Assert.True(wrapper.HandleKeyDown(new() { Key = Key.RightAlt }, session, processor));
            processor.SetModeState(processor.ModeState with { Win32InputMode = false });
            wrapper.HandleKeyUp(new() { Key = Key.RightAlt }, session);
            processor.SetModeState(processor.ModeState with { Win32InputMode = true });
            wrapper.HandleKeyDown(new() { Key = Key.A }, session, processor);
            Assert.Equal("0", ParseWin32InputFields(transport.LastInput!)[4]);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Win32AppWrapperReleasesModifierAfterKittyStopsReporting(bool handled)
    {
        if (!OperatingSystem.IsWindows()) return;
        await WithWin32Session((adapter, session, processor, transport) =>
        {
            HandledInputSuppressingTerminalInputAdapter wrapper = new(adapter);
            wrapper.HandleKeyDown(new() { Key = Key.RightAlt }, session, processor);
            processor.SetKittyKeyboardFlags(1);
            processor.IsKeyEncodingAuthoritative = true;
            int writes = transport.InputCount;
            wrapper.HandleKeyUp(new() { Key = Key.RightAlt, Handled = handled }, session);
            Assert.Equal(writes, transport.InputCount); // Update physical state without bypassing Kitty suppression.
            processor.SetKittyKeyboardFlags(0);
            wrapper.HandleKeyDown(new() { Key = Key.A, KeySymbol = "a" }, session, processor);
            Assert.Equal("0", ParseWin32InputFields(transport.LastInput!)[4]);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Win32AppWrapperReleasesBothShiftsAcrossModeChange(bool handled)
    {
        if (!OperatingSystem.IsWindows()) return;
        await WithWin32Session((adapter, session, processor, transport) =>
        {
            HandledInputSuppressingTerminalInputAdapter wrapper = new(adapter);
            wrapper.HandleKeyDown(new() { Key = Key.LeftShift }, session, processor);
            wrapper.HandleKeyDown(new() { Key = Key.RightShift }, session, processor);
            wrapper.HandleKeyUp(new() { Key = Key.LeftShift }, session);
            Assert.Equal("16", ParseWin32InputFields(transport.LastInput!)[4]);
            processor.SetModeState(processor.ModeState with { Win32InputMode = false });
            wrapper.HandleKeyUp(new() { Key = Key.RightShift, Handled = handled }, session);
            processor.SetModeState(processor.ModeState with { Win32InputMode = true });
            wrapper.HandleKeyDown(new() { Key = Key.A, KeySymbol = "a" }, session, processor);
            Assert.Equal("0", ParseWin32InputFields(transport.LastInput!)[4]);
        });
    }

    [Fact]
    public async Task Win32AppWrapperClearsHeldModifierWhenReleasedDuringComposition()
    {
        if (!OperatingSystem.IsWindows()) return;
        await WithWin32Session((adapter, session, processor, transport) =>
        {
            HandledInputSuppressingTerminalInputAdapter wrapper = new(adapter);
            wrapper.HandleKeyDown(new() { Key = Key.RightAlt }, session, processor);
            wrapper.SetComposing(true);
            wrapper.HandleKeyUp(new() { Key = Key.RightAlt }, session);
            wrapper.SetComposing(false);
            wrapper.HandleKeyDown(new() { Key = Key.A, KeySymbol = "a" }, session, processor);
            Assert.Equal("0", ParseWin32InputFields(transport.LastInput!)[4]);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Win32AppWrapperTracksPressesSuppressedByKitty(bool bothShifts)
    {
        if (!OperatingSystem.IsWindows()) return;
        await WithWin32Session((adapter, session, processor, transport) =>
        {
            HandledInputSuppressingTerminalInputAdapter wrapper = new(adapter);
            processor.SetKittyKeyboardFlags(1);
            processor.IsKeyEncodingAuthoritative = true;
            wrapper.HandleKeyDown(new() { Key = bothShifts ? Key.LeftShift : Key.RightAlt }, session, processor);
            if (bothShifts) wrapper.HandleKeyDown(new() { Key = Key.RightShift }, session, processor);
            Assert.Equal(0, transport.InputCount);
            processor.SetKittyKeyboardFlags(0);
            if (bothShifts) wrapper.HandleKeyUp(new() { Key = Key.LeftShift }, session);
            wrapper.HandleKeyDown(new() { Key = Key.A, KeySymbol = "a" }, session, processor);
            Assert.Equal(bothShifts ? "16" : "1", ParseWin32InputFields(transport.LastInput!)[4]);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Win32ModifierEventsPreferKittyThroughBothAdapters(bool useAppWrapper)
    {
        await WithWin32Session((adapter, session, processor, transport) =>
        {
            processor.SetKittyKeyboardFlags(11);
            processor.EncodedKeySequence = "\u001b[57441u"u8.ToArray();
            ITerminalInputAdapter input = useAppWrapper ? new HandledInputSuppressingTerminalInputAdapter(adapter) : adapter;
            Assert.True(input.HandleKeyDown(new() { Key = Key.LeftShift }, session, processor));
            Assert.True(input.HandleKeyUp(new() { Key = Key.LeftShift }, session));
            Assert.Equal(2, transport.InputCount);
            Assert.Equal("\u001b[57441u", Encoding.UTF8.GetString(transport.LastInput!));
            Assert.Collection(processor.EncodedKeyRequests,
                key => Assert.Equal(TerminalInputAction.Press, key.Action),
                key => Assert.Equal(TerminalInputAction.Release, key.Action));
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Win32BareModifiersAreIgnoredByPowerShellConsoleReadKey(bool useAppWrapper)
    {
        if (!OperatingSystem.IsWindows()) return;
        string? pwsh = FindPowerShellCore();
        if (pwsh is null) Assert.Skip("PowerShell Core is required for the ConPTY integration test.");
        await WithWin32Session((adapter, session, processor, transport) =>
        {
            if (!OperatingSystem.IsWindows()) return;
            using WindowsPty pty = new();
            using BasicVtProcessor output = new(new RoyalTerminal.Avalonia.Rendering.TerminalScreen(80, 24));
            using ManualResetEventSlim ready = new(false);
            using ManualResetEventSlim complete = new(false);
            StringBuilder received = new();
            object sync = new();
#pragma warning disable CA1416 // Callback only exists inside this Windows-guarded test.
            output.ResponseCallback = bytes => pty.Write(bytes, 0, bytes.Length);
            pty.DataReceived += (bytes, count) =>
            {
                lock (sync)
                {
                    output.Process(bytes.AsSpan(0, count));
                    received.Append(Encoding.UTF8.GetString(bytes, 0, count));
                    string text = received.ToString();
                    if (text.Contains("__READY__", StringComparison.Ordinal)) ready.Set();
                    if (HasCompletePowerShellKeyResult(text)) complete.Set();
                }
            };
            transport.InputObserver = bytes => pty.Write(bytes, 0, bytes.Length);
#pragma warning restore CA1416
            string script = "[Console]::WriteLine('__READY__'); $k=[Console]::ReadKey($true); [Console]::WriteLine('__KEY_'+[int]$k.Key+'__')";
            try
            {
                // Readiness also verifies that output uses ConPTY and ReadKey
                // sees a console, rather than inherited test-runner pipes.
                pty.Start(shell: pwsh, arguments: ["-NoLogo", "-NoProfile", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script))]);
                Assert.True(ready.Wait(TimeSpan.FromSeconds(10)), "PowerShell did not reach Console.ReadKey. Output: " + CaptureOutput());
                ITerminalInputAdapter input = useAppWrapper ? new HandledInputSuppressingTerminalInputAdapter(adapter) : adapter;
                foreach (Key key in new[] { Key.LeftShift, Key.RightShift, Key.LeftAlt, Key.RightAlt, Key.LeftCtrl, Key.RightCtrl })
                {
                    input.HandleKeyDown(new() { Key = key }, session, processor);
                    input.HandleKeyUp(new() { Key = key }, session);
                }
                input.HandleKeyDown(new() { Key = Key.X, KeySymbol = "x" }, session, processor);
                Assert.True(complete.Wait(TimeSpan.FromSeconds(10)), "PowerShell did not return a key. Output: " + CaptureOutput());
                lock (sync) Assert.Contains("__KEY_88__", received.ToString());
            }
            finally
            {
                pty.Stop();
                transport.InputObserver = null;
            }

            string CaptureOutput()
            {
                lock (sync) return received.ToString();
            }
        });
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("__READY__", false)]
    [InlineData("__KEY_", false)]
    [InlineData("__KEY_8", false)]
    [InlineData("__KEY_88_", false)]
    [InlineData("__KEY_88__", true)]
    [InlineData("__READY__\r\n__KEY_160__\r\n", true)]
    public void PowerShellResultWaitsForClosingDelimiter(string output, bool complete)
    {
        Assert.Equal(complete, HasCompletePowerShellKeyResult(output));
    }

    private static bool HasCompletePowerShellKeyResult(string output)
    {
        const string prefix = "__KEY_";
        int start = output.IndexOf(prefix, StringComparison.Ordinal);
        return start >= 0 && output.IndexOf("__", start + prefix.Length, StringComparison.Ordinal) >= 0;
    }

    private static string? FindPowerShellCore()
    {
        string installed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PowerShell", "7", "pwsh.exe");
        if (File.Exists(installed)) return installed;
        foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string candidate = Path.Combine(directory, "pwsh.exe");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static async Task WithWin32Session(Action<DefaultTerminalInputAdapter, TerminalSessionService, FakeVtProcessor, FakeTransport> test)
    {
        DefaultTerminalInputAdapter adapter = new();
        TerminalSessionService session = new();
        FakeVtProcessor processor = new();
        FakeTransport transport = new();
        Action<byte[], int> onData = (_, _) => { };
        Action<int> onExit = _ => { };
        await session.StartSessionAsync(new StaticTransportFactory(transport), new FakeTransportOptions(TerminalTransportIds.Pipe),
            processor, onData, onExit, _ => { }, () => { }, _ => { });
        processor.SetModeState(processor.ModeState with { Win32InputMode = true });
        try { test(adapter, session, processor, transport); }
        finally { await session.StopSessionAsync(processor, onData, onExit); }
    }
}
