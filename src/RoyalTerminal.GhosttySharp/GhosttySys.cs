// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.InteropServices;
using RoyalTerminal.GhosttySharp.Native;
using SkiaSharp;

namespace RoyalTerminal.GhosttySharp;

/// <summary>
/// Process-global runtime helpers for optional <c>libghostty-vt</c> system hooks.
/// </summary>
public static class GhosttySys
{
    private static readonly object s_sync = new();
    private static GhosttyVtNative.GhosttySysDecodePngCallback? s_decodePngCallback;
    private static bool s_skiaPngDecoderInstalled;

    /// <summary>
    /// Installs a Skia-backed PNG decoder for Kitty Graphics support.
    /// Safe to call multiple times.
    /// </summary>
    public static unsafe void EnsureSkiaPngDecoderInstalled()
    {
        NativeLibraryLoader.Initialize();

        lock (s_sync)
        {
            if (s_skiaPngDecoderInstalled)
            {
                return;
            }

            s_decodePngCallback ??= DecodePng;
            nint callback = Marshal.GetFunctionPointerForDelegate(s_decodePngCallback);
            ThrowIfFailed(
                GhosttyVtNative.SysSet(GhosttyVtNative.GhosttySysOption.DecodePng, (void*)callback),
                "ghostty_sys_set(decode_png)");

            s_skiaPngDecoderInstalled = true;
        }
    }

    private static unsafe byte DecodePng(
        void* userdata,
        GhosttyVtNative.GhosttyAllocator* allocator,
        byte* data,
        nuint dataLength,
        GhosttyVtNative.GhosttySysImage* output)
    {
        if (data is null || output is null || dataLength == 0)
        {
            return 0;
        }

        byte[] pngData = new byte[checked((int)dataLength)];
        Marshal.Copy((nint)data, pngData, 0, pngData.Length);

        using SKData encoded = SKData.CreateCopy(pngData);
        using SKCodec? codec = SKCodec.Create(encoded);
        if (codec is null)
        {
            return 0;
        }

        SKImageInfo info = new(
            codec.Info.Width,
            codec.Info.Height,
            SKColorType.Rgba8888,
            SKAlphaType.Unpremul);
        nuint rgbaLength = checked((nuint)info.BytesSize);
        byte* pixels = GhosttyVtNative.Alloc(allocator, rgbaLength);
        if (pixels is null)
        {
            return 0;
        }

        SKCodecResult result = codec.GetPixels(info, (nint)pixels, info.RowBytes, new SKCodecOptions());
        if (result != SKCodecResult.Success)
        {
            GhosttyVtNative.Free(allocator, pixels, rgbaLength);
            return 0;
        }

        output->Width = checked((uint)info.Width);
        output->Height = checked((uint)info.Height);
        output->Data = pixels;
        output->DataLength = rgbaLength;
        return 1;
    }

    private static void ThrowIfFailed(GhosttyVtNative.GhosttyResult result, string operation)
    {
        if (result == GhosttyVtNative.GhosttyResult.Success)
        {
            return;
        }

        throw new InvalidOperationException($"{operation} failed with {result}.");
    }
}
