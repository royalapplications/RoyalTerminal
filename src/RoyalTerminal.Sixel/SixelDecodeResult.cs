// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Sixel - Decode result.

namespace RoyalTerminal.Sixel;

/// <summary>
/// Result returned by <see cref="SixelDecoder"/>.
/// </summary>
public sealed class SixelDecodeResult
{
    internal SixelDecodeResult(
        SixelDecodeStatus status,
        SixelImage? image,
        int finalCursorX,
        int finalCursorY,
        string? message)
    {
        Status = status;
        Image = image;
        FinalCursorX = Math.Max(0, finalCursorX);
        FinalCursorY = Math.Max(0, finalCursorY);
        Message = message;
    }

    /// <summary>Gets whether decoding succeeded and produced an image.</summary>
    public bool Success => Status == SixelDecodeStatus.Success && Image is not null;

    /// <summary>Decode status.</summary>
    public SixelDecodeStatus Status { get; }

    /// <summary>Decoded image when <see cref="Success"/> is true.</summary>
    public SixelImage? Image { get; }

    /// <summary>Final sixel graphics cursor X offset in pixels.</summary>
    public int FinalCursorX { get; }

    /// <summary>Final sixel graphics cursor Y offset in pixels.</summary>
    public int FinalCursorY { get; }

    /// <summary>Optional diagnostic message for failed decodes.</summary>
    public string? Message { get; }

    internal static SixelDecodeResult Failure(SixelDecodeStatus status, string message)
        => new(status, null, 0, 0, message);

    internal static SixelDecodeResult FromImage(SixelImage image, int finalCursorX, int finalCursorY)
        => new(SixelDecodeStatus.Success, image, finalCursorX, finalCursorY, null);
}
