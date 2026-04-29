// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Sixel - Decoder options.

namespace RoyalTerminal.Sixel;

/// <summary>
/// Resource and compatibility options for <see cref="SixelDecoder"/>.
/// </summary>
public sealed record SixelDecoderOptions
{
    /// <summary>Default decoder limits.</summary>
    public static SixelDecoderOptions Default { get; } = new();

    /// <summary>Maximum encoded DCS payload bytes accepted by the decoder.</summary>
    public int MaxInputBytes { get; init; } = 8 * 1024 * 1024;

    /// <summary>Maximum decoded image width in pixels.</summary>
    public int MaxWidth { get; init; } = 4096;

    /// <summary>Maximum decoded image height in pixels.</summary>
    public int MaxHeight { get; init; } = 4096;

    /// <summary>Maximum decoded pixel count.</summary>
    public int MaxPixels { get; init; } = 16 * 1024 * 1024;

    /// <summary>Maximum number of sixel color registers.</summary>
    public int MaxColorRegisters { get; init; } = 1024;

    /// <summary>Maximum vertical device-pixel scale applied to one sixel pixel.</summary>
    public int MaxPixelAspectRatio { get; init; } = 64;

    /// <summary>
    /// Whether a sixel payload without an explicit background mode starts with
    /// transparent pixels. DEC background mode 0 or 2 still requests an opaque
    /// background, and mode 1 requests transparency.
    /// </summary>
    public bool DefaultTransparentBackground { get; init; } = true;
}
