// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using SkiaSharp;
using System.Buffers.Binary;

namespace RoyalTerminal.Avalonia.App.Services.Notifications;

internal static class NotificationImageDecoder
{
    internal static unsafe NotificationImage? Decode(ReadOnlyMemory<byte> encoded)
    {
        if (encoded.IsEmpty || encoded.Length > 1024 * 1024) return null;
        if (encoded.Length >= 24 && encoded.Span[0] == 0x89 && encoded.Span.Slice(1, 7).SequenceEqual("PNG\r\n\u001a\n"u8) &&
            !DimensionsValid(BinaryPrimitives.ReadUInt32BigEndian(encoded.Span[16..]), BinaryPrimitives.ReadUInt32BigEndian(encoded.Span[20..]))) return null;
        if (encoded.Length >= 10 && encoded.Span[..3].SequenceEqual("GIF"u8) &&
            !DimensionsValid(BinaryPrimitives.ReadUInt16LittleEndian(encoded.Span[6..]), BinaryPrimitives.ReadUInt16LittleEndian(encoded.Span[8..]))) return null;
        try
        {
            using SKData data = SKData.CreateCopy(encoded.Span);
            using SKCodec? codec = SKCodec.Create(data);
            if (codec is null || codec.EncodedFormat is not (SKEncodedImageFormat.Png or SKEncodedImageFormat.Jpeg or SKEncodedImageFormat.Gif)) return null;
            SKImageInfo info = codec.Info;
            if (!DimensionsValid((uint)info.Width, (uint)info.Height)) return null;
            // Decode only the first frame and send straight (not premultiplied) RGBA.
            info = new(info.Width, info.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
            byte[] rgba = GC.AllocateUninitializedArray<byte>(info.BytesSize);
            fixed (byte* pointer = rgba)
                if (codec.GetPixels(info, (nint)pointer) != SKCodecResult.Success) return null;
            return new(info.Width, info.Height, rgba);
        }
        catch (Exception) { return null; }
    }

    private static bool DimensionsValid(uint width, uint height)
        => width is > 0 and <= 2048 && height is > 0 and <= 2048 && (ulong)width * height <= 1024 * 1024;
}
