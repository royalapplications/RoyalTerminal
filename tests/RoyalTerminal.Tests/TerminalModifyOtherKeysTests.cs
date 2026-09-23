// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.GhosttySharp.Native;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Snapshots;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class TerminalModifyOtherKeysTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("\u001b[>4;2m", true)]
    [InlineData("\u001b[>4:2m", true)]
    [InlineData("\u001b[>m", false)]
    [InlineData("\u001b[>0m", false)]
    [InlineData("\u001b[>1;2m", false)]
    [InlineData("\u001b[>2;2m", false)]
    [InlineData("\u001b[>4m", false)]
    [InlineData("\u001b[>4;0m", false)]
    [InlineData("\u001b[>4;1m", false)]
    [InlineData("\u001b[>4;3m", false)]
    [InlineData("\u001b[>0;2m", false)]
    [InlineData("\u001b[>4;2;0m", null)]
    [InlineData("\u001b[>3;2m", null)]
    [InlineData("\u001b[?4;2m", null)]
    [InlineData("\u001b[>4;2 m", null)]
    [InlineData("\u001b[>n", false)]
    [InlineData("\u001b[>4n", false)]
    [InlineData("\u001b[>999;99;2n", false)]
    [InlineData("\u001b[>4:2n", null)]
    public void ParserMatchesNativeAcrossEverySplit(string sequence, bool? value)
    {
        if (!Available()) return;
        byte[] bytes = Encoding.UTF8.GetBytes(sequence);
        foreach (bool initial in new[] { false, true })
        for (int split = 0; split <= bytes.Length; split++)
        {
            using GhosttyTerminal native = new(8, 3);
            using BasicVtProcessor managed = new(new TerminalScreen(8, 3));
            if (initial) { native.Write("\u001b[>4;2m"u8); managed.Process("\u001b[>4;2m"u8); }
            native.Write(bytes.AsSpan(0, split)); managed.Process(bytes.AsSpan(0, split));
            native.Write(bytes.AsSpan(split)); managed.Process(bytes.AsSpan(split));
            Assert.Equal(value ?? initial, native.GetModifyOtherKeys2());
            Assert.Equal(native.GetModifyOtherKeys2(), managed.ModifyOtherKeys2);
        }
    }

    [Theory]
    [InlineData("A", "a")]
    [InlineData("A", "A")]
    [InlineData("A", "é")]
    [InlineData("A", "😀")]
    [InlineData("A", "ab")]
    [InlineData("A", null)]
    [InlineData("D1", "!")]
    [InlineData("D1", "1")]
    [InlineData("Space", " ")]
    [InlineData("OemOpenBrackets", "[")]
    [InlineData("Oem2", "?")]
    [InlineData("OemSemicolon", ";")]
    [InlineData("Back", null)]
    [InlineData("Back", "\u007f")]
    [InlineData("Tab", null)]
    [InlineData("Return", null)]
    [InlineData("Escape", null)]
    [InlineData("Return", "committed")]
    [InlineData("Up", "a")]
    [InlineData("F1", "a")]
    public void ExtensionSequencesMatchNativeForEveryModifierAndBackarrowMode(string key, string? text)
    {
        if (!Available()) return;
        using GhosttyVtProcessor native = new(new TerminalScreen(8, 3));
        using BasicVtProcessor managed = new(new TerminalScreen(8, 3));
        native.Process("\u001b[>4;2m"u8); managed.Process("\u001b[>4;2m"u8);
        foreach (bool backarrow in new[] { false, true })
        {
            byte[] mode = Encoding.ASCII.GetBytes(backarrow ? "\u001b[?67h" : "\u001b[?67l");
            native.Process(mode); managed.Process(mode);
            for (int mods = 0; mods < 16; mods++)
            foreach (TerminalInputAction action in new[] { TerminalInputAction.Press, TerminalInputAction.Release })
            {
                TerminalKeyEncodingRequest request = new(key, action, text, (TerminalModifiers)mods);
                native.TryEncodeKey(request, out byte[] expected);
                bool encoded = managed.TryEncodeKey(request, out byte[] actual);
                // This capability handles mode-2 extensions, not all unchanged
                // legacy keys. Those continue through the UI fallback path.
                if (encoded) Assert.Equal(expected, actual);
                if (expected.AsSpan().StartsWith("\u001b[27;"u8)) Assert.True(encoded, $"Missing {key}, {mods}, {text}, {action}");
                Assert.False(managed.TryEncodeKey(request with { IsComposing = true }, out _));
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StateIsGlobalLiveAndSessionScoped(bool native)
    {
        if (native && !Available()) return;
        using IVtProcessor processor = native ? new GhosttyVtProcessor(new TerminalScreen(8, 3)) : new BasicVtProcessor(new TerminalScreen(8, 3));
        ITerminalModifyOtherKeysStateSource state = (ITerminalModifyOtherKeysStateSource)processor;
        processor.Process("\u001b[?2026h\u001b[>4;2m"u8); Assert.True(state.ModifyOtherKeys2);
        processor.Process("\u001b[?1049h\u001b[!p\u001b[?1049l"u8); Assert.True(state.ModifyOtherKeys2);
        processor.Process("\u001bc"u8); Assert.False(state.ModifyOtherKeys2);
        foreach (bool preserve in new[] { true, false })
        {
            processor.Process("\u001b[>4;2m"u8);
            ((ITerminalSessionHistoryController)processor).PrepareForNewSession(preserve);
            Assert.False(state.ModifyOtherKeys2);
        }
    }

    [Fact]
    public void KittyPrecedesLegacyExtension()
    {
        using BasicVtProcessor managed = new(new TerminalScreen(8, 3));
        managed.Process("\u001b[>4;2m\u001b[>1u"u8);
        Assert.True(managed.ModifyOtherKeys2);
        Assert.False(managed.TryEncodeKey(new("A", TerminalInputAction.Press, "a", TerminalModifiers.Control), out _));
        managed.Process("\u001b[<u"u8);
        Assert.True(managed.TryEncodeKey(new("A", TerminalInputAction.Press, "a", TerminalModifiers.Control), out byte[] bytes));
        Assert.Equal("\u001b[27;5;97~", Encoding.ASCII.GetString(bytes));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SnapshotInstallsKeyboardFlagWithoutInputReplay(bool enabled)
    {
        if (!Available()) return;
        List<SnapshotTestRecord> records = SnapshotTestRecords.Fixture();
        records[0].Payload[33] = enabled ? (byte)1 : (byte)0;
        for (int i = 0; i < records.Count; i++)
            if (records[i].Tag == GhosttySnapshotRecordTag.Continuation) records[i] = new(records[i].Tag, []);
        using GhosttyTerminal native = GhosttySnapshot.Decode(SnapshotTestRecords.Encode(records));
        using GhosttySnapshotStateReader reader = new(GhosttySnapshot.Encode(native), new());
        using BasicVtProcessor managed = new(new TerminalScreen(2, 3));
        managed.InstallSnapshotModes(reader.ReadReady().Terminal.Header);
        Assert.Equal(enabled, native.GetModifyOtherKeys2()); Assert.Equal(enabled, managed.ModifyOtherKeys2);
    }

    [Fact]
    public unsafe void NativeStateGuardsAndWarmReadAreAllocationFree()
    {
        if (!Available()) return;
        using GhosttyTerminal native = new(8, 3);
        byte value = 77;
        Assert.Equal(GhosttyVtNative.GhosttyResult.InvalidValue, GhosttyVtNative.ModifyOtherKeys2(0, &value));
        Assert.Equal(77, value);
        Assert.Equal(GhosttyVtNative.GhosttyResult.InvalidValue, GhosttyVtNative.ModifyOtherKeys2(native.Handle, null));
        for (int i = 0; i < 1000; i++) _ = native.GetModifyOtherKeys2();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) _ = native.GetModifyOtherKeys2();
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        native.Dispose(); Assert.Throws<ObjectDisposedException>(() => native.GetModifyOtherKeys2());
    }

    private bool Available()
    {
        bool available = GhosttyVtProcessor.IsAvailable(); output.WriteLine($"Native modifyOtherKeys available: {available}"); return available;
    }
}
