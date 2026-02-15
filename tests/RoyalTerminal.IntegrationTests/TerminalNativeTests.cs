// Licensed under the MIT License.
// GhosttySharp.IntegrationTests — libghostty-terminal native integration tests.

using GhosttySharp.Native;
using Xunit;

namespace GhosttySharp.IntegrationTests;

/// <summary>
/// Integration tests for the native terminal library (libghostty-terminal).
/// </summary>
public class TerminalNativeTests
{
    [Fact]
    public void SelfTest_VtParsingWorks()
    {
        if (!GhosttyTerminalNative.IsAvailable())
        {
            // Skip if native library not available
            return;
        }

        var result = GhosttyTerminalNative.SelfTest();
        Assert.Equal(0u, result);
    }

    [Fact]
    public void Terminal_CreateAndFree()
    {
        if (!GhosttyTerminalNative.IsAvailable())
            return;

        var handle = GhosttyTerminalNative.TerminalNew(80, 24, 1000);
        Assert.NotEqual(nint.Zero, handle);
        GhosttyTerminalNative.TerminalFree(handle);
    }

    [Fact]
    public void Terminal_GetDimensions()
    {
        if (!GhosttyTerminalNative.IsAvailable())
            return;

        var handle = GhosttyTerminalNative.TerminalNew(120, 40, 0);
        Assert.NotEqual(nint.Zero, handle);

        Assert.Equal(120u, GhosttyTerminalNative.TerminalGetCols(handle));
        Assert.Equal(40u, GhosttyTerminalNative.TerminalGetRows(handle));

        GhosttyTerminalNative.TerminalFree(handle);
    }

    [Fact]
    public unsafe void Terminal_ProcessData_CellsPopulated()
    {
        if (!GhosttyTerminalNative.IsAvailable())
            return;

        var handle = GhosttyTerminalNative.TerminalNew(80, 24, 0);
        Assert.NotEqual(nint.Zero, handle);

        // Process "ABC" — should appear in row 0, cols 0-2
        var data = "ABC"u8;
        fixed (byte* ptr = data)
        {
            GhosttyTerminalNative.TerminalProcess(handle, ptr, (nuint)data.Length);
        }

        // Read row 0
        var cells = stackalloc GhosttyTerminalNative.CellInfo[80];
        var filled = GhosttyTerminalNative.TerminalGetRowCells(handle, 0, cells, 80);
        Assert.True(filled >= 3);
        Assert.Equal((uint)'A', cells[0].Codepoint);
        Assert.Equal((uint)'B', cells[1].Codepoint);
        Assert.Equal((uint)'C', cells[2].Codepoint);

        GhosttyTerminalNative.TerminalFree(handle);
    }

    [Fact]
    public unsafe void Terminal_SgrSequence_NotLeaked()
    {
        if (!GhosttyTerminalNative.IsAvailable())
            return;

        var handle = GhosttyTerminalNative.TerminalNew(80, 24, 0);

        // Process ESC[37mHello — the "37m" should NOT appear as text
        var data = "\x1B[37mHello"u8;
        fixed (byte* ptr = data)
        {
            GhosttyTerminalNative.TerminalProcess(handle, ptr, (nuint)data.Length);
        }

        var cells = stackalloc GhosttyTerminalNative.CellInfo[80];
        var filled = GhosttyTerminalNative.TerminalGetRowCells(handle, 0, cells, 80);

        // Cells should start with 'H','e','l','l','o' — NOT '3','7','m'
        Assert.Equal((uint)'H', cells[0].Codepoint);
        Assert.Equal((uint)'e', cells[1].Codepoint);
        Assert.Equal((uint)'l', cells[2].Codepoint);
        Assert.Equal((uint)'l', cells[3].Codepoint);
        Assert.Equal((uint)'o', cells[4].Codepoint);

        GhosttyTerminalNative.TerminalFree(handle);
    }

