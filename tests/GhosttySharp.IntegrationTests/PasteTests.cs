// Licensed under the MIT License.
// GhosttySharp.IntegrationTests — Tests that call into the native libghostty-vt library.

using System.Runtime.InteropServices;
using System.Text;
using GhosttySharp.Native;
using Xunit;
using static GhosttySharp.Native.GhosttyVtNative;

namespace GhosttySharp.IntegrationTests;

/// <summary>
/// Integration tests for the Ghostty VT paste safety API.
/// These tests call into the real native libghostty-vt library.
/// </summary>
public class PasteTests
{
    [Fact]
    public void PasteIsSafe_SafeText_ReturnsTrue()
    {
        var data = "hello world"u8;
        bool safe;
        unsafe
        {
            fixed (byte* ptr = data)
            {
                safe = GhosttyVtNative.PasteIsSafe(ptr, (nuint)data.Length);
            }
        }
        Assert.True(safe);
    }

    [Fact]
    public void PasteIsSafe_TextWithNewline_ReturnsFalse()
    {
        var data = "rm -rf /\n"u8;
        bool safe;
        unsafe
        {
            fixed (byte* ptr = data)
            {
                safe = GhosttyVtNative.PasteIsSafe(ptr, (nuint)data.Length);
            }
        }
        Assert.False(safe);
    }

    [Fact]
    public void PasteIsSafe_EmptyString_ReturnsTrue()
    {
        bool safe;
        unsafe
        {
            byte dummy = 0;
            safe = GhosttyVtNative.PasteIsSafe(&dummy, 0);
        }
        Assert.True(safe);
    }

    [Fact]
    public void PasteIsSafe_BracketedPasteEnd_ReturnsFalse()
    {
        // ESC[201~ — bracketed paste end sequence
        var data = "\x1b[201~"u8;
        bool safe;
        unsafe
        {
            fixed (byte* ptr = data)
            {
                safe = GhosttyVtNative.PasteIsSafe(ptr, (nuint)data.Length);
            }
        }
        Assert.False(safe);
    }

    [Fact]
    public void PasteIsSafe_LongSafeText_ReturnsTrue()
    {
        var data = Encoding.UTF8.GetBytes(new string('A', 10000));
        bool safe;
        unsafe
        {
            fixed (byte* ptr = data)
            {
                safe = GhosttyVtNative.PasteIsSafe(ptr, (nuint)data.Length);
            }
        }
        Assert.True(safe);
    }
}
