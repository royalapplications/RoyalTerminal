// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class ManagedKittyKeyEncoderTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Keys()
    {
        foreach (string key in new[] { "A", "D1", "Space", "Oem2", "Return", "Back", "Tab", "Escape", "Up", "Home", "Insert", "Delete",
            "PageDown", "F1", "F2", "F3", "F4", "F5", "F12", "F13", "F24", "NumPad0", "NumPad9", "Decimal", "Divide", "Add",
            "LeftShift", "RightShift", "LeftCtrl", "RightCtrl", "LeftAlt", "RightAlt", "LWin", "RWin", "CapsLock", "NumLock", "Scroll", "Pause" })
            yield return [key];
    }

    [Theory]
    [MemberData(nameof(Keys))]
    public void EveryFlagCombinationModifierAndActionMatchesNative(string key)
    {
        bool available = GhosttyVtProcessor.IsAvailable();
        output.WriteLine($"Native Kitty encoder differential available: {available}");
        if (!available) return;
        using BasicVtProcessor managed = new(new TerminalScreen(8, 3));
        using GhosttyVtProcessor native = new(new TerminalScreen(8, 3));
        for (int flags = 1; flags <= 31; flags++)
        {
            byte[] mode = Encoding.ASCII.GetBytes($"\u001b[={flags}u");
            managed.Process(mode); native.Process(mode);
            for (int modifiers = 0; modifiers < 64; modifiers++)
            foreach (TerminalInputAction action in new[] { TerminalInputAction.Press, TerminalInputAction.Repeat, TerminalInputAction.Release })
            {
                TerminalKeyEncodingRequest request = new(key, action, key == "A" ? "A" : null, (TerminalModifiers)modifiers);
                Compare(request);
            }
        }
        void Compare(TerminalKeyEncodingRequest request)
        {
            bool expected = native.TryEncodeKey(request, out byte[] nativeBytes);
            bool actual = managed.TryEncodeKey(request, out byte[] managedBytes);
            Assert.True(expected == actual && nativeBytes.AsSpan().SequenceEqual(managedBytes),
                $"flags={managed.KittyKeyboardFlags} request={request}: {Convert.ToHexString(nativeBytes)} != {Convert.ToHexString(managedBytes)}");
        }
    }

    [Theory]
    [InlineData("A", "é", 233U)]
    [InlineData("A", "É", 233U)]
    [InlineData("A", "😀a", 0U)]
    [InlineData("A", "\ud800", 0U)]
    [InlineData("Return", "IME", 0U)]
    [InlineData("Return", "\r", 0U)]
    [InlineData("Return", "\r\n", 0U)]
    [InlineData("Back", "IME", 0U)]
    [InlineData("LeftShift", "preedit", 0U)]
    [InlineData("A", null, 233U)]
    public void LayoutAlternatesConsumedModifiersAndCompositionMatchNative(string key, string? text, uint unshifted)
    {
        if (!GhosttyVtProcessor.IsAvailable()) return;
        using BasicVtProcessor managed = new(new TerminalScreen(8, 3));
        using GhosttyVtProcessor native = new(new TerminalScreen(8, 3));
        for (int flags = 1; flags <= 31; flags++)
        {
            byte[] mode = Encoding.ASCII.GetBytes($"\u001b[={flags}u");
            managed.Process(mode); native.Process(mode);
            for (int mods = 0; mods < 16; mods++)
            foreach (bool composing in new[] { false, true })
            foreach (TerminalInputAction action in new[] { TerminalInputAction.Press, TerminalInputAction.Repeat, TerminalInputAction.Release })
            {
                TerminalKeyEncodingRequest request = new(key, action, text, (TerminalModifiers)mods, composing, unshifted, TerminalModifiers.Shift | TerminalModifiers.Alt);
                bool expected = native.TryEncodeKey(request, out byte[] nativeBytes);
                bool actual = managed.TryEncodeKey(request, out byte[] managedBytes);
                Assert.True(expected == actual && nativeBytes.AsSpan().SequenceEqual(managedBytes),
                    $"flags={flags} request={request}: {Convert.ToHexString(nativeBytes)} != {Convert.ToHexString(managedBytes)}");
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvalidActionsAndScalarsAreRejectedAndSuppressedKeysDoNotAllocate(bool native)
    {
        if (native && !GhosttyVtProcessor.IsAvailable()) return;
        using IVtProcessor processor = native ? new GhosttyVtProcessor(new TerminalScreen(8, 3)) : new BasicVtProcessor(new TerminalScreen(8, 3));
        ITerminalKeySequenceEncoderSource encoder = (ITerminalKeySequenceEncoderSource)processor;
        processor.Process("\u001b[=31u"u8);
        Assert.False(encoder.TryEncodeKey(new("A", (TerminalInputAction)99, "a", 0), out _));
        Assert.False(encoder.TryEncodeKey(new("A", TerminalInputAction.Press, "a", 0, UnshiftedCodepoint: 0xD800), out _));
        TerminalKeyEncodingRequest suppressed = new("A", TerminalInputAction.Press, null, 0, IsComposing: true);
        for (int i = 0; i < 1000; i++) encoder.TryEncodeKey(suppressed, out _);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) Assert.False(encoder.TryEncodeKey(suppressed, out _));
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
