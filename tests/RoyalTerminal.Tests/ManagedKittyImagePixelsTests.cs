// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Compression;
using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class ManagedKittyImagePixelsTests
{
    [Fact]
    public void RgbOwnershipAndLazyRgbaViewKeepProtocolStorageSeparate()
    {
        byte[] rgb = [1, 2, 3, 5, 6, 7];
        ManagedKittyImagePixels pixels = ManagedKittyImagePixels.FromRgb(2, 1, rgb);
        Assert.False(pixels.IsRgba);
        Assert.Equal(6, pixels.StorageBytes);
        Assert.Equal(8, pixels.RgbaByteLength);
        Assert.True(pixels.Data.Span.Overlaps(rgb));

        KittyGraphicsDecodedImage rgba = pixels.GetRgbaImage();
        Assert.Equal(new byte[] { 1, 2, 3, 255, 5, 6, 7, 255 }, rgba.Rgba);
        Assert.Same(rgba, pixels.GetRgbaImage());
        Assert.Equal(6, pixels.StorageBytes);
        Assert.False(pixels.IsRgba);

        ManagedKittyImagePixels promoted = pixels.AsRgba();
        Assert.True(promoted.IsRgba);
        Assert.Equal(8, promoted.StorageBytes);
        Assert.Same(rgba, promoted.GetRgbaImage());
        Assert.Same(promoted, promoted.AsRgba());
        Assert.Equal(new byte[] { 1, 2, 3, 5, 6, 7 }, pixels.Data.ToArray());
    }

    [Fact]
    public void AlreadyRgbaPixelsDoNotCopyOrCreateAnotherView()
    {
        KittyGraphicsDecodedImage image = new(1, 1, [1, 2, 3, 4]);
        ManagedKittyImagePixels pixels = new(image);
        Assert.True(pixels.IsRgba);
        Assert.Equal(4, pixels.StorageBytes);
        Assert.Same(image, pixels.GetRgbaImage());
        Assert.Same(pixels, pixels.AsRgba());
    }

    [Fact]
    public void PixelMetadataAndWarmedRgbaLookupAvoidImageSizedAllocations()
    {
        _ = ManagedKittyImagePixels.FromRgb(1, 1, [1, 2, 3]).GetRgbaImage();
        byte[] rgb = new byte[256 * 256 * 3];
        long before = GC.GetAllocatedBytesForCurrentThread();
        ManagedKittyImagePixels pixels = ManagedKittyImagePixels.FromRgb(256, 256, rgb);
        long metadataBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.InRange(metadataBytes, 1, 128);
        before = GC.GetAllocatedBytesForCurrentThread();
        KittyGraphicsDecodedImage rgba = pixels.GetRgbaImage();
        long viewBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.InRange(viewBytes, 256 * 256 * 4, 256 * 256 * 4 + 128);
        before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) _ = pixels.GetRgbaImage();
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        GC.KeepAlive(rgba);
    }

    [Fact]
    public void InvalidBuffersAndDimensionsAreRejectedBeforeConversion()
    {
        Assert.Throws<ArgumentNullException>(() => ManagedKittyImagePixels.FromRgb(1, 1, null!));
        Assert.Throws<ArgumentNullException>(() => new ManagedKittyImagePixels(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => ManagedKittyImagePixels.FromRgb(0, 1, []));
        Assert.Throws<ArgumentOutOfRangeException>(() => ManagedKittyImagePixels.FromRgb(1, -1, []));
        Assert.Throws<ArgumentException>(() => ManagedKittyImagePixels.FromRgb(1, 1, [1, 2]));
        Assert.Throws<ArgumentException>(() => ManagedKittyImagePixels.FromRgb(int.MaxValue, int.MaxValue, []));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DirectAndZlibLoadersRetainRgbStorage(bool compress)
    {
        byte[] rgb = [1, 2, 3, 5, 6, 7];
        byte[] data = rgb;
        if (compress)
        {
            using MemoryStream output = new();
            using (ZLibStream encoder = new(output, CompressionLevel.Fastest, leaveOpen: true)) encoder.Write(rgb);
            data = output.ToArray();
        }
        string text = $"f=24,s=2,v=1{(compress ? ",o=z" : "")};{Convert.ToBase64String(data)}";
        Assert.True(ManagedKittyGraphicsCommand.TryParse(Encoding.ASCII.GetBytes(text), 1024, out var command));
        Assert.True(ManagedKittyImageLoader.TryCreate(command, null, 1024, out var loader, out _));
        Assert.True(loader.TryComplete(out ManagedKittyImagePixels? image, out string error), error);
        Assert.False(image.IsRgba);
        Assert.Equal(6, image.StorageBytes);
        Assert.Equal(rgb, image.Data.ToArray());
    }

    [Fact]
    public void PublicationDoesNotChangeQuotaOrEvictionGeneration()
    {
        TerminalScreen screen = new(8, 3, 10);
        ManagedKittyGraphicsStore store = new(6);
        Assert.True(store.TryAddImage(screen, 1, 0, ManagedKittyImagePixels.FromRgb(1, 1, [1, 2, 3]), false, out _));
        Assert.True(store.TryAddImage(screen, 2, 0, ManagedKittyImagePixels.FromRgb(1, 1, [5, 6, 7]), false, out _));
        ManagedKittyGraphicsStore.Image first = Assert.IsType<ManagedKittyGraphicsStore.Image>(store.Find(1));
        ulong generation = first.Generation;
        TerminalKittyImageSource published = first.Source;
        Assert.Same(published, first.Source);
        Assert.Equal(6, store.StoredBytes);
        Assert.Equal(3, first.QuotaBytes);
        Assert.Equal(generation, first.Generation);
        Assert.True(store.TryAddImage(screen, 3, 0, ManagedKittyImagePixels.FromRgb(1, 1, [9, 10, 11]), false, out _));
        Assert.Null(store.Find(1));
        Assert.NotNull(store.Find(2));
        Assert.Equal(new byte[] { 1, 2, 3, 255 }, published.RgbaPixels);
    }

    [Fact]
    public void ExplicitPromotionChangesQuotaAndGenerationExactlyOnce()
    {
        TerminalScreen screen = new(8, 3, 10);
        ManagedKittyGraphicsStore store = new(3);
        Assert.True(store.TryAddImage(screen, 1, 0, ManagedKittyImagePixels.FromRgb(1, 1, [1, 2, 3]), false, out _));
        ManagedKittyGraphicsStore.Image image = Assert.IsType<ManagedKittyGraphicsStore.Image>(store.Find(1));
        ulong original = image.Generation;
        store.ConvertImageToRgba(image);
        Assert.Equal(4, store.StoredBytes);
        Assert.Equal(4, image.QuotaBytes);
        Assert.True(image.Animation.CurrentPixels.IsRgba);
        Assert.True(image.Generation > original);
        ulong converted = image.Generation;
        store.ConvertImageToRgba(image);
        Assert.Equal(converted, image.Generation);
        Assert.Equal(4, store.StoredBytes);
        Assert.True(store.TryReserveAnimation(screen, image, 0));
        Assert.False(store.TryReserveAnimation(screen, image, 4));
    }
}
