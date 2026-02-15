// Licensed under the MIT License.
// RoyalTerminal.IntegrationTests — Key event and encoder native integration tests.

using System.Runtime.InteropServices;
using System.Text;
using RoyalTerminal.GhosttySharp.Native;
using Xunit;
using static RoyalTerminal.GhosttySharp.Native.GhosttyVtNative;

namespace RoyalTerminal.IntegrationTests;

/// <summary>
/// Integration tests for the Ghostty VT key event and key encoder APIs.
/// Creates key events, sets properties, and encodes into terminal escape sequences.
/// </summary>
public class KeyEncoderTests
{
    [Fact]
    public void KeyEvent_CreateAndFree()
    {
        var result = GhosttyVtNative.KeyEventNew(0, out var keyEvent);
        Assert.Equal(GhosttyResult.Success, result);
        Assert.NotEqual(nint.Zero, keyEvent);
        GhosttyVtNative.KeyEventFree(keyEvent);
    }

    [Fact]
    public void KeyEvent_SetAndGetAction()
    {
        GhosttyVtNative.KeyEventNew(0, out var keyEvent);
        try
        {
            GhosttyVtNative.KeyEventSetAction(keyEvent, GhosttyVtKeyAction.Press);
            Assert.Equal(GhosttyVtKeyAction.Press, GhosttyVtNative.KeyEventGetAction(keyEvent));

            GhosttyVtNative.KeyEventSetAction(keyEvent, GhosttyVtKeyAction.Release);
            Assert.Equal(GhosttyVtKeyAction.Release, GhosttyVtNative.KeyEventGetAction(keyEvent));
        }
        finally { GhosttyVtNative.KeyEventFree(keyEvent); }
    }

    [Fact]
    public void KeyEvent_SetAndGetKey()
    {
        GhosttyVtNative.KeyEventNew(0, out var keyEvent);
        try
        {
            GhosttyVtNative.KeyEventSetKey(keyEvent, GhosttyVtKey.A);
            Assert.Equal(GhosttyVtKey.A, GhosttyVtNative.KeyEventGetKey(keyEvent));

            GhosttyVtNative.KeyEventSetKey(keyEvent, GhosttyVtKey.Enter);
            Assert.Equal(GhosttyVtKey.Enter, GhosttyVtNative.KeyEventGetKey(keyEvent));
        }
        finally { GhosttyVtNative.KeyEventFree(keyEvent); }
    }

    [Fact]
    public void KeyEvent_SetAndGetMods()
    {
        GhosttyVtNative.KeyEventNew(0, out var keyEvent);
        try
        {
            GhosttyVtNative.KeyEventSetMods(keyEvent, GhosttyVtMods.Ctrl | GhosttyVtMods.Shift);
            var mods = GhosttyVtNative.KeyEventGetMods(keyEvent);
            Assert.True(mods.HasFlag(GhosttyVtMods.Ctrl));
            Assert.True(mods.HasFlag(GhosttyVtMods.Shift));
            Assert.False(mods.HasFlag(GhosttyVtMods.Alt));
        }
        finally { GhosttyVtNative.KeyEventFree(keyEvent); }
    }

    [Fact]
    public void KeyEvent_SetAndGetUnshiftedCodepoint()
    {
        GhosttyVtNative.KeyEventNew(0, out var keyEvent);
        try
        {
            GhosttyVtNative.KeyEventSetUnshiftedCodepoint(keyEvent, 'a');
            Assert.Equal((uint)'a', GhosttyVtNative.KeyEventGetUnshiftedCodepoint(keyEvent));
        }
        finally { GhosttyVtNative.KeyEventFree(keyEvent); }
    }

    [Fact]
    public void KeyEncoder_CreateAndFree()
    {
        var result = GhosttyVtNative.KeyEncoderNew(0, out var encoder);
        Assert.Equal(GhosttyResult.Success, result);
        Assert.NotEqual(nint.Zero, encoder);
        GhosttyVtNative.KeyEncoderFree(encoder);
    }

    [Fact]
    public void KeyEncoder_EncodeEnterKey()
    {
        GhosttyVtNative.KeyEncoderNew(0, out var encoder);
        GhosttyVtNative.KeyEventNew(0, out var keyEvent);

        try
        {
            GhosttyVtNative.KeyEventSetAction(keyEvent, GhosttyVtKeyAction.Press);
            GhosttyVtNative.KeyEventSetKey(keyEvent, GhosttyVtKey.Enter);

            unsafe
            {
                Span<byte> buf = stackalloc byte[128];
                fixed (byte* bufPtr = buf)
                {
                    var result = GhosttyVtNative.KeyEncoderEncode(
                        encoder, keyEvent, bufPtr, 128, out var written);
                    Assert.Equal(GhosttyResult.Success, result);
                    Assert.True(written > 0);

                    // Enter usually encodes as \r (carriage return)
                    var encoded = new ReadOnlySpan<byte>(bufPtr, (int)written);
                    Assert.Equal((byte)'\r', encoded[0]);
                }
            }
        }
        finally
        {
            GhosttyVtNative.KeyEventFree(keyEvent);
            GhosttyVtNative.KeyEncoderFree(encoder);
        }
    }

