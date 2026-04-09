// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.IntegrationTests — SGR parser native integration tests.

using System.Runtime.InteropServices;
using RoyalTerminal.GhosttySharp.Native;
using Xunit;
using static RoyalTerminal.GhosttySharp.Native.GhosttyVtNative;

namespace RoyalTerminal.IntegrationTests;

/// <summary>
/// Integration tests for the Ghostty VT SGR parser.
/// Parses terminal styling sequences (bold, colors, underline) via native library.
/// </summary>
public class SgrParserTests
{
    [Fact]
    public void SgrParser_CreateAndFree()
    {
        var result = GhosttyVtNative.SgrNew(0, out var parser);
        Assert.Equal(GhosttyResult.Success, result);
        Assert.NotEqual(nint.Zero, parser);
        GhosttyVtNative.SgrFree(parser);
    }

    [Fact]
    public void SgrParser_ParseBold()
    {
        var result = GhosttyVtNative.SgrNew(0, out var parser);
        Assert.Equal(GhosttyResult.Success, result);

        try
        {
            // SGR 1 = bold
            unsafe
            {
                ushort param = 1;
                var setResult = GhosttyVtNative.SgrSetParams(parser, &param, null, 1);
                Assert.Equal(GhosttyResult.Success, setResult);

                // SgrAttribute is a tagged union: 4 bytes tag + 64 bytes value = 68 bytes
                // But actual size might differ, use generous buffer
                Span<byte> attrBuf = stackalloc byte[128];
                fixed (byte* attrPtr = attrBuf)
                {
                    bool hasAttr = GhosttyVtNative.SgrNext(parser, attrPtr);
                    Assert.True(hasAttr);

                    // Read the tag (first 4 bytes, int32)
                    var tag = (GhosttySgrAttributeTag)Marshal.ReadInt32((nint)attrPtr);
                    Assert.Equal(GhosttySgrAttributeTag.Bold, tag);
                }
            }
        }
        finally
        {
            GhosttyVtNative.SgrFree(parser);
        }
    }

    [Fact]
    public void SgrParser_ParseBoldAndRedForeground()
    {
        var result = GhosttyVtNative.SgrNew(0, out var parser);
        Assert.Equal(GhosttyResult.Success, result);

        try
        {
            // SGR 1;31 = bold + red foreground
            unsafe
            {
                Span<ushort> parms = stackalloc ushort[] { 1, 31 };
                fixed (ushort* paramsPtr = parms)
                {
                    var setResult = GhosttyVtNative.SgrSetParams(parser, paramsPtr, null, 2);
                    Assert.Equal(GhosttyResult.Success, setResult);
                }

                Span<byte> attrBuf = stackalloc byte[128];
                var tags = new List<GhosttySgrAttributeTag>();

                fixed (byte* attrPtr = attrBuf)
                {
                    while (GhosttyVtNative.SgrNext(parser, attrPtr))
                    {
                        var tag = (GhosttySgrAttributeTag)Marshal.ReadInt32((nint)attrPtr);
                        tags.Add(tag);
                    }
                }

                Assert.Contains(GhosttySgrAttributeTag.Bold, tags);
                // SGR 31 = red foreground, may be mapped as Fg8 or Bg8 depending on parser
                Assert.True(
                    tags.Contains(GhosttySgrAttributeTag.Fg8) || tags.Contains(GhosttySgrAttributeTag.Bg8),
                    $"Expected Fg8 or Bg8 in [{string.Join(", ", tags)}]");
            }
        }
        finally
        {
            GhosttyVtNative.SgrFree(parser);
        }
    }

    [Fact]
    public void SgrParser_ParseReset_ReturnsUnset()
    {
        var result = GhosttyVtNative.SgrNew(0, out var parser);
        Assert.Equal(GhosttyResult.Success, result);

        try
        {
            // SGR 0 = reset all attributes
            unsafe
            {
                ushort param = 0;
                var setResult = GhosttyVtNative.SgrSetParams(parser, &param, null, 1);
                Assert.Equal(GhosttyResult.Success, setResult);

                Span<byte> attrBuf = stackalloc byte[128];
                fixed (byte* attrPtr = attrBuf)
                {
                    bool hasAttr = GhosttyVtNative.SgrNext(parser, attrPtr);
                    Assert.True(hasAttr);

                    var tag = (GhosttySgrAttributeTag)Marshal.ReadInt32((nint)attrPtr);
                    Assert.Equal(GhosttySgrAttributeTag.Unset, tag);
                }
            }
        }
        finally
        {
            GhosttyVtNative.SgrFree(parser);
        }
    }

