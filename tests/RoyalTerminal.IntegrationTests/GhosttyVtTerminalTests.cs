// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.IntegrationTests — Official libghostty-vt terminal and render-state coverage.

using System.Runtime.InteropServices;
using System.Text;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.GhosttySharp.Native;
using Xunit;

namespace RoyalTerminal.IntegrationTests;

public class GhosttyVtTerminalTests
{
    [Fact]
    public unsafe void OfficialTerminalAndRenderState_WriteTextAndGridRefExposeCells()
    {
        if (!GhosttyVtNative.IsAvailable())
        {
            return;
        }

        using GhosttyTerminal terminal = new(80, 24);
        using GhosttyRenderState renderState = new();

        terminal.Write("ABC"u8);
        renderState.Update(terminal);

        Assert.Equal((ushort)80, terminal.GetColumns());
        Assert.Equal((ushort)24, terminal.GetRows());
        Assert.Equal((ushort)80, renderState.GetColumns());
        Assert.Equal((ushort)24, renderState.GetRows());

        string rowText = ReadLeadingRowText(renderState, 3);
        Assert.Equal("ABC", rowText);

        Assert.True(renderState.TryGetCursorViewport(out ushort cursorX, out ushort cursorY, out bool wideTail));
        Assert.Equal((ushort)3, cursorX);
        Assert.Equal((ushort)0, cursorY);
        Assert.False(wideTail);

        Assert.True(terminal.TryGetGridReference(GhosttyVtNative.GhosttyPoint.Active(0, 0), out GhosttyVtNative.GhosttyGridRef reference));
        Assert.Equal(GhosttyVtNative.GhosttyResult.Success, GhosttyVtNative.GridRefCell(in reference, out ulong cell));
        Assert.Equal((uint)'A', GetCellCodepoint(cell));
    }

    [Fact]
    public void OfficialRenderState_ExposesGraphemeClusters()
    {
        if (!GhosttyVtNative.IsAvailable())
        {
            return;
        }

        using GhosttyTerminal terminal = new(80, 24);
        using GhosttyRenderState renderState = new();

        terminal.Write(Encoding.UTF8.GetBytes("Ae\u0301B"));
        renderState.Update(terminal);

        renderState.BeginRows();
        Assert.True(renderState.MoveNextRow());
        renderState.BeginCurrentRowCells();
        Assert.True(renderState.MoveNextCell());
        Assert.True(renderState.MoveNextCell());

        Assert.Equal(2u, renderState.GetCurrentCellGraphemeLength());
        uint[] grapheme = new uint[2];
        renderState.GetCurrentCellGraphemes(grapheme);
        Assert.Equal((uint)'e', grapheme[0]);
        Assert.Equal(0x0301u, grapheme[1]);

        GhosttyVtNative.GhosttyStyle style = renderState.GetCurrentCellStyle();
        Assert.True(GhosttyVtNative.StyleIsDefault(in style));
    }

