// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Globalization;
using System.Text;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class ManagedKittyResponseFormatterTests
{
    [Theory]
    [InlineData(0u, 0u, 0u, 0u, "")]
    [InlineData(0u, 0u, 5u, 7u, "")]
    [InlineData(4u, 0u, 0u, 0u, "\u001b_Gi=4;")]
    [InlineData(0u, 4u, 0u, 0u, "\u001b_GI=4;")]
    [InlineData(12u, 4u, 0u, 0u, "\u001b_Gi=12,I=4;")]
    [InlineData(0u, 4u, 2u, 3u, "\u001b_GI=4,p=2,r=3;")]
    [InlineData(uint.MaxValue, uint.MaxValue, uint.MaxValue, uint.MaxValue,
        "\u001b_Gi=4294967295,I=4294967295,p=4294967295,r=4294967295;")]
    public void HeaderAndReplyPreserveNativeFieldOrder(uint id, uint number, uint placement, uint frame, string expected)
    {
        Span<byte> buffer = stackalloc byte[ManagedKittyResponseFormatter.MaximumHeaderBytes];
        int length = ManagedKittyResponseFormatter.WriteHeader(buffer, id, number, placement, frame);
        Assert.Equal(expected, Encoding.ASCII.GetString(buffer[..length]));
        Assert.Equal(expected.Length == 0 ? "" : expected + "OK\u001b\\",
            Encoding.ASCII.GetString(ManagedKittyResponseFormatter.Format(id, number, placement, frame, "OK")));
    }

    [Theory]
    [InlineData("ENOENT: image not found")]
    [InlineData("non-ASCII: \u00e9\U0001f600\ud800")]
    [InlineData("")]
    public void HostMessagesRetainPreviousAsciiReplacementPolicy(string message)
    {
        Assert.Equal(Encoding.ASCII.GetBytes("\u001b_Gi=1;" + message + "\u001b\\"),
            ManagedKittyResponseFormatter.Format(1, 0, 0, 0, message));
    }

    [Fact]
    public void LongMessagesAreOwnedAndNotLimitedToHeaderScratch()
    {
        string message = new('x', 4096);
        byte[] first = ManagedKittyResponseFormatter.Format(1, 0, 0, 0, message);
        byte[] second = ManagedKittyResponseFormatter.Format(1, 0, 0, 0, message);
        Assert.Equal(Encoding.ASCII.GetBytes("\u001b_Gi=1;" + message + "\u001b\\"), second);
        first[0] = 0;
        Assert.Equal(0x1B, second[0]);
    }

    [Fact]
    public void HeaderFormattingIsAllocationFreeAndCultureIndependent()
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-SA");
            Span<byte> buffer = stackalloc byte[ManagedKittyResponseFormatter.MaximumHeaderBytes];
            int length = ManagedKittyResponseFormatter.WriteHeader(buffer, uint.MaxValue, uint.MaxValue, uint.MaxValue, uint.MaxValue);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++)
                ManagedKittyResponseFormatter.WriteHeader(buffer, uint.MaxValue, uint.MaxValue, uint.MaxValue, uint.MaxValue);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.Equal(0, allocated);
            Assert.StartsWith("\u001b_Gi=4294967295,I=4294967295,", Encoding.ASCII.GetString(buffer[..length]));
            Assert.Throws<ArgumentException>(() => ManagedKittyResponseFormatter.WriteHeader(new byte[63], 1, 0, 0, 0));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }
}