    [Fact]
    public unsafe void Terminal_SplitSequence_AcrossChunks()
    {
        if (!GhosttyTerminalNative.IsAvailable())
            return;

        var handle = GhosttyTerminalNative.TerminalNew(80, 24, 0);

        // Chunk 1: ESC [
        var chunk1 = "\x1B["u8;
        fixed (byte* ptr = chunk1)
        {
            GhosttyTerminalNative.TerminalProcess(handle, ptr, (nuint)chunk1.Length);
        }

        // Chunk 2: 31mRed
        var chunk2 = "31mRed"u8;
        fixed (byte* ptr = chunk2)
        {
            GhosttyTerminalNative.TerminalProcess(handle, ptr, (nuint)chunk2.Length);
        }

        var cells = stackalloc GhosttyTerminalNative.CellInfo[80];
        GhosttyTerminalNative.TerminalGetRowCells(handle, 0, cells, 80);

        // Cells should contain 'R','e','d' — NOT '3','1','m'
        Assert.Equal((uint)'R', cells[0].Codepoint);
        Assert.Equal((uint)'e', cells[1].Codepoint);
        Assert.Equal((uint)'d', cells[2].Codepoint);

        GhosttyTerminalNative.TerminalFree(handle);
    }

    [Fact]
    public unsafe void Terminal_Resize_UpdatesDimensions()
    {
        if (!GhosttyTerminalNative.IsAvailable())
            return;

        var handle = GhosttyTerminalNative.TerminalNew(80, 24, 0);
        Assert.NotEqual(nint.Zero, handle);

        // Initial dimensions
        Assert.Equal(80u, GhosttyTerminalNative.TerminalGetCols(handle));
        Assert.Equal(24u, GhosttyTerminalNative.TerminalGetRows(handle));

        // Resize to larger
        GhosttyTerminalNative.TerminalResize(handle, 152, 41);
        Assert.Equal(152u, GhosttyTerminalNative.TerminalGetCols(handle));
        Assert.Equal(41u, GhosttyTerminalNative.TerminalGetRows(handle));

        // Resize to smaller
        GhosttyTerminalNative.TerminalResize(handle, 40, 10);
        Assert.Equal(40u, GhosttyTerminalNative.TerminalGetCols(handle));
        Assert.Equal(10u, GhosttyTerminalNative.TerminalGetRows(handle));

        GhosttyTerminalNative.TerminalFree(handle);
    }

    [Fact]
    public unsafe void Terminal_Resize_CellsReadableAfterResize()
    {
        if (!GhosttyTerminalNative.IsAvailable())
            return;

        var handle = GhosttyTerminalNative.TerminalNew(80, 24, 0);

        // Write some text
        var data = "Hello World"u8;
        fixed (byte* ptr = data)
        {
            GhosttyTerminalNative.TerminalProcess(handle, ptr, (nuint)data.Length);
        }

        // Resize
        GhosttyTerminalNative.TerminalResize(handle, 120, 40);

        // Should still be able to read cells after resize
        var cells = stackalloc GhosttyTerminalNative.CellInfo[120];
        var filled = GhosttyTerminalNative.TerminalGetRowCells(handle, 0, cells, 120);
        Assert.True(filled >= 11);
        Assert.Equal((uint)'H', cells[0].Codepoint);

        GhosttyTerminalNative.TerminalFree(handle);
    }

    [Fact]
    public unsafe void Terminal_Resize_ProcessDataAfterResize()
    {
        if (!GhosttyTerminalNative.IsAvailable())
            return;

        var handle = GhosttyTerminalNative.TerminalNew(80, 24, 0);

        // Write initial text
        var data1 = "Before"u8;
        fixed (byte* ptr = data1)
        {
            GhosttyTerminalNative.TerminalProcess(handle, ptr, (nuint)data1.Length);
        }

        // Resize
        GhosttyTerminalNative.TerminalResize(handle, 120, 40);

        // Write more text (cursor should still work after resize)
        var data2 = " After"u8;
        fixed (byte* ptr = data2)
        {
            GhosttyTerminalNative.TerminalProcess(handle, ptr, (nuint)data2.Length);
        }

        // Read cells — should contain "Before After"
        var cells = stackalloc GhosttyTerminalNative.CellInfo[120];
        GhosttyTerminalNative.TerminalGetRowCells(handle, 0, cells, 120);
        Assert.Equal((uint)'B', cells[0].Codepoint);
        Assert.Equal((uint)' ', cells[6].Codepoint);
        Assert.Equal((uint)'A', cells[7].Codepoint);

        GhosttyTerminalNative.TerminalFree(handle);
    }

