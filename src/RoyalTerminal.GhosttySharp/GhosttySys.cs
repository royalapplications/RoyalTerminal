// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using RoyalTerminal.GhosttySharp.Native;
using RoyalTerminal.Imaging;

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
    /// Uses .NET's cryptographic random source for Ghostty secrets. Configure
    /// this process-global hook at startup before using terminal instances.
    /// </summary>
    public static unsafe void UseManagedSecureRandom()
    {
        NativeLibraryLoader.Initialize();
        ThrowIfFailed(
            GhosttyVtNative.SysSet(
                GhosttyVtNative.GhosttySysOption.RandomSecure,
                (void*)(delegate* unmanaged[Cdecl]<nint, byte*, nuint, byte>)&FillSecureRandom),
            "ghostty_sys_set(random_secure)");
    }

    /// <summary>Restores Ghostty's platform cryptographic random source at startup.</summary>
    public static unsafe void UsePlatformSecureRandom()
    {
        NativeLibraryLoader.Initialize();
        ThrowIfFailed(
            GhosttyVtNative.SysSet(GhosttyVtNative.GhosttySysOption.RandomSecure, null),
            "ghostty_sys_set(random_secure)");
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe byte FillSecureRandom(nint userdata, byte* buffer, nuint length)
    {
        try
        {
            RandomNumberGenerator.Fill(new Span<byte>(buffer, checked((int)length)));
            return 1;
        }
        catch
        {
            // Report unavailable entropy without crossing the unmanaged boundary.
            return 0;
        }
    }

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
        if (data is null || output is null || dataLength == 0 || dataLength > BoundedSkiaPngDecoder.MaxImageBytes)
        {
            return 0;
        }

        byte* pixels = null;
        nuint rgbaLength = 0;
        try
        {
            if (!BoundedSkiaPngDecoder.TryCreate(
                new ReadOnlySpan<byte>(data, (int)dataLength),
                BoundedSkiaPngDecoder.MaxImageBytes,
                BoundedSkiaPngDecoder.MaxImageBytes,
                out BoundedSkiaPngDecoder? decoder))
            {
                return 0;
            }

            using BoundedSkiaPngDecoder boundedDecoder = decoder;
            rgbaLength = checked((nuint)decoder.Info.BytesSize);
            pixels = GhosttyVtNative.Alloc(allocator, rgbaLength);
            if (pixels is null)
            {
                return 0;
            }

            if (!decoder.TryDecode((nint)pixels))
            {
                return 0;
            }

            output->Width = checked((uint)decoder.Info.Width);
            output->Height = checked((uint)decoder.Info.Height);
            output->Data = pixels;
            output->DataLength = rgbaLength;
            pixels = null; // Successful decoding transfers ownership to Ghostty.
            return 1;
        }
        catch
        {
            // Reject malformed input and allocation/decode failures within the ABI boundary.
            return 0;
        }
        finally
        {
            if (pixels is not null)
            {
                GhosttyVtNative.Free(allocator, pixels, rgbaLength);
            }
        }
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
