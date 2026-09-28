// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.GhosttySharp;
using RoyalTerminal.GhosttySharp.Native;
using RoyalTerminal.IntegrationTests.TestInfrastructure;
using System.Text;
using Xunit;

namespace RoyalTerminal.IntegrationTests;

public class GhosttyKittyGraphicsExtendedTests
{
    [GhosttyNativeFact]
    public void ImageAdmissionAfterQuotaExemptRgbPromotionReclaimsActualBytes()
    {
        if (!GhosttyVtHelpers.GetBuildFeatures().KittyGraphics) return;
        using GhosttyTerminal terminal = new(10, 3);
        terminal.SetKittyImageStorageLimit(3);
        terminal.Write("\x1b_Ga=t,i=1,f=24,s=1,v=1;/wAA\x1b\\"u8);
        terminal.Write("\x1b_Ga=f,i=1,r=1,f=24,s=1,v=1;AAD/\x1b\\"u8);
        Assert.True(terminal.TryGetKittyGraphics(out GhosttyKittyGraphics? graphics));
        Assert.True(graphics!.TryGetImage(1, out GhosttyKittyGraphicsImage promoted));
        Assert.Equal(GhosttyVtNative.GhosttyKittyImageFormat.Rgba, promoted.GetFormat());
        byte[] retained = promoted.CopyData();
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, retained);

