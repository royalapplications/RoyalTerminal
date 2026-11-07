// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.InteropServices;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.GhosttySharp.Native;
using RoyalTerminal.IntegrationTests.TestInfrastructure;
using Xunit;
using static RoyalTerminal.GhosttySharp.Native.GhosttyVtNative;

namespace RoyalTerminal.IntegrationTests;

public sealed class GhosttyOneApiTests
{
    [GhosttyNativeFact]
    public unsafe void VersionAndSizedInitializationFollowTheReleasedContract()
    {
        Assert.StartsWith("1.", Marshal.PtrToStringUTF8(Version()));
        GhosttyTerminalSelectWordOptions options = default;
        new Span<byte>(&options, sizeof(GhosttyTerminalSelectWordOptions)).Fill(0xFF);
        SizedInit(&options, (nuint)sizeof(GhosttyTerminalSelectWordOptions));
        Assert.Equal((nuint)sizeof(GhosttyTerminalSelectWordOptions), options.Size);
        Assert.All(new ReadOnlySpan<byte>((byte*)&options + sizeof(nuint), sizeof(GhosttyTerminalSelectWordOptions) - sizeof(nuint)).ToArray(), value => Assert.Equal(0, value));
    }

    [GhosttyNativeFact]
    public unsafe void FormatterOptionsResetAndUnknownKeysReturnExplicitResults()
    {
        using GhosttyTerminal terminal = new(10, 2);
        Assert.Equal(GhosttyResult.Success, FormatterTerminalNew(0, out nint formatter, terminal.Handle));
        try
        {
            bool trim = false;
            Assert.Equal(GhosttyResult.Success, FormatterSet(formatter, GhosttyFormatterOption.Trim, &trim));
            Assert.Equal(GhosttyResult.Success, FormatterGet(formatter, GhosttyFormatterOption.Trim, &trim));
            Assert.False(trim);
            Assert.Equal(GhosttyResult.Success, FormatterSet(formatter, GhosttyFormatterOption.Trim, null));
            Assert.Equal(GhosttyResult.Success, FormatterGet(formatter, GhosttyFormatterOption.Trim, &trim));
            Assert.True(trim);
            Assert.Equal(GhosttyResult.Unsupported, FormatterSet(formatter, (GhosttyFormatterOption)999, null));
            Assert.Equal(GhosttyResult.Unsupported, FormatterGet(formatter, (GhosttyFormatterOption)999, null));
        }
        finally
        {
            FormatterFree(formatter);
        }
    }

    [GhosttyNativeFact]
    public void InputSettersReportInvalidEnumsThroughManagedWrappers()
    {
        using GhosttyKeyEvent key = new();
        Assert.Throws<InvalidOperationException>(() => key.SetAction((GhosttyVtKeyAction)999));
        Assert.Throws<InvalidOperationException>(() => key.SetKey((GhosttyVtKey)999));
        using GhosttyMouseEvent mouse = new();
        Assert.Throws<InvalidOperationException>(() => mouse.SetAction((GhosttyMouseAction)999));
    }

    [GhosttyNativeFact]
    public void GraphemeCopyHonorsCapacityAndPreservesAdjacentMemory()
    {
        using GhosttyTerminal terminal = new(10, 2);
        using GhosttyRenderState render = new();
        terminal.Write("a\u0301"u8);
        render.Update(terminal);
        render.BeginRows();
        Assert.True(render.MoveNextDirtyRow(out _));
        render.BeginCurrentRowCells();
        Assert.True(render.MoveNextCell());
        uint[] codepoints = [0xDEADBEEF, 0xDEADBEEF, 0xDEADBEEF];
        Assert.Throws<InvalidOperationException>(() => render.GetCurrentCellGraphemes(codepoints.AsSpan(0, 1)));
        Assert.Equal(0xDEADBEEFu, codepoints[1]);
        Assert.Equal(0xDEADBEEFu, codepoints[2]);
        render.GetCurrentCellGraphemes(codepoints.AsSpan(0, 2));
        Assert.Equal((uint)'a', codepoints[0]);
        Assert.Equal(0x301u, codepoints[1]);
        Assert.Equal(0xDEADBEEFu, codepoints[2]);
    }

    [GhosttyNativeFact]
    public void FrozenStyleFlagsKeepUnderlineAndBooleanFieldsIndependent()
    {
        GhosttyStyle style = default;
        style.Bold = true;
        style.Underline = 5;
        style.Italic = true;
        style.Bold = false;
        Assert.False(style.Bold);
        Assert.True(style.Italic);
        Assert.Equal(5, style.Underline);
        Assert.Equal((ushort)0x502, style.Flags);
        using GhosttyTerminal terminal = new(10, 2);
        terminal.Write("\u001b[1;3;4:3;38;2;12;34;56mX"u8);
        style = terminal.GetCursorStyle();
        Assert.True(style.Bold);
        Assert.True(style.Italic);
        Assert.Equal(3, style.Underline);
        Assert.Equal(GhosttyStyleColorTag.Rgb, style.ForegroundColor.Tag);
        Assert.Equal((byte)12, style.ForegroundColor.Value.Rgb.R);
        Assert.Equal((byte)34, style.ForegroundColor.Value.Rgb.G);
        Assert.Equal((byte)56, style.ForegroundColor.Value.Rgb.B);
    }
}
