// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using SkiaSharp;

namespace RoyalTerminal.Imaging;

// Linked into the native binding and managed rendering adapter to share validation and
// decoding without making the managed backend depend on the native binding assembly.
internal sealed class BoundedSkiaPngDecoder : IDisposable
{
    internal const int MaxDimension = 10_000;
    internal const int MaxImageBytes = 400 * 1024 * 1024;

    private readonly SKData _encoded;
    private readonly SKCodec _codec;

    private BoundedSkiaPngDecoder(SKData encoded, SKCodec codec, SKImageInfo info)
    {
        _encoded = encoded;
        _codec = codec;
        Info = info;
    }

    internal SKImageInfo Info { get; }

    internal static bool TryCreate(
        ReadOnlySpan<byte> encoded,
        int maxDecodedBytes,
        int maxEncodedBytes,
        [NotNullWhen(true)] out BoundedSkiaPngDecoder? decoder)
    {
        decoder = null;
        // Read only the fixed PNG signature/IHDR before copying encoded bytes or asking
        // Skia to allocate. Ghostty graphics_image.zig imposes these same hard bounds.
        if (encoded.Length < 33 || encoded.Length > Math.Min(maxEncodedBytes, MaxImageBytes) || maxDecodedBytes <= 0 ||
            encoded[0] != 0x89 || !encoded.Slice(1, 7).SequenceEqual("PNG\r\n\u001a\n"u8) ||
            BinaryPrimitives.ReadUInt32BigEndian(encoded[8..]) != 13 ||
            !encoded.Slice(12, 4).SequenceEqual("IHDR"u8))
        {
            return false;
        }

        uint width = BinaryPrimitives.ReadUInt32BigEndian(encoded[16..]);
        uint height = BinaryPrimitives.ReadUInt32BigEndian(encoded[20..]);
        if (width == 0 || width > MaxDimension || height == 0 || height > MaxDimension ||
            (ulong)width * height * 4 > (ulong)Math.Min(maxDecodedBytes, MaxImageBytes))
        {
            return false;
        }

        SKData data = SKData.CreateCopy(encoded);
        SKCodec? codec = null;
        try
        {
            codec = SKCodec.Create(data);
            if (codec is null || codec.EncodedFormat != SKEncodedImageFormat.Png ||
                codec.Info.Width != width || codec.Info.Height != height)
            {
                return false;
            }

            SKImageInfo info = new((int)width, (int)height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
            decoder = new BoundedSkiaPngDecoder(data, codec, info);
            return true;
        }
        finally
        {
            if (decoder is null)
            {
                codec?.Dispose();
                data.Dispose();
            }
        }
    }

    internal bool TryDecode(nint pixels)
        => _codec.GetPixels(Info, pixels, Info.RowBytes, new SKCodecOptions()) == SKCodecResult.Success;

    public void Dispose()
    {
        _codec.Dispose();
        _encoded.Dispose();
    }
}