    [Fact]
    public void OfficialTerminal_ModesResizeResetScrollAndFormatterWork()
    {
        if (!GhosttyVtNative.IsAvailable())
        {
            return;
        }

        using GhosttyTerminal terminal = new(10, 5);
        using GhosttyRenderState renderState = new();

        terminal.Write("\u001b[?2004h\u001b[?1049hHello"u8);

        Assert.True(terminal.GetMode(GhosttyVtNative.ModeBracketedPaste));
        Assert.Equal(GhosttyVtNative.GhosttyTerminalScreen.Alternate, terminal.GetActiveScreen());

        terminal.Resize(12, 6, 8, 16);
        Assert.Equal((ushort)12, terminal.GetColumns());
        Assert.Equal((ushort)6, terminal.GetRows());

        using (GhosttyFormatter formatter = new(terminal, GhosttyVtNative.GhosttyFormatterFormat.Plain))
        {
            string formatted = formatter.FormatToString();
            Assert.Contains("Hello", formatted);
        }

        terminal.Reset();
        Assert.False(terminal.GetMode(GhosttyVtNative.ModeBracketedPaste));
        Assert.Equal(GhosttyVtNative.GhosttyTerminalScreen.Primary, terminal.GetActiveScreen());

        for (int i = 0; i < 16; i++)
        {
            terminal.Write(Encoding.UTF8.GetBytes($"line-{i}\r\n"));
        }

        GhosttyVtNative.GhosttyTerminalScrollbar bottom = terminal.GetScrollbar();
        Assert.True(bottom.Total >= bottom.Length);

        terminal.ScrollViewport(GhosttyVtNative.GhosttyTerminalScrollViewport.Top());
        GhosttyVtNative.GhosttyTerminalScrollbar top = terminal.GetScrollbar();
        Assert.Equal(0ul, top.Offset);

        terminal.ScrollViewport(GhosttyVtNative.GhosttyTerminalScrollViewport.Bottom());
        GhosttyVtNative.GhosttyTerminalScrollbar restored = terminal.GetScrollbar();
        Assert.True(restored.Offset >= top.Offset);

        renderState.Update(terminal);
        Assert.Equal((ushort)12, renderState.GetColumns());
        Assert.Equal((ushort)6, renderState.GetRows());
    }

    [Fact]
    public void OfficialTerminal_EffectCallbacks_FireForBellTitleAndWritePty()
    {
        if (!GhosttyVtNative.IsAvailable())
        {
            return;
        }

        using GhosttyTerminal terminal = new(80, 24);

        int bellCount = 0;
        string title = string.Empty;
        string writePty = string.Empty;

        GhosttyVtNative.GhosttyTerminalBellCallback bell = (_, _) => bellCount++;
        GhosttyVtNative.GhosttyTerminalTitleChangedCallback titleChanged = (_, _) => title = terminal.GetTitle();
        GhosttyVtNative.GhosttyTerminalWritePtyCallback writePtyCallback = (_, _, data, len) =>
        {
            if (data == nint.Zero || len == 0)
            {
                return;
            }

            byte[] buffer = new byte[checked((int)len)];
            Marshal.Copy(data, buffer, 0, buffer.Length);
            writePty = Encoding.UTF8.GetString(buffer);
        };

        terminal.SetBellCallback(Marshal.GetFunctionPointerForDelegate(bell));
        terminal.SetTitleChangedCallback(Marshal.GetFunctionPointerForDelegate(titleChanged));
        terminal.SetWritePtyCallback(Marshal.GetFunctionPointerForDelegate(writePtyCallback));

        terminal.Write("\a"u8);
        terminal.Write("\u001b]2;RoyalTerminal\a"u8);
        terminal.Write("\u001b[5n"u8);

        Assert.Equal(1, bellCount);
        Assert.Equal("RoyalTerminal", title);
        Assert.Equal("RoyalTerminal", terminal.GetTitle());
        Assert.Equal("\u001b[0n", writePty);
    }

    [Fact]
    public void ProtocolHelpers_EncodeReportsAndBuildInfo()
    {
        if (!GhosttyVtNative.IsAvailable())
        {
            return;
        }

        GhosttyVtHelpers.GhosttyBuildFeatures features = GhosttyVtHelpers.GetBuildFeatures();
        Assert.InRange((int)features.OptimizeMode, 0, 3);

        Assert.Equal("\u001b[I", GhosttyVtHelpers.EncodeFocusString(GhosttyVtNative.GhosttyFocusEvent.Gained));
        Assert.Equal(
            "\u001b[?2004;1$y",
            GhosttyVtHelpers.EncodeModeReportString(
                GhosttyVtNative.ModeBracketedPaste,
                GhosttyVtNative.GhosttyModeReportState.Set));

        string sizeReport = GhosttyVtHelpers.EncodeSizeReportString(
            GhosttyVtNative.GhosttySizeReportStyle.Csi18T,
            new GhosttyVtNative.GhosttySizeReportSize
            {
                Rows = 24,
                Columns = 80,
                CellWidth = 8,
                CellHeight = 16,
            });
        Assert.Equal("\u001b[8;24;80t", sizeReport);
    }