        // Four bytes must be reclaimed even though the admission limit is three.
        // The upstream assertion incorrectly required the deficit <= the limit.
        terminal.Write("\x1b_Ga=t,i=2,f=24,s=1,v=1;AP8A\x1b\\"u8);
        Assert.False(graphics.TryGetImage(1, out _));
        Assert.True(graphics.TryGetImage(2, out GhosttyKittyGraphicsImage added));
        Assert.Equal(GhosttyVtNative.GhosttyKittyImageFormat.Rgb, added.GetFormat());
        Assert.Equal(new byte[] { 0, 255, 0 }, added.CopyData());
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, retained);
    }

    [GhosttyNativeFact]
    public void DeletingDisplayedAnimationFrameSelectsItsSuccessorAndChangesGeneration()
    {
        if (!GhosttyVtHelpers.GetBuildFeatures().KittyGraphics) return;
        using GhosttyTerminal terminal = new(10, 3);
        terminal.SetKittyImageStorageLimit(1024);
        byte[][] frames = [[255, 0, 0, 255], [0, 0, 255, 255], [255, 255, 255, 255], [0, 255, 0, 255]];
        for (int i = 0; i < frames.Length; i++)
            terminal.Write(Encoding.ASCII.GetBytes($"\u001b_Ga={(i == 0 ? 't' : 'f')},i=1,f=32,s=1,v=1;{Convert.ToBase64String(frames[i])}\u001b\\"));
        terminal.Write("\u001b_Ga=a,i=1,c=2\u001b\\"u8);
        Assert.True(terminal.TryGetKittyGraphics(out GhosttyKittyGraphics? graphics));
        Assert.True(graphics!.TryGetImage(1, out GhosttyKittyGraphicsImage before));
        Assert.Equal(frames[1], before.CopyRgbaData());
        ulong generation = before.GetGeneration();

        // Upstream mixed the additional-frame index (root excluded) with the
        // displayed index (root included), returning red with stale generation.
        terminal.Write("\u001b_Ga=d,d=f,i=1,r=2\u001b\\"u8);

        Assert.True(graphics.TryGetImage(1, out GhosttyKittyGraphicsImage after));
        Assert.Equal(frames[2], after.CopyRgbaData());
        Assert.True(after.GetGeneration() > generation);
    }

    [GhosttyNativeFact]
    public void DeletingEarlierFramePreservesLastFrameGenerationAndElapsedGap()
    {
        if (!GhosttyVtHelpers.GetBuildFeatures().KittyGraphics) return;
        foreach (int removed in new[] { 1, 2 })
        {
            using GhosttyTerminal terminal = new(10, 3);
            terminal.SetKittyImageStorageLimit(1024);
            terminal.Write("\u001b_Ga=T,i=1,p=1,s=1,v=1,C=1;/wAA/w==\u001b\\"u8);
            terminal.Write("\u001b_Ga=f,i=1,s=1,v=1,z=40;AAD//w==\u001b\\"u8);
            terminal.Write("\u001b_Ga=f,i=1,s=1,v=1,z=60;AP8A/w==\u001b\\"u8);
            terminal.Write("\u001b_Ga=a,i=1,c=3,s=3\u001b\\"u8);
            Assert.True(terminal.TryGetKittyGraphics(out var graphics));
            Assert.NotNull(graphics);
            Assert.Equal(60ul, graphics.AdvanceAnimations(100));
            Assert.True(graphics.TryGetImage(1, out var before));
            ulong generation = before.GetGeneration();
            byte[] pixels = before.CopyRgbaData();

            terminal.Write(Encoding.ASCII.GetBytes($"\u001b_Ga=d,d=f,i=1,r={removed}\u001b\\"));

            Assert.True(graphics.TryGetImage(1, out var after));
            Assert.Equal(generation, after.GetGeneration());
            Assert.Equal(pixels, after.CopyRgbaData());
            Assert.Equal(40ul, graphics.AdvanceAnimations(120));
            Assert.Equal(1ul, graphics.AdvanceAnimations(159));
            Assert.Equal(removed == 1 ? 40ul : 60ul, graphics.AdvanceAnimations(160));
            Assert.True(graphics.TryGetImage(1, out var advanced));
            Assert.True(advanced.GetGeneration() > generation);
            Assert.Equal(removed == 1 ? new byte[] { 0, 0, 255, 255 } : pixels, advanced.CopyRgbaData());
        }
    }

    [Theory]
    [InlineData(GhosttyVtNative.GhosttyKittyImageFormat.Rgba, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 })]
    [InlineData(GhosttyVtNative.GhosttyKittyImageFormat.Rgb, new byte[] { 1, 2, 3, 5, 6, 7 }, new byte[] { 1, 2, 3, 255, 5, 6, 7, 255 })]
    [InlineData(GhosttyVtNative.GhosttyKittyImageFormat.Gray, new byte[] { 1, 5 }, new byte[] { 1, 1, 1, 255, 5, 5, 5, 255 })]
    [InlineData(GhosttyVtNative.GhosttyKittyImageFormat.GrayAlpha, new byte[] { 1, 4, 5, 8 }, new byte[] { 1, 1, 1, 4, 5, 5, 5, 8 })]
    public void ConvertsEveryNativePixelFormatDirectlyToOwnedRgba(
        GhosttyVtNative.GhosttyKittyImageFormat format, byte[] source, byte[] expected)
    {
        byte[] actual = GhosttyImagePixelConverter.CopyRgba(source, format, 2, 1);

        Assert.Equal(expected, actual);
        Assert.NotSame(source, actual);
    }

    [Fact]
    public void FusedPixelConversionAllocatesOnlyTheDestination()
    {
        byte[] source = new byte[3 * 512 * 512];
        _ = GhosttyImagePixelConverter.CopyRgba(source, GhosttyVtNative.GhosttyKittyImageFormat.Rgb, 512, 512);
        long before = GC.GetAllocatedBytesForCurrentThread();

        byte[] rgba = GhosttyImagePixelConverter.CopyRgba(source, GhosttyVtNative.GhosttyKittyImageFormat.Rgb, 512, 512);

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(4 * 512 * 512, rgba.Length);
        Assert.InRange(allocated, rgba.Length, rgba.Length + 128L);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(4)]
    public void RejectsMismatchedPixelPayloads(int length)
    {
        Assert.Empty(GhosttyImagePixelConverter.CopyRgba(
            new byte[length], GhosttyVtNative.GhosttyKittyImageFormat.Rgb, 1, 1));
    }

    [GhosttyNativeFact]
    public void CopiesNativeRgbDirectlyToRgba()
    {
        if (!GhosttyVtHelpers.GetBuildFeatures().KittyGraphics)
        {
            return;
        }

        using GhosttyTerminal terminal = new(80, 24);
        terminal.SetKittyImageStorageLimit(1024);
        terminal.Write("\u001b_Ga=t,t=d,f=24,i=1,s=2,v=1;AQIDBQYH\u001b\\"u8);
        Assert.True(terminal.TryGetKittyGraphics(out GhosttyKittyGraphics? graphics));
        Assert.True(graphics!.TryGetImage(1, out GhosttyKittyGraphicsImage image));

        Assert.Equal(new byte[] { 1, 2, 3, 255, 5, 6, 7, 255 }, image.CopyRgbaData());
        Assert.Equal(new byte[] { 1, 2, 3, 5, 6, 7 }, image.CopyData());
    }

    [GhosttyNativeFact]
    public void BorrowedGraphicsRejectAccessAfterTerminalDisposal()
    {
        if (!GhosttyVtHelpers.GetBuildFeatures().KittyGraphics)
        {
            return;
        }

        using GhosttyTerminal terminal = new(80, 24);
        terminal.SetKittyImageStorageLimit(1024);
        terminal.Write("\u001b_Ga=t,t=d,f=24,i=1,s=2,v=1;AQIDBQYH\u001b\\"u8);
        Assert.True(terminal.TryGetKittyGraphics(out GhosttyKittyGraphics? graphics));
        Assert.True(graphics!.TryGetImage(1, out GhosttyKittyGraphicsImage image));
        using GhosttyKittyGraphicsPlacementIterator iterator = graphics.CreatePlacementIterator();
        terminal.Dispose();

        Assert.False(graphics.IsValid);
        Assert.False(image.IsValid);
        Assert.Throws<ObjectDisposedException>(() => graphics.GetGeneration());
        Assert.Throws<ObjectDisposedException>(() => graphics.Populate(iterator));
        Assert.Throws<ObjectDisposedException>(() => graphics.TryGetImage(1, out _));
        Assert.Throws<ObjectDisposedException>(() => graphics.AdvanceAnimations(0));
        Assert.Throws<ObjectDisposedException>(() => image.GetWidth());
        Assert.Throws<ObjectDisposedException>(() => image.CopyData());
        Assert.Throws<ObjectDisposedException>(() => image.CopyRgbaData());
    }

    [GhosttyNativeFact]
    public void RejectsMalformedPngWithinNativeCallback()
    {
        if (!GhosttyVtHelpers.GetBuildFeatures().KittyGraphics)
        {
            return;
        }

        GhosttySys.EnsureSkiaPngDecoderInstalled();
        using GhosttyTerminal terminal = new(80, 24);
        terminal.SetKittyImageStorageLimit(1024);
        terminal.Write("\u001b_Ga=t,t=d,f=100,i=1;AQIDBQYH\u001b\\"u8);

        Assert.True(terminal.TryGetKittyGraphics(out GhosttyKittyGraphics? graphics));
        Assert.False(graphics!.TryGetImage(1, out _));
    }

    [GhosttyNativeFact]
    public void ImageAndStorageGenerationStampsAreMonotonic()
    {
        if (!GhosttyVtHelpers.GetBuildFeatures().KittyGraphics)
        {
            return;
        }

        using GhosttyTerminal terminal = new(80, 24);
        terminal.Resize(80, 24, 8, 16);
        terminal.SetKittyImageStorageLimit(32UL * 1024UL * 1024UL);
        terminal.SetKittyImageMediumFile(enabled: true);
        terminal.SetKittyImageMediumTempFileDirectory(Path.GetTempPath());
        terminal.Write("\u001b_Ga=T,t=d,f=24,i=1,p=1,s=1,v=2,c=10,r=1;////////\u001b\\"u8);

        Assert.True(terminal.TryGetKittyGraphics(out GhosttyKittyGraphics? graphics));
        Assert.NotNull(graphics);
        Assert.True(graphics!.TryGetImage(1, out GhosttyKittyGraphicsImage image));
        Assert.True(graphics.GetGeneration() > 0);
        Assert.InRange(image.GetGeneration(), 1ul, graphics.GetGeneration());
    }
}
