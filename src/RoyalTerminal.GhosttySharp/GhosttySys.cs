// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.InteropServices;
using System.Text;
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
    private static GhosttyVtNative.GhosttySysLogCallback? s_logCallback;
    private static Action<GhosttySysLogMessage>? s_logSink;
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

    /// <summary>
    /// Installs or clears a process-global managed log callback for Ghostty VT diagnostics.
    /// </summary>
    public static unsafe void SetLogCallback(Action<GhosttySysLogMessage>? callback)
    {
        NativeLibraryLoader.Initialize();

        lock (s_sync)
        {
            if (callback is null)
            {
                ThrowIfFailed(
                    GhosttyVtNative.SysSet(GhosttyVtNative.GhosttySysOption.Log, null),
                    "ghostty_sys_set(log)");
                s_logSink = null;
                return;
            }

            s_logCallback ??= Log;
            nint function = Marshal.GetFunctionPointerForDelegate(s_logCallback);
            ThrowIfFailed(
                GhosttyVtNative.SysSet(GhosttyVtNative.GhosttySysOption.Log, (void*)function),
                "ghostty_sys_set(log)");
            s_logSink = callback;
        }
    }

    /// <summary>
    /// Clears the process-global managed log callback for Ghostty VT diagnostics.
    /// </summary>
    public static void ClearLogCallback()
    {
        SetLogCallback(null);
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

    private static unsafe void Log(
        void* userdata,
        GhosttyVtNative.GhosttySysLogLevel level,
        byte* scope,
        nuint scopeLength,
        byte* message,
        nuint messageLength)
    {
        Action<GhosttySysLogMessage>? sink = s_logSink;
        if (sink is null)
        {
            return;
        }

        try
        {
            string scopeText = scope is null || scopeLength == 0
                ? string.Empty
                : Encoding.UTF8.GetString(new ReadOnlySpan<byte>(scope, checked((int)scopeLength)));
            string messageText = message is null || messageLength == 0
                ? string.Empty
                : Encoding.UTF8.GetString(new ReadOnlySpan<byte>(message, checked((int)messageLength)));

            sink(new GhosttySysLogMessage(level, scopeText, messageText));
        }
        catch
        {
            // Log callbacks run from native code; exceptions must not cross the ABI boundary.
        }
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

/// <summary>
/// Managed Ghostty VT log message routed through <see cref="GhosttySys.SetLogCallback"/>.
/// </summary>
/// <param name="Level">Log severity.</param>
/// <param name="Scope">Optional log scope.</param>
/// <param name="Message">Log message text.</param>
public readonly record struct GhosttySysLogMessage(
    GhosttyVtNative.GhosttySysLogLevel Level,
    string Scope,
    string Message);
