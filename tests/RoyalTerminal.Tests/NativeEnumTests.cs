// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Tests — Enum and type validation tests.

using RoyalTerminal.GhosttySharp.Native;
using Xunit;

namespace RoyalTerminal.Tests;

/// <summary>
/// Tests for native enum definitions and type layout.
/// </summary>
public class NativeEnumTests
{
    [Fact]
    public void GhosttyKey_HasExpectedValues()
    {
        // Unidentified should be 0
        Assert.Equal(0, (int)GhosttyKey.Unidentified);

        // Letters should exist
        Assert.True(Enum.IsDefined(typeof(GhosttyKey), GhosttyKey.A));
        Assert.True(Enum.IsDefined(typeof(GhosttyKey), GhosttyKey.Z));

        // Digits
        Assert.True(Enum.IsDefined(typeof(GhosttyKey), GhosttyKey.Digit0));
        Assert.True(Enum.IsDefined(typeof(GhosttyKey), GhosttyKey.Digit9));

        // Special keys
        Assert.True(Enum.IsDefined(typeof(GhosttyKey), GhosttyKey.Enter));
        Assert.True(Enum.IsDefined(typeof(GhosttyKey), GhosttyKey.Escape));
        Assert.True(Enum.IsDefined(typeof(GhosttyKey), GhosttyKey.Tab));
        Assert.True(Enum.IsDefined(typeof(GhosttyKey), GhosttyKey.Space));
        Assert.True(Enum.IsDefined(typeof(GhosttyKey), GhosttyKey.Backspace));

        // Arrow keys
        Assert.True(Enum.IsDefined(typeof(GhosttyKey), GhosttyKey.ArrowUp));
        Assert.True(Enum.IsDefined(typeof(GhosttyKey), GhosttyKey.ArrowDown));
        Assert.True(Enum.IsDefined(typeof(GhosttyKey), GhosttyKey.ArrowLeft));
        Assert.True(Enum.IsDefined(typeof(GhosttyKey), GhosttyKey.ArrowRight));

        // Modifiers
        Assert.True(Enum.IsDefined(typeof(GhosttyKey), GhosttyKey.ShiftLeft));
        Assert.True(Enum.IsDefined(typeof(GhosttyKey), GhosttyKey.ShiftRight));
        Assert.True(Enum.IsDefined(typeof(GhosttyKey), GhosttyKey.ControlLeft));
        Assert.True(Enum.IsDefined(typeof(GhosttyKey), GhosttyKey.AltLeft));
        Assert.True(Enum.IsDefined(typeof(GhosttyKey), GhosttyKey.MetaLeft));

        // Numpad
        Assert.True(Enum.IsDefined(typeof(GhosttyKey), GhosttyKey.Numpad0));
        Assert.True(Enum.IsDefined(typeof(GhosttyKey), GhosttyKey.Numpad9));
        Assert.True(Enum.IsDefined(typeof(GhosttyKey), GhosttyKey.NumpadAdd));
        Assert.True(Enum.IsDefined(typeof(GhosttyKey), GhosttyKey.NumpadDecimal));

        // Function keys
        Assert.True(Enum.IsDefined(typeof(GhosttyKey), GhosttyKey.F1));
        Assert.True(Enum.IsDefined(typeof(GhosttyKey), GhosttyKey.F12));
    }

    [Fact]
    public void GhosttyMods_IsFlagsEnum()
    {
        // Verify it's a proper flags enum
        var combined = GhosttyMods.Shift | GhosttyMods.Ctrl;
        Assert.True(combined.HasFlag(GhosttyMods.Shift));
        Assert.True(combined.HasFlag(GhosttyMods.Ctrl));
        Assert.False(combined.HasFlag(GhosttyMods.Alt));
    }

    [Fact]
    public void GhosttyMods_BitValues()
    {
        Assert.Equal(0, (int)GhosttyMods.None);
        Assert.Equal(1 << 0, (int)GhosttyMods.Shift);
        Assert.Equal(1 << 1, (int)GhosttyMods.Ctrl);
        Assert.Equal(1 << 2, (int)GhosttyMods.Alt);
        Assert.Equal(1 << 3, (int)GhosttyMods.Super);
    }

    [Fact]
    public void GhosttyInputAction_HasExpectedValues()
    {
        Assert.True(Enum.IsDefined(typeof(GhosttyInputAction), GhosttyInputAction.Press));
        Assert.True(Enum.IsDefined(typeof(GhosttyInputAction), GhosttyInputAction.Release));
        Assert.True(Enum.IsDefined(typeof(GhosttyInputAction), GhosttyInputAction.Repeat));
    }

    [Fact]
    public void GhosttyMouseState_HasExpectedValues()
    {
        Assert.Equal(0, (int)GhosttyMouseState.Release);
        Assert.Equal(1, (int)GhosttyMouseState.Press);
    }

    [Fact]
    public void GhosttyMouseButton_HasExpectedValues()
    {
        Assert.True(Enum.IsDefined(typeof(GhosttyMouseButton), GhosttyMouseButton.Left));
        Assert.True(Enum.IsDefined(typeof(GhosttyMouseButton), GhosttyMouseButton.Right));
        Assert.True(Enum.IsDefined(typeof(GhosttyMouseButton), GhosttyMouseButton.Middle));
    }

    [Fact]
    public void GhosttyColorScheme_HasExpectedValues()
    {
        Assert.True(Enum.IsDefined(typeof(GhosttyColorScheme), GhosttyColorScheme.Light));
        Assert.True(Enum.IsDefined(typeof(GhosttyColorScheme), GhosttyColorScheme.Dark));
    }

    [Fact]
    public void GhosttySplitDirection_HasExpectedValues()
    {
        Assert.True(Enum.IsDefined(typeof(GhosttySplitDirection), GhosttySplitDirection.Right));
        Assert.True(Enum.IsDefined(typeof(GhosttySplitDirection), GhosttySplitDirection.Down));
    }

    [Fact]
    public void GhosttyClipboard_HasExpectedValues()
    {
        Assert.Equal(0, (int)GhosttyClipboard.Standard);
        Assert.Equal(1, (int)GhosttyClipboard.Selection);
    }

    [Fact]
    public void GhosttyInputKey_Struct_HasExpectedLayout()
    {
        var key = new GhosttyInputKey
        {
            Action = GhosttyInputAction.Press,
            Mods = GhosttyMods.Ctrl | GhosttyMods.Shift,
            Keycode = (uint)GhosttyKey.A,
            Composing = false,
        };

        Assert.Equal(GhosttyInputAction.Press, key.Action);
        Assert.True(key.Mods.HasFlag(GhosttyMods.Ctrl));
        Assert.True(key.Mods.HasFlag(GhosttyMods.Shift));
        Assert.Equal((uint)GhosttyKey.A, key.Keycode);
        Assert.False(key.Composing);
    }

    [Fact]
    public void GhosttySurfaceSize_Struct_HasExpectedFields()
    {
        var size = new GhosttySurfaceSize
        {
            Columns = 80,
            Rows = 24,
            WidthPx = 640,
            HeightPx = 384,
            CellWidthPx = 8,
            CellHeightPx = 16,
        };

        Assert.Equal(80, size.Columns);
        Assert.Equal(24, size.Rows);
        Assert.Equal(640u, size.WidthPx);
        Assert.Equal(384u, size.HeightPx);
        Assert.Equal(8u, size.CellWidthPx);
        Assert.Equal(16u, size.CellHeightPx);
    }
}
