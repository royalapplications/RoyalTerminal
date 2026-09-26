// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Compression;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class ManagedKittyImageLoaderTests
{
    [Theory]
    [InlineData(24, "AQID", "AQID/w==")]
    [InlineData(32, "AQIDBA==", "AQIDBA==")]
    [InlineData(0, "AQIDBA==", "AQIDBA==")]
    public void RawFormatsConvertToRgba(int format, string input, string expected)
    {
        ManagedKittyImageLoader loader = Load($"f={format},s=1,v=1;{input}");
        Assert.True(loader.TryComplete(out var image, out string error), error);
        Assert.Equal(Convert.FromBase64String(expected), image.GetRgbaImage().Rgba);
        Assert.Equal(1, image.Width);
        Assert.Equal(1, image.Height);
    }

    [Fact]
    public void ChunksKeepInitialMetadataAndInheritNonzeroQuiet()
    {
        ManagedKittyImageLoader loader = Load("i=9,f=32,s=1,v=1,m=1,q=1;AQI=");
        Assert.True(loader.TryAppend(Command("f=24,s=99,v=99,q=2;AwQ="), out _));
        Assert.True(loader.TryComplete(out var image, out _));
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, image.GetRgbaImage().Rgba);
        Assert.Equal(9u, loader.InitialCommand.ImageId);
        Assert.Equal(2, loader.Quiet);
    }

    [Theory]
    [InlineData("s=0,v=1;", "EINVAL: dimensions required")]
    [InlineData("s=10001,v=1;", "EINVAL: dimensions too large")]
    [InlineData("s=1,v=1;AQI=", "EINVAL: invalid data")]
    [InlineData("a=f,s=1,v=1;AQI=", "ENODATA: insufficient data")]
    [InlineData("s=1,v=1;AQIDBAU=", "EINVAL: invalid data")]
    [InlineData("f=100;AQID", "EINVAL: unsupported format")]
    public void InvalidImagesReturnProtocolErrors(string command, string expected)
    {
        Assert.False(Load(command).TryComplete(out _, out string error));
        Assert.Equal(expected, error);
    }

    [Fact]
    public void AnimationFramesTruncateSurplusPixelData()
    {
        Assert.True(Load("a=f,s=1,v=1;AQIDBAU=").TryComplete(out var image, out _));
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, image.GetRgbaImage().Rgba);
    }

    [Fact]
    public void ZlibDecodingIsBoundedAndRejectsCorruptData()
    {
        using MemoryStream compressed = new();
        using (ZLibStream encoder = new(compressed, CompressionLevel.Fastest, leaveOpen: true)) encoder.Write([1, 2, 3, 4]);
        string data = Convert.ToBase64String(compressed.ToArray());
        Assert.True(Load($"o=z,s=1,v=1;{data}").TryComplete(out var image, out _));
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, image.GetRgbaImage().Rgba);
        Assert.False(Load("o=z,s=1,v=1;AQID").TryComplete(out _, out string error));
        Assert.Equal("EINVAL: decompression failed", error);

        using MemoryStream bomb = new();
        using (ZLibStream encoder = new(bomb, CompressionLevel.SmallestSize, leaveOpen: true)) encoder.Write(new byte[4096]);
        ManagedKittyGraphicsCommand command = Command($"o=z,s=1,v=1;{Convert.ToBase64String(bomb.ToArray())}");
        Assert.True(ManagedKittyImageLoader.TryCreate(command, null, 128, out var loader, out _));
        Assert.False(loader.TryComplete(out _, out error));
        Assert.Equal("EINVAL: decompression failed", error);
    }

    [Fact]
    public void EncodedAndDecodedLimitsRejectBeforeLargeAllocation()
    {
        Assert.False(ManagedKittyImageLoader.TryCreate(Command("s=1,v=1;AQIDBA=="), null, 3, out _, out _));
        Assert.True(ManagedKittyImageLoader.TryCreate(Command("f=24,s=1,v=1;AQID"), null, 3, out var loader, out _));
        Assert.False(loader.TryComplete(out _, out _));
        Assert.False(ManagedKittyImageLoader.TryCreate(Command("f=99,i=1"), null, 128, out _, out string error));
        Assert.Equal("EINVAL: unsupported format", error);
    }

    [Fact]
    public void ExternalMediaRequireCapabilityAndPassValidatedRangeHints()
    {
        ManagedKittyGraphicsCommand command = Command("t=s,f=24,s=1,v=1,O=5,S=3;L2ltYWdl");
        Assert.False(ManagedKittyImageLoader.TryCreate(command, null, 128, out _, out string error));
        Assert.Equal("EINVAL: unsupported medium", error);
        FakeMedium reader = new();
        Assert.True(ManagedKittyImageLoader.TryCreate(command, null, 128, out var loader, out _, reader));
        Assert.Equal(new KittyGraphicsMediumRequest(KittyGraphicsMedium.SharedMemory, command.Data, 5, 3, 3), reader.Request);
        Assert.True(loader.TryComplete(out var image, out _));
        Assert.Equal(new byte[] { 1, 2, 3, 255 }, image.GetRgbaImage().Rgba);
    }

    [Fact]
    public void PngPixelsKeepTheirRgbaAccountingAndOwnedBuffer()
    {
        FakePng decoder = new();
        Assert.True(ManagedKittyImageLoader.TryCreate(Command("f=100;AA=="), decoder, 4, out var loader, out _));
        Assert.True(loader.TryComplete(out var image, out _));
        Assert.True(image.IsRgba);
        Assert.Equal(4, image.StorageBytes);
        Assert.Same(decoder.Image, image.GetRgbaImage());
    }

    private sealed class FakePng : IKittyGraphicsPngDecoder
    {
        internal KittyGraphicsDecodedImage Image { get; } = new(1, 1, [1, 2, 3, 4]);
        public bool TryDecode(ReadOnlySpan<byte> encoded, int maxDecodedBytes,
            [NotNullWhen(true)] out KittyGraphicsDecodedImage? image)
        {
            image = Image;
            return true;
        }
    }

    private sealed class FakeMedium : IKittyGraphicsMediumReader
    {
        internal KittyGraphicsMediumRequest Request;
        public bool TryRead(KittyGraphicsMediumRequest request, int maxBytes, [NotNullWhen(true)] out byte[]? data, out string? error)
        {
            Request = request;
            data = [1, 2, 3];
            error = null;
            return true;
        }
    }

    private static ManagedKittyImageLoader Load(string command)
    {
        Assert.True(ManagedKittyImageLoader.TryCreate(Command(command), null, 1024, out var loader, out string error), error);
        return loader;
    }

    private static ManagedKittyGraphicsCommand Command(string command)
    {
        Assert.True(ManagedKittyGraphicsCommand.TryParse(Encoding.ASCII.GetBytes(command), 1024, out var parsed));
        return parsed;
    }
}