    [Fact]
    public void MouseEncoder_FromTerminalState_EncodesSgrMousePress()
    {
        if (!GhosttyVtNative.IsAvailable())
        {
            return;
        }

        using GhosttyTerminal terminal = new(80, 24);
        using GhosttyMouseEncoder encoder = new();
        using GhosttyMouseEvent mouseEvent = new();

        terminal.Write("\u001b[?1000h\u001b[?1006h"u8);
        encoder.SetFromTerminal(terminal);
        encoder.SetSize(new GhosttyVtNative.GhosttyMouseEncoderSize
        {
            Size = (nuint)Marshal.SizeOf<GhosttyVtNative.GhosttyMouseEncoderSize>(),
            ScreenWidth = 640,
            ScreenHeight = 384,
            CellWidth = 8,
            CellHeight = 16,
        });

        mouseEvent.SetAction(GhosttyVtNative.GhosttyMouseAction.Press);
        mouseEvent.SetButton(GhosttyVtNative.GhosttyMouseButtonId.Left);
        mouseEvent.SetPosition(8, 16);

        Assert.True(mouseEvent.TryGetButton(out GhosttyVtNative.GhosttyMouseButtonId button));
        Assert.Equal(GhosttyVtNative.GhosttyMouseButtonId.Left, button);
        Assert.True(terminal.GetMouseTracking());

        string encoded = encoder.EncodeToString(mouseEvent);
        Assert.StartsWith("\u001b[<", encoded);
        Assert.EndsWith("M", encoded);
    }

    [Fact]
    public void OfficialTerminal_SplitEscapeSequenceAcrossWrites_DoesNotLeakControlBytes()
    {
        if (!GhosttyVtNative.IsAvailable())
        {
            return;
        }

        using GhosttyTerminal terminal = new(80, 24);
        using GhosttyRenderState renderState = new();

        terminal.Write("\u001b["u8);
        terminal.Write("31mRed"u8);
        renderState.Update(terminal);

        Assert.Equal("Red", ReadLeadingRowText(renderState, 3));
    }

    [Fact]
    public void OfficialTerminal_Resize_PreservesCellsAndAcceptsAdditionalWrites()
    {
        if (!GhosttyVtNative.IsAvailable())
        {
            return;
        }

        using GhosttyTerminal terminal = new(80, 24);
        using GhosttyRenderState renderState = new();

        terminal.Write("Before"u8);
        terminal.Resize(120, 40);
        terminal.Write(" After"u8);
        renderState.Update(terminal);

        Assert.Equal((ushort)120, terminal.GetColumns());
        Assert.Equal((ushort)40, terminal.GetRows());
        Assert.Equal("Before After", ReadLeadingRowText(renderState, 12));
    }

    private static unsafe uint GetCellCodepoint(ulong cell)
    {
        uint codepoint = 0;
        Assert.Equal(
            GhosttyVtNative.GhosttyResult.Success,
            GhosttyVtNative.CellGet(cell, GhosttyVtNative.GhosttyCellData.Codepoint, &codepoint));
        return codepoint;
    }

    private static unsafe string ReadLeadingRowText(GhosttyRenderState renderState, int count)
    {
        renderState.BeginRows();
        Assert.True(renderState.MoveNextRow());
        renderState.BeginCurrentRowCells();

        StringBuilder builder = new(count);
        for (int i = 0; i < count; i++)
        {
            Assert.True(renderState.MoveNextCell());
            builder.Append(char.ConvertFromUtf32((int)GetCellCodepoint(renderState.GetCurrentCellRaw())));
        }

        return builder.ToString();
    }
}
