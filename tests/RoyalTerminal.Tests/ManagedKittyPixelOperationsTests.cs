// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class ManagedKittyPixelOperationsTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(31)]
    [InlineData(32)]
    [InlineData(33)]
    [InlineData(128)]
    [InlineData(257)]
    public void RgbExpansionMatchesScalarAtEveryAlignmentAndTail(int count)
    {
        foreach (int offset in new[] { 0, 1, 3, 15 })
        {
            byte[] input = new byte[count * 3 + offset + 7];
            new Random(count + offset).NextBytes(input);
            byte[] unchanged = (byte[])input.Clone();
            byte[] scalar = new byte[count * 4 + 23];
            byte[] vector = new byte[scalar.Length];
            scalar.AsSpan().Fill(0xA5);
            vector.AsSpan().Fill(0xA5);
            ManagedKittyAnimationPixels.ExpandRgb(input.AsSpan(offset, count * 3), scalar.AsSpan(9), useSimd: false);
            ManagedKittyAnimationPixels.ExpandRgb(input.AsSpan(offset, count * 3), vector.AsSpan(9));
            Assert.Equal(scalar, vector);
            for (int i = 0; i < count; i++)
            {
                Assert.Equal(input.AsSpan(offset + i * 3, 3).ToArray(), vector.AsSpan(9 + i * 4, 3).ToArray());
                Assert.Equal(255, vector[9 + i * 4 + 3]);
            }
            Assert.All(vector[..9], value => Assert.Equal(0xA5, value));
            Assert.All(vector[(9 + count * 4)..], value => Assert.Equal(0xA5, value));
            Assert.Equal(unchanged, input);
        }
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(0x01020304u)]
    [InlineData(0xFF80007Fu)]
    [InlineData(0xFFFFFFFFu)]
    [InlineData(0xFF000000u)]
    public void BackgroundFillPreservesWireChannelOrderAndGuards(uint color)
    {
        foreach (int count in new[] { 0, 1, 3, 4, 5, 16, 17, 65537 })
        {
            byte[] buffer = new byte[count * 4 + 6];
            buffer.AsSpan().Fill(0xA5);
            ManagedKittyAnimationPixels.Fill(buffer.AsSpan(3, count * 4), color);
            for (int i = 0; i < count * 4; i++)
                Assert.Equal((byte)(color >> (24 - i % 4 * 8)), buffer[i + 3]);
            Assert.All(buffer[..3], value => Assert.Equal(0xA5, value));
            Assert.All(buffer[^3..], value => Assert.Equal(0xA5, value));
        }
    }

    [Fact]
    public void InvalidOrAliasedStorageIsRejectedBeforeWriting()
    {
        byte[] buffer = new byte[16];
        buffer.AsSpan().Fill(73);
        Assert.Throws<ArgumentException>(() => ManagedKittyAnimationPixels.ExpandRgb(new byte[2], buffer));
        Assert.Throws<ArgumentException>(() => ManagedKittyAnimationPixels.ExpandRgb(new byte[6], buffer.AsSpan(0, 7)));
        Assert.Throws<ArgumentException>(() => ManagedKittyAnimationPixels.ExpandRgb(buffer.AsSpan(0, 6), buffer));
        Assert.Throws<ArgumentException>(() => ManagedKittyAnimationPixels.Fill(buffer.AsSpan(0, 7), 0));
        Assert.Throws<ArgumentException>(() => ManagedKittyAnimationPixels.Fill(buffer.AsSpan(0, 7), 0x01020304));
        Assert.All(buffer, value => Assert.Equal(73, value));
    }

    [Fact]
    public void AlphaFastPathsMatchOriginalWuffsArithmeticForAllAlphaPairs()
    {
        byte[] source = new byte[256 * 256 * 4];
        byte[] destination = new byte[source.Length];
        Random random = new(591);
        random.NextBytes(source);
        random.NextBytes(destination);
        for (int sourceAlpha = 0; sourceAlpha < 256; sourceAlpha++)
        for (int destinationAlpha = 0; destinationAlpha < 256; destinationAlpha++)
        {
            int offset = (sourceAlpha * 256 + destinationAlpha) * 4;
            source[offset + 3] = (byte)sourceAlpha;
            destination[offset + 3] = (byte)destinationAlpha;
        }
        byte[] expected = (byte[])destination.Clone();
        ScalarCompose(expected, source);
        ManagedKittyAnimationPixels.Compose(destination, 256, source, 256, 256, 256, 0, 0, 0, 0, false);
        Assert.Equal(expected, destination);
    }

    [Fact]
    public void TransparentSourceStillUsesNativeLowAlphaRounding()
    {
        byte[] destination = [1, 1, 1, 1];
        ManagedKittyAnimationPixels.Compose(destination, 1, new byte[] { 254, 128, 65, 0 }, 1, 1, 1, 0, 0, 0, 0, false);
        Assert.Equal(new byte[] { 0, 0, 0, 1 }, destination);
    }

    [Fact]
    public void WarmPixelOperationsDoNotAllocate()
    {
        byte[] rgb = new byte[257 * 3];
        byte[] rgba = new byte[257 * 4];
        byte[] destination = new byte[rgba.Length];
        for (int i = 0; i < 100; i++) Apply();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) Apply();
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        void Apply()
        {
            ManagedKittyAnimationPixels.ExpandRgb(rgb, rgba);
            ManagedKittyAnimationPixels.Fill(destination, 0x04030201);
            ManagedKittyAnimationPixels.Compose(destination, 257, rgba, 257, 257, 1, 0, 0, 0, 0, false);
        }
    }

    private static void ScalarCompose(Span<byte> target, ReadOnlySpan<byte> pixels)
    {
        for (int offset = 0; offset < target.Length; offset += 4)
        {
            uint da = (uint)target[offset + 3] * 257;
            if (da == 0) { pixels.Slice(offset, 4).CopyTo(target.Slice(offset, 4)); continue; }
            uint sa = (uint)pixels[offset + 3] * 257;
            uint inverse = 65535 - sa;
            uint alpha = sa + da * inverse / 65535;
            for (int channel = 0; channel < 3; channel++)
            {
                uint premultiplied = (uint)target[offset + channel] * 257 * da / 65535;
                uint blended = ((uint)pixels[offset + channel] * 257 * sa + premultiplied * inverse) / 65535;
                target[offset + channel] = (byte)((blended * 65535 / alpha) >> 8);
            }
            target[offset + 3] = (byte)(alpha >> 8);
        }
    }
}
