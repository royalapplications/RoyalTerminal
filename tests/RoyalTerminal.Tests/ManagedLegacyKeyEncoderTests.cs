// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class ManagedLegacyKeyEncoderTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Keys()
    {
        foreach (string key in new[] { "Up", "Down", "Left", "Right", "Home", "End", "Insert", "Delete", "PageUp", "PageDown", "Apps",
            "F1", "F2", "F3", "F4", "F5", "F12", "F13", "F16", "F20", "F21", "F24", "F25",
            "NumPad0", "NumPad9", "Decimal", "Divide", "Multiply", "Subtract", "Add", "Back", "Return", "Escape", "Tab" }) yield return [key];
    }

    [Theory]
    [MemberData(nameof(Keys))]
    public void EveryPcModifierAndTerminalModeCombinationMatchesNative(string key)
    {
        bool available = GhosttyVtProcessor.IsAvailable();
        output.WriteLine($"Native legacy differential available: {available}");
        if (!available) return;
        using BasicVtProcessor managed = new(new TerminalScreen(8, 3));
        using GhosttyVtProcessor native = new(new TerminalScreen(8, 3));
        for (int modes = 0; modes < 32; modes++)
        {
            byte[] state = Encoding.ASCII.GetBytes($"\u001b[?1{Set(1)}\u001b[?66{Set(2)}\u001b[?1035{Set(4)}\u001b[?67{Set(8)}\u001b[>4;{((modes & 16) == 0 ? 0 : 2)}m");
            managed.Process(state); native.Process(state);
            for (int mods = 0; mods < 64; mods++)
            foreach (TerminalInputAction action in new[] { TerminalInputAction.Press, TerminalInputAction.Repeat, TerminalInputAction.Release })
                Compare(managed, native, new(key, action, null, (TerminalModifiers)mods), modes);
            string Set(int bit) => (modes & bit) == 0 ? "l" : "h";
        }
    }

    [Theory]
    [InlineData("A", "a", 0U)]
    [InlineData("A", "A", 0U)]
    [InlineData("A", "É", 233U)]
    [InlineData("A", "é", 233U)]
    [InlineData("A", "😀", 0U)]
    [InlineData("A", "\ud800", 0U)]
    [InlineData("A", null, 233U)]
    [InlineData("A", "ab", 0U)]
    [InlineData("C", "с", 0U)]
    [InlineData("I", "i", 0U)]
    [InlineData("M", "m", 0U)]
    [InlineData("OemOpenBrackets", "[", 0U)]
    [InlineData("D2", "@", 0U)]
    [InlineData("OemMinus", "_", 0U)]
    [InlineData("Oem2", "?", 0U)]
    [InlineData("Space", " ", 0U)]
    [InlineData("Return", "IME", 0U)]
    [InlineData("Return", "é", 0U)]
    [InlineData("Return", "\r", 0U)]
    [InlineData("Back", "IME", 0U)]
    [InlineData("Escape", "IME", 0U)]
    [InlineData("LeftShift", "preedit", 0U)]
    public void TextLayoutCompositionAndConsumedModifiersMatchNative(string key, string? text, uint unshifted)
    {
        if (!GhosttyVtProcessor.IsAvailable()) return;
        using BasicVtProcessor managed = new(new TerminalScreen(8, 3));
        using GhosttyVtProcessor native = new(new TerminalScreen(8, 3));
        for (int modes = 0; modes < 4; modes++)
        {
            byte[] state = Encoding.ASCII.GetBytes($"\u001b[?1036{((modes & 1) == 0 ? 'l' : 'h')}\u001b[>4;{((modes & 2) == 0 ? 0 : 2)}m");
            managed.Process(state); native.Process(state);
            for (int mods = 0; mods < 64; mods++)
            for (int consumed = 0; consumed < 16; consumed++)
            {
                TerminalKeyEncodingRequest request = new(key, TerminalInputAction.Repeat, text, (TerminalModifiers)mods,
                    UnshiftedCodepoint: unshifted, ConsumedModifiers: (TerminalModifiers)consumed);
                Compare(managed, native, request, modes);
                Compare(managed, native, request with { IsComposing = true }, modes);
                Compare(managed, native, request with { Action = TerminalInputAction.Release }, modes);
            }
        }
    }

    private static void Compare(BasicVtProcessor managed, GhosttyVtProcessor native, TerminalKeyEncodingRequest request, int modes)
    {
        bool expected = native.TryEncodeKey(request, out byte[] nativeBytes);
        bool actual = managed.TryEncodeKey(request, out byte[] managedBytes);
        Assert.True(expected == actual && nativeBytes.AsSpan().SequenceEqual(managedBytes),
            $"modes={modes} request={request}: {Convert.ToHexString(nativeBytes)} != {Convert.ToHexString(managedBytes)}");
    }
}
