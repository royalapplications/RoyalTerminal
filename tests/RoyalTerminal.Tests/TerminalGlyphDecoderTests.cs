// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.GhosttySharp.Native;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Glyphs;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class TerminalGlyphDecoderTests(ITestOutputHelper output)
{
    private const string Triangle = "AAEAZABkA4QDhAACAAABAQEB9P5wAyADhPzgAAA=";

    [Fact]
    public void TriangleMatchesUpstreamOwnedCoordinatesAndIgnoresBoundingBoxHints()
    {
        byte[] payload = Convert.FromBase64String(Triangle);
        payload.AsSpan(2, 8).Fill(0xFF);
        Assert.True(TerminalGlyphDecoder.TryDecode(payload, out TerminalGlyphOutline? glyph, out TerminalGlyphDecodeError error));
        Assert.Equal(TerminalGlyphDecodeError.None, error);
        Assert.Equal(new ushort[] { 2 }, glyph.ContourEnds.ToArray());
        Assert.Equal(new TerminalGlyphPoint[] { new(500, 900, true), new(100, 100, true), new(900, 100, true) }, glyph.Points.ToArray());
        payload.AsSpan().Clear();
        Assert.Equal(glyph.Points.ToArray(), glyph.GetContour(0).ToArray());
        Assert.Throws<ArgumentOutOfRangeException>(() => glyph.GetContour(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => glyph.GetContour(1));
    }

    [Fact]
    public void RepeatedShortVectorsAndOffCurveFlagsDecodeWithoutCoordinateNarrowing()
    {
        byte[] repeated = Record([3], [0x1F, 3, 1, 2, 4, 8, 1, 2, 4, 8]);
        Assert.True(TerminalGlyphDecoder.TryDecode(repeated, out TerminalGlyphOutline? glyph, out _));
        Assert.Equal(new TerminalGlyphPoint[] { new(1, -1, true), new(3, -3, true), new(7, -7, true), new(15, -15, true) }, glyph.Points.ToArray());
        byte[] offCurve = Record([1], [0x30, 0x37, 7, 9]);
        Assert.True(TerminalGlyphDecoder.TryDecode(offCurve, out glyph, out _));
        Assert.Equal(new TerminalGlyphPoint[] { new(0, 0, false), new(7, 9, true) }, glyph.Points.ToArray());
        byte[] large = Record([1], [0x21, 0x21, 0x7F, 0xFF, 0x7F, 0xFF]);
        Assert.True(TerminalGlyphDecoder.TryDecode(large, out glyph, out _));
        Assert.Equal(65534, glyph.Points[1].X);
    }

    [Fact]
    public void MultipleContoursAndEmptyGlyphsArePreserved()
    {
        Assert.True(TerminalGlyphDecoder.TryDecode(Record([1, 3], [0x39, 3]), out TerminalGlyphOutline? glyph, out _));
        Assert.Equal(2, glyph.GetContour(0).Length);
        Assert.Equal(2, glyph.GetContour(1).Length);
        for (int length = 10; length <= 13; length++)
        {
            Assert.True(TerminalGlyphDecoder.TryDecode(new byte[length], out glyph, out _));
            Assert.True(glyph.Points.IsEmpty);
            Assert.True(glyph.ContourEnds.IsEmpty);
        }
    }

    [Fact]
    public void TruncationAndMalformedExpansionNeverReturnPartialOutlines()
    {
        byte[] triangle = Convert.FromBase64String(Triangle);
        for (int length = 0; length < triangle.Length; length++)
        {
            Assert.False(TerminalGlyphDecoder.TryDecode(triangle.AsSpan(0, length), out TerminalGlyphOutline? glyph, out TerminalGlyphDecodeError error));
            Assert.Null(glyph);
            Assert.Equal(TerminalGlyphDecodeError.MalformedPayload, error);
        }
        foreach (byte[] invalid in new[] { Record([0, 0], [0x31]), Record([0], [0x39, 1]), Record([1], [0x39]), Record([1], [0x01, 0x01]) })
        {
            Assert.False(TerminalGlyphDecoder.TryDecode(invalid, out TerminalGlyphOutline? glyph, out TerminalGlyphDecodeError error));
            Assert.Null(glyph);
            Assert.Equal(TerminalGlyphDecodeError.MalformedPayload, error);
        }
    }

    [Theory]
    [InlineData(5461, true)]
    [InlineData(5462, false)]
    [InlineData(65536, false)]
    public void CompactPointExpansionHonorsNativeAllocationLimit(int points, bool valid)
    {
        byte[] payload = RepeatedPoints(points);
        Assert.True(payload.Length < 1024);
        Assert.Equal(valid, TerminalGlyphDecoder.TryDecode(payload, out TerminalGlyphOutline? glyph, out TerminalGlyphDecodeError error));
        Assert.Equal(valid ? TerminalGlyphDecodeError.None : TerminalGlyphDecodeError.PayloadTooLarge, error);
        Assert.Equal(valid ? points : 0, glyph?.Points.Length ?? 0);
    }

    [Fact]
    public void InvalidPayloadValidationDoesNotAllocate()
    {
        byte[] invalid = Record([2], [0x39, 9]);
        for (int i = 0; i < 10; i++) TerminalGlyphDecoder.TryDecode(invalid, out _, out _);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) TerminalGlyphDecoder.TryDecode(invalid, out _, out _);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void BinaryDecodeOutcomesMatchNativeGlyphRegistration()
    {
        bool available = GhosttyVtProcessor.IsAvailable();
        output.WriteLine($"Native glyph decoder differential available: {available}");
        if (!available) return;
        using GhosttyTerminal terminal = new(10, 3);
        terminal.SetGlyphProtocol(true);
        StringBuilder response = new();
        GhosttyVtNative.GhosttyTerminalWritePtyCallback callback = (_, _, data, length) =>
        {
            byte[] bytes = new byte[checked((int)length)];
            Marshal.Copy(data, bytes, 0, bytes.Length);
            response.Append(Encoding.UTF8.GetString(bytes));
        };
        terminal.SetWritePtyCallback(Marshal.GetFunctionPointerForDelegate(callback));
        try
        {
            byte[] triangle = Convert.FromBase64String(Triangle);
            for (int length = 0; length <= triangle.Length; length++) Compare(triangle.AsSpan(0, length));
            for (int index = 0; index < triangle.Length; index++)
            {
                byte original = triangle[index];
                for (int value = 0; value <= byte.MaxValue; value++)
                {
                    triangle[index] = (byte)value;
                    Compare(triangle);
                }
                triangle[index] = original;
            }
            Compare(Record([0, 0], [0x31]));
            Compare(Record([3], [0x1F, 3, 1, 2, 4, 8, 1, 2, 4, 8]));
            Compare(Record([1], [0x30, 0x37, 7, 9]));
            Compare(RepeatedPoints(5461));
            Compare(RepeatedPoints(5462));
            Compare(RepeatedPoints(65536));
            Compare(new byte[65536]);
            Compare(new byte[65537]);

            void Compare(ReadOnlySpan<byte> payload)
            {
                TerminalGlyphDecoder.TryDecode(payload, out _, out TerminalGlyphDecodeError error);
                string expected = error switch
                {
                    TerminalGlyphDecodeError.None => "\u001b_25a1;r;cp=e000;status=0\u001b\\",
                    _ => "\u001b_25a1;r;cp=e000;status=1;reason=" + (error switch
                    {
                        TerminalGlyphDecodeError.CompositeUnsupported => "composite_unsupported",
                        TerminalGlyphDecodeError.HintingUnsupported => "hinting_unsupported",
                        TerminalGlyphDecodeError.PayloadTooLarge => "payload_too_large",
                        _ => "malformed_payload",
                    }) + "\u001b\\",
                };
                response.Clear();
                terminal.Write(Encoding.ASCII.GetBytes("\u001b_25a1;r;cp=e000;" + Convert.ToBase64String(payload) + "\u001b\\"));
                Assert.True(expected == response.ToString(),
                    $"Payload {Convert.ToHexString(payload[..Math.Min(payload.Length, 40)])}: expected {expected}, native {response}");
            }
        }
        finally
        {
            terminal.SetWritePtyCallback(0);
            GC.KeepAlive(callback);
        }
    }

    private static byte[] Record(ushort[] ends, byte[] flagsAndCoordinates)
    {
        byte[] result = new byte[12 + ends.Length * 2 + flagsAndCoordinates.Length];
        BinaryPrimitives.WriteInt16BigEndian(result, checked((short)ends.Length));
        for (int i = 0; i < ends.Length; i++) BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(10 + i * 2), ends[i]);
        flagsAndCoordinates.CopyTo(result, 12 + ends.Length * 2);
        return result;
    }

    private static byte[] RepeatedPoints(int count)
    {
        List<byte> flags = [];
        for (int remaining = count; remaining > 0;)
        {
            int run = Math.Min(remaining, 256);
            flags.Add(0x39);
            flags.Add((byte)(run - 1));
            remaining -= run;
        }
        return Record([checked((ushort)(count - 1))], flags.ToArray());
    }
}
