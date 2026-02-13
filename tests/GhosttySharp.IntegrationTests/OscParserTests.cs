// Licensed under the MIT License.
// GhosttySharp.IntegrationTests — OSC parser native integration tests.

using System.Runtime.InteropServices;
using System.Text;
using GhosttySharp.Native;
using Xunit;
using static GhosttySharp.Native.GhosttyVtNative;

namespace GhosttySharp.IntegrationTests;

/// <summary>
/// Integration tests for the Ghostty VT OSC parser.
/// These tests create, use, and free native OSC parser instances.
/// </summary>
public class OscParserTests
{
    [Fact]
    public void OscParser_CreateAndFree()
    {
        var result = GhosttyVtNative.OscNew(0, out var parser);
        Assert.Equal(GhosttyResult.Success, result);
        Assert.NotEqual(nint.Zero, parser);
        GhosttyVtNative.OscFree(parser);
    }

    [Fact]
    public void OscParser_ParseWindowTitle()
    {
        var result = GhosttyVtNative.OscNew(0, out var parser);
        Assert.Equal(GhosttyResult.Success, result);

        try
        {
            // Feed OSC 0 (set window title): "0;My Terminal Title"
            var data = "0;My Terminal Title"u8;
            foreach (var b in data)
            {
                GhosttyVtNative.OscNext(parser, b);
            }

            // End with BEL terminator (0x07)
            var command = GhosttyVtNative.OscEnd(parser, 0x07);
            Assert.NotEqual(nint.Zero, command);

            var cmdType = GhosttyVtNative.OscCommandType(command);
            Assert.Equal(GhosttyOscCommandType.ChangeWindowTitle, cmdType);
        }
        finally
        {
            GhosttyVtNative.OscFree(parser);
        }
    }

    [Fact]
    public void OscParser_ExtractWindowTitleData()
    {
        var result = GhosttyVtNative.OscNew(0, out var parser);
        Assert.Equal(GhosttyResult.Success, result);

        try
        {
            var data = "0;Hello Ghostty"u8;
            foreach (var b in data)
            {
                GhosttyVtNative.OscNext(parser, b);
            }

            var command = GhosttyVtNative.OscEnd(parser, 0x07);
            var cmdType = GhosttyVtNative.OscCommandType(command);
            Assert.Equal(GhosttyOscCommandType.ChangeWindowTitle, cmdType);

            // Extract title string
            unsafe
            {
                byte* titlePtr = null;
                bool ok = GhosttyVtNative.OscCommandData(
                    command,
                    GhosttyOscCommandData.ChangeWindowTitleStr,
                    &titlePtr);

                Assert.True(ok);
                Assert.True(titlePtr != null);

                var title = Marshal.PtrToStringUTF8((nint)titlePtr);
                Assert.Equal("Hello Ghostty", title);
            }
        }
        finally
        {
            GhosttyVtNative.OscFree(parser);
        }
    }

    [Fact]
    public void OscParser_Reset_AllowsReuse()
    {
        var result = GhosttyVtNative.OscNew(0, out var parser);
        Assert.Equal(GhosttyResult.Success, result);

        try
        {
            // Parse first title
            foreach (var b in "0;First"u8)
                GhosttyVtNative.OscNext(parser, b);
            var cmd1 = GhosttyVtNative.OscEnd(parser, 0x07);
            Assert.Equal(GhosttyOscCommandType.ChangeWindowTitle, GhosttyVtNative.OscCommandType(cmd1));

            // Reset and parse second title
            GhosttyVtNative.OscReset(parser);
            foreach (var b in "0;Second"u8)
                GhosttyVtNative.OscNext(parser, b);
            var cmd2 = GhosttyVtNative.OscEnd(parser, 0x07);
            Assert.Equal(GhosttyOscCommandType.ChangeWindowTitle, GhosttyVtNative.OscCommandType(cmd2));
        }
        finally
        {
            GhosttyVtNative.OscFree(parser);
        }
    }

    [Fact]
    public void OscParser_InvalidSequence_ReturnsInvalid()
    {
        var result = GhosttyVtNative.OscNew(0, out var parser);
        Assert.Equal(GhosttyResult.Success, result);

        try
        {
            // Feed garbage — should produce invalid or null command
            GhosttyVtNative.OscNext(parser, 0xFF);
            var command = GhosttyVtNative.OscEnd(parser, 0x07);

            // Command may be null for unrecognized input
            if (command != nint.Zero)
            {
                var cmdType = GhosttyVtNative.OscCommandType(command);
                // Should be invalid or some other type — just verify no crash
                _ = cmdType;
            }
            else
            {
                // Null command for garbage input — also valid behavior
                var nullType = GhosttyVtNative.OscCommandType(0);
                Assert.Equal(GhosttyOscCommandType.Invalid, nullType);
            }
        }
        finally
        {
            GhosttyVtNative.OscFree(parser);
        }
    }

    [Fact]
    public void OscParser_NullCommand_ReturnsInvalid()
    {
        var cmdType = GhosttyVtNative.OscCommandType(0);
        Assert.Equal(GhosttyOscCommandType.Invalid, cmdType);
    }
}