    [Fact]
    public unsafe void Terminal_GetRowCellsWithGraphemes_BasicFallbackSafe()
    {
        if (!GhosttyTerminalNative.IsAvailable())
            return;

        var handle = GhosttyTerminalNative.TerminalNew(80, 24, 0);
        Assert.NotEqual(nint.Zero, handle);

        try
        {
            var data = "ABC"u8;
            fixed (byte* ptr = data)
            {
                GhosttyTerminalNative.TerminalProcess(handle, ptr, (nuint)data.Length);
            }

            var cells = stackalloc GhosttyTerminalNative.CellInfo[80];
            var spans = stackalloc GhosttyTerminalNative.GraphemeSpan[80];
            var graphemeCodepoints = stackalloc uint[320];
            uint graphemeWritten = 0;

            uint filled = GhosttyTerminalNative.TerminalGetRowCellsWithGraphemes(
                handle,
                0,
                cells,
                80,
                spans,
                80,
                graphemeCodepoints,
                320,
                &graphemeWritten);

            Assert.True(filled >= 3);
            Assert.Equal((uint)'A', cells[0].Codepoint);
            Assert.Equal((uint)'B', cells[1].Codepoint);
            Assert.Equal((uint)'C', cells[2].Codepoint);
        }
        finally
        {
            GhosttyTerminalNative.TerminalFree(handle);
        }
    }

    [Fact]
    public unsafe void Terminal_GetRowCellsWithGraphemes_CombiningSequence_WhenSupported()
    {
        if (!GhosttyTerminalNative.IsAvailable() || !GhosttyTerminalNative.SupportsRowCellGraphemes)
            return;

        var handle = GhosttyTerminalNative.TerminalNew(80, 24, 0);
        Assert.NotEqual(nint.Zero, handle);

        try
        {
            byte[] data = System.Text.Encoding.UTF8.GetBytes("e\u0301");
            fixed (byte* ptr = data)
            {
                GhosttyTerminalNative.TerminalProcess(handle, ptr, (nuint)data.Length);
            }

            var cells = stackalloc GhosttyTerminalNative.CellInfo[80];
            var spans = stackalloc GhosttyTerminalNative.GraphemeSpan[80];
            var graphemeCodepoints = stackalloc uint[320];
            uint graphemeWritten = 0;

            uint filled = GhosttyTerminalNative.TerminalGetRowCellsWithGraphemes(
                handle,
                0,
                cells,
                80,
                spans,
                80,
                graphemeCodepoints,
                320,
                &graphemeWritten);

            Assert.True(filled >= 1);
            Assert.Equal((uint)'e', cells[0].Codepoint);
            Assert.Equal(1u, spans[0].Length);

            uint trailing = graphemeCodepoints[spans[0].Offset];
            Assert.Equal(0x0301u, trailing);
        }
        finally
        {
            GhosttyTerminalNative.TerminalFree(handle);
        }
    }

    [Fact]
    public unsafe void Terminal_GetRowCellsWithGraphemes_RegionalIndicatorPair_WhenSupported()
    {
        if (!GhosttyTerminalNative.IsAvailable() || !GhosttyTerminalNative.SupportsRowCellGraphemes)
            return;

        var handle = GhosttyTerminalNative.TerminalNew(80, 24, 0);
        Assert.NotEqual(nint.Zero, handle);

        try
        {
            byte[] data = System.Text.Encoding.UTF8.GetBytes("\U0001F1E8\U0001F1E6");
            fixed (byte* ptr = data)
            {
                GhosttyTerminalNative.TerminalProcess(handle, ptr, (nuint)data.Length);
            }

            var cells = stackalloc GhosttyTerminalNative.CellInfo[80];
            var spans = stackalloc GhosttyTerminalNative.GraphemeSpan[80];
            var graphemeCodepoints = stackalloc uint[320];
            uint graphemeWritten = 0;

            uint filled = GhosttyTerminalNative.TerminalGetRowCellsWithGraphemes(
                handle,
                0,
                cells,
                80,
                spans,
                80,
                graphemeCodepoints,
                320,
                &graphemeWritten);

            Assert.True(filled >= 2);
            Assert.Equal(0x1F1E8u, cells[0].Codepoint);
            Assert.True(
                spans[0].Length == 1 && graphemeWritten >= 1 && graphemeCodepoints[spans[0].Offset] == 0x1F1E6u,
                $"filled={filled}, span0=({spans[0].Offset},{spans[0].Length}), graphemeWritten={graphemeWritten}, " +
                $"cp0=0x{cells[0].Codepoint:X}, cp1=0x{cells[1].Codepoint:X}, attrs0=0x{cells[0].Attrs:X8}, attrs1=0x{cells[1].Attrs:X8}");
        }
        finally
        {
            GhosttyTerminalNative.TerminalFree(handle);
        }
    }
}