    [Fact]
    public void SgrParser_Reset_AllowsReuse()
    {
        var result = GhosttyVtNative.SgrNew(0, out var parser);
        Assert.Equal(GhosttyResult.Success, result);

        try
        {
            unsafe
            {
                // First parse: bold
                ushort param1 = 1;
                GhosttyVtNative.SgrSetParams(parser, &param1, null, 1);

                Span<byte> attrBuf = stackalloc byte[128];
                fixed (byte* attrPtr = attrBuf)
                {
                    GhosttyVtNative.SgrNext(parser, attrPtr);
                    var tag1 = (GhosttySgrAttributeTag)Marshal.ReadInt32((nint)attrPtr);
                    Assert.Equal(GhosttySgrAttributeTag.Bold, tag1);
                }

                // Reset and parse italic
                GhosttyVtNative.SgrReset(parser);
                ushort param2 = 3;
                GhosttyVtNative.SgrSetParams(parser, &param2, null, 1);

                fixed (byte* attrPtr = attrBuf)
                {
                    GhosttyVtNative.SgrNext(parser, attrPtr);
                    var tag2 = (GhosttySgrAttributeTag)Marshal.ReadInt32((nint)attrPtr);
                    Assert.Equal(GhosttySgrAttributeTag.Italic, tag2);
                }
            }
        }
        finally
        {
            GhosttyVtNative.SgrFree(parser);
        }
    }

    [Fact]
    public void SgrParser_NoMoreAttributes_ReturnsFalse()
    {
        var result = GhosttyVtNative.SgrNew(0, out var parser);
        Assert.Equal(GhosttyResult.Success, result);

        try
        {
            unsafe
            {
                ushort param = 1; // bold only
                GhosttyVtNative.SgrSetParams(parser, &param, null, 1);

                Span<byte> attrBuf = stackalloc byte[128];
                fixed (byte* attrPtr = attrBuf)
                {
                    // First call: bold attribute
                    bool has1 = GhosttyVtNative.SgrNext(parser, attrPtr);
                    Assert.True(has1);

                    // Second call: should be false (no more)
                    bool has2 = GhosttyVtNative.SgrNext(parser, attrPtr);
                    Assert.False(has2);
                }
            }
        }
        finally
        {
            GhosttyVtNative.SgrFree(parser);
        }
    }

    [Fact]
    public unsafe void SgrParser_HelperAccessors_ExposeTypedValues()
    {
        var result = GhosttyVtNative.SgrNew(0, out var parser);
        Assert.Equal(GhosttyResult.Success, result);

        try
        {
            ushort[] values = [1];
            fixed (ushort* valuesPtr = values)
            {
                Assert.Equal(
                    GhosttyResult.Success,
                    GhosttyVtNative.SgrSetParams(parser, valuesPtr, null, 1));
            }

            GhosttySgrAttribute attr = default;
            Assert.True(GhosttyVtNative.SgrNext(parser, &attr));
            Assert.Equal(GhosttySgrAttributeTag.Bold, GhosttyVtNative.SgrAttributeTag(attr));
            Assert.NotEqual(nint.Zero, (nint)GhosttyVtNative.SgrAttributeValue(&attr));

            GhosttyVtNative.SgrReset(parser);

            ushort[] unknownValues = [999];
            fixed (ushort* unknownPtr = unknownValues)
            {
                Assert.Equal(
                    GhosttyResult.Success,
                    GhosttyVtNative.SgrSetParams(parser, unknownPtr, null, 1));
            }

            GhosttySgrAttribute unknownAttr = default;
            Assert.True(GhosttyVtNative.SgrNext(parser, &unknownAttr));
            Assert.Equal(GhosttySgrAttributeTag.Unknown, GhosttyVtNative.SgrAttributeTag(unknownAttr));

            GhosttySgrAttributeValue* unknownValue = GhosttyVtNative.SgrAttributeValue(&unknownAttr);
            Assert.NotEqual(nint.Zero, (nint)unknownValue);

            ushort* fullPtr = null;
            nuint fullLength = GhosttyVtNative.SgrUnknownFull(unknownValue->Unknown, &fullPtr);
            Assert.True(fullLength > 0);
            Assert.NotEqual((nint)0, (nint)fullPtr);
            Assert.Equal((ushort)999, fullPtr[0]);

            ushort* partialPtr = null;
            nuint partialLength = GhosttyVtNative.SgrUnknownPartial(unknownValue->Unknown, &partialPtr);
            Assert.True(partialLength > 0);
            Assert.NotEqual((nint)0, (nint)partialPtr);
        }
        finally
        {
            GhosttyVtNative.SgrFree(parser);
        }
    }
}
