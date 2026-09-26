// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Diagnostics.CodeAnalysis;
using RoyalTerminal.Imaging;
using RoyalTerminal.Terminal;

namespace RoyalTerminal.Avalonia.Rendering;

/// <summary>Decodes bounded Kitty PNG images using Skia, without native Ghostty dependencies.</summary>
public sealed class SkiaKittyGraphicsPngDecoder : IKittyGraphicsPngDecoder
{
    private readonly int _maxEncodedBytes;

    /// <summary>Creates a decoder with an encoded-payload cap (default 400 MiB, the Ghostty hard limit).</summary>
    public SkiaKittyGraphicsPngDecoder(int maxEncodedBytes = 400 * 1024 * 1024)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxEncodedBytes);
        _maxEncodedBytes = maxEncodedBytes;
    }

    /// <inheritdoc />
    public unsafe bool TryDecode(
        ReadOnlySpan<byte> encoded,
        int maxDecodedBytes,
        [NotNullWhen(true)] out KittyGraphicsDecodedImage? image)
    {
        image = null;
        try
        {
            if (!BoundedSkiaPngDecoder.TryCreate(encoded, maxDecodedBytes, _maxEncodedBytes, out BoundedSkiaPngDecoder? decoder))
            {
                return false;
            }

            using (decoder)
            {
                byte[] pixels = GC.AllocateUninitializedArray<byte>(decoder.Info.BytesSize);
                fixed (byte* pointer = pixels)
                {
                    if (!decoder.TryDecode((nint)pointer))
                    {
                        return false;
                    }
                }

                image = new KittyGraphicsDecodedImage(decoder.Info.Width, decoder.Info.Height, pixels);
                return true;
            }
        }
        catch (Exception exception) when (exception is not StackOverflowException)
        {
            // Terminal-controlled malformed input or codec/allocation failure is rejected.
            return false;
        }
    }
}