    [Fact]
    public void KeyEncoder_EncodeArrowUp()
    {
        GhosttyVtNative.KeyEncoderNew(0, out var encoder);
        GhosttyVtNative.KeyEventNew(0, out var keyEvent);

        try
        {
            GhosttyVtNative.KeyEventSetAction(keyEvent, GhosttyVtKeyAction.Press);
            GhosttyVtNative.KeyEventSetKey(keyEvent, GhosttyVtKey.ArrowUp);

            unsafe
            {
                Span<byte> buf = stackalloc byte[128];
                fixed (byte* bufPtr = buf)
                {
                    var result = GhosttyVtNative.KeyEncoderEncode(
                        encoder, keyEvent, bufPtr, 128, out var written);
                    Assert.Equal(GhosttyResult.Success, result);
                    Assert.True(written > 0);

                    // Arrow up encodes as ESC[A or ESC OA
                    var encoded = Encoding.ASCII.GetString(bufPtr, (int)written);
                    Assert.Contains("A", encoded);
                    Assert.StartsWith("\x1b", encoded);
                }
            }
        }
        finally
        {
            GhosttyVtNative.KeyEventFree(keyEvent);
            GhosttyVtNative.KeyEncoderFree(encoder);
        }
    }

    [Fact]
    public void KeyEncoder_EncodeCtrlC()
    {
        GhosttyVtNative.KeyEncoderNew(0, out var encoder);
        GhosttyVtNative.KeyEventNew(0, out var keyEvent);

        try
        {
            GhosttyVtNative.KeyEventSetAction(keyEvent, GhosttyVtKeyAction.Press);
            GhosttyVtNative.KeyEventSetKey(keyEvent, GhosttyVtKey.C);
            GhosttyVtNative.KeyEventSetMods(keyEvent, GhosttyVtMods.Ctrl);

            unsafe
            {
                Span<byte> buf = stackalloc byte[128];
                fixed (byte* bufPtr = buf)
                {
                    var result = GhosttyVtNative.KeyEncoderEncode(
                        encoder, keyEvent, bufPtr, 128, out var written);
                    Assert.Equal(GhosttyResult.Success, result);
                    Assert.True(written > 0);

                    // Ctrl+C = 0x03 (ETX)
                    Assert.Equal(0x03, bufPtr[0]);
                }
            }
        }
        finally
        {
            GhosttyVtNative.KeyEventFree(keyEvent);
            GhosttyVtNative.KeyEncoderFree(encoder);
        }
    }

    [Fact]
    public void KeyEncoder_ReleaseEvent_NoOutput()
    {
        GhosttyVtNative.KeyEncoderNew(0, out var encoder);
        GhosttyVtNative.KeyEventNew(0, out var keyEvent);

        try
        {
            GhosttyVtNative.KeyEventSetAction(keyEvent, GhosttyVtKeyAction.Release);
            GhosttyVtNative.KeyEventSetKey(keyEvent, GhosttyVtKey.A);

            unsafe
            {
                Span<byte> buf = stackalloc byte[128];
                fixed (byte* bufPtr = buf)
                {
                    var result = GhosttyVtNative.KeyEncoderEncode(
                        encoder, keyEvent, bufPtr, 128, out var written);
                    Assert.Equal(GhosttyResult.Success, result);
                    // Release events typically produce no output in legacy mode
                    Assert.Equal((nuint)0, written);
                }
            }
        }
        finally
        {
            GhosttyVtNative.KeyEventFree(keyEvent);
            GhosttyVtNative.KeyEncoderFree(encoder);
        }
    }

    [Fact]
    public void KeyEncoder_WithKittyProtocol_ProducesExtendedSequence()
    {
        GhosttyVtNative.KeyEncoderNew(0, out var encoder);
        GhosttyVtNative.KeyEventNew(0, out var keyEvent);

        try
        {
            // Enable Kitty keyboard protocol (disambiguate flag)
            unsafe
            {
                byte kittyFlags = 1; // GHOSTTY_KITTY_KEY_DISAMBIGUATE
                GhosttyVtNative.KeyEncoderSetopt(
                    encoder, 5, // GHOSTTY_KEY_ENCODER_OPT_KITTY_FLAGS
                    &kittyFlags);
            }

            GhosttyVtNative.KeyEventSetAction(keyEvent, GhosttyVtKeyAction.Press);
            GhosttyVtNative.KeyEventSetKey(keyEvent, GhosttyVtKey.Enter);

            unsafe
            {
                Span<byte> buf = stackalloc byte[128];
                fixed (byte* bufPtr = buf)
                {
                    var result = GhosttyVtNative.KeyEncoderEncode(
                        encoder, keyEvent, bufPtr, 128, out var written);
                    Assert.Equal(GhosttyResult.Success, result);
                    Assert.True(written > 0);
                }
            }
        }
        finally
        {
            GhosttyVtNative.KeyEventFree(keyEvent);
            GhosttyVtNative.KeyEncoderFree(encoder);
        }
    }

    [Fact]
    public void KeyEvent_SetAndGetUtf8Text()
    {
        GhosttyVtNative.KeyEventNew(0, out var keyEvent);

        try
        {
            unsafe
            {
                var text = "a"u8;
                fixed (byte* textPtr = text)
                {
                    GhosttyVtNative.KeyEventSetUtf8(keyEvent, textPtr, (nuint)text.Length);
                }

                var resultPtr = GhosttyVtNative.KeyEventGetUtf8(keyEvent, out var len);
                Assert.True(resultPtr != null);
                Assert.Equal((nuint)1, len);
                Assert.Equal((byte)'a', resultPtr[0]);
            }
        }
        finally { GhosttyVtNative.KeyEventFree(keyEvent); }
    }
}
