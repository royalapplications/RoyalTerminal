// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Terminal.Glyphs;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class TerminalGlyphResponseFormatterTests
{
    [Theory]
    [InlineData('r', 0xE000u, null, "r;cp=e000;status=0")]
    [InlineData('r', 0u, "malformed_payload", "r;cp=0;status=1;reason=malformed_payload")]
    [InlineData('r', 0x1FFFFFu, "out_of_memory", "r;cp=1fffff;status=1;reason=out_of_memory")]
    [InlineData('c', 0u, null, "c;status=0")]
    [InlineData('c', 0u, "out_of_namespace", "c;status=1;reason=out_of_namespace")]
    public void StatusBytesMatchNativeFraming(char verb, uint codepoint, string? error, string expected)
    {
        Span<byte> buffer = stackalloc byte[TerminalGlyphResponseFormatter.MaximumBytes];
        int count = TerminalGlyphResponseFormatter.WriteStatus(buffer, (byte)verb, codepoint, error);
        byte[] wire = Encoding.ASCII.GetBytes("\u001b_25a1;" + expected + "\u001b\\");
        Assert.Equal(wire, buffer[..count].ToArray());
        Assert.Equal(wire, TerminalGlyphResponseFormatter.Status((byte)verb, codepoint, error));
    }

    [Theory]
    [InlineData(false, false, "")]
    [InlineData(true, false, "glossary")]
    [InlineData(false, true, "system")]
    [InlineData(true, true, "system,glossary")]
    public void QueryPreservesCoverageOrder(bool glossary, bool system, string status)
    {
        byte[] expected = Encoding.ASCII.GetBytes("\u001b_25a1;q;cp=10fffd;status=" + status + "\u001b\\");
        Assert.Equal(expected, TerminalGlyphResponseFormatter.Query(0x10FFFD, glossary, system));
    }

    [Fact]
    public void ScratchWritesAllocateNothingAndOwnedRepliesDoNotAlias()
    {
        Span<byte> buffer = stackalloc byte[TerminalGlyphResponseFormatter.MaximumBytes];
        for (int i = 0; i < 4; i++)
        {
            TerminalGlyphResponseFormatter.WriteStatus(buffer, (byte)'r', 0xE000, "out_of_memory");
            TerminalGlyphResponseFormatter.WriteQuery(buffer, 0xE000, true, true);
        }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++)
        {
            TerminalGlyphResponseFormatter.WriteStatus(buffer, (byte)'r', 0xE000, "out_of_memory");
            TerminalGlyphResponseFormatter.WriteQuery(buffer, 0xE000, true, true);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
        byte[] first = TerminalGlyphResponseFormatter.Status((byte)'c', 0, null);
        first[0] = 0;
        Assert.Equal(0x1B, TerminalGlyphResponseFormatter.Status((byte)'c', 0, null)[0]);
        Assert.Throws<ArgumentException>(() => TerminalGlyphResponseFormatter.WriteStatus(new byte[127], (byte)'c', 0, null));
        Assert.Throws<ArgumentException>(() => TerminalGlyphResponseFormatter.WriteQuery(new byte[127], 0, false, false));
    }
}
