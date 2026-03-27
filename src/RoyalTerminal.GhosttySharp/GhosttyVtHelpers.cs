// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.GhosttySharp.Native;

namespace RoyalTerminal.GhosttySharp;

/// <summary>
/// Managed helpers for protocol and build-info utilities in <c>libghostty-vt</c>.
/// </summary>
public static class GhosttyVtHelpers
{
    /// <summary>
    /// Snapshot of compile-time capabilities reported by <c>libghostty-vt</c>.
    /// </summary>
    public readonly struct GhosttyBuildFeatures
    {
        public GhosttyBuildFeatures(
            bool simd,
            bool kittyGraphics,
            bool tmuxControlMode,
            GhosttyVtNative.GhosttyOptimizeMode optimizeMode)
        {
            Simd = simd;
            KittyGraphics = kittyGraphics;
            TmuxControlMode = tmuxControlMode;
            OptimizeMode = optimizeMode;
        }

        public bool Simd { get; }
        public bool KittyGraphics { get; }
        public bool TmuxControlMode { get; }
        public GhosttyVtNative.GhosttyOptimizeMode OptimizeMode { get; }
    }

    /// <summary>Gets the compile-time feature set exposed by the native library.</summary>
    public static unsafe GhosttyBuildFeatures GetBuildFeatures()
    {
        NativeLibraryLoader.Initialize();

        bool simd = default;
        bool kittyGraphics = default;
        bool tmuxControlMode = default;
        GhosttyVtNative.GhosttyOptimizeMode optimize = default;

        ThrowIfFailed(GhosttyVtNative.BuildInfo(GhosttyVtNative.GhosttyBuildInfoData.Simd, &simd), "ghostty_build_info(simd)");
        ThrowIfFailed(
            GhosttyVtNative.BuildInfo(GhosttyVtNative.GhosttyBuildInfoData.KittyGraphics, &kittyGraphics),
            "ghostty_build_info(kitty_graphics)");
        ThrowIfFailed(
            GhosttyVtNative.BuildInfo(GhosttyVtNative.GhosttyBuildInfoData.TmuxControlMode, &tmuxControlMode),
            "ghostty_build_info(tmux_control_mode)");
        ThrowIfFailed(
            GhosttyVtNative.BuildInfo(GhosttyVtNative.GhosttyBuildInfoData.Optimize, &optimize),
            "ghostty_build_info(optimize)");

        return new GhosttyBuildFeatures(simd, kittyGraphics, tmuxControlMode, optimize);
    }

    /// <summary>Encodes a focus event report as UTF-8 bytes.</summary>
    public static unsafe byte[] EncodeFocus(GhosttyVtNative.GhosttyFocusEvent @event)
    {
        GhosttyVtNative.GhosttyResult probe = GhosttyVtNative.FocusEncode(@event, null, 0, out nuint needed);
        if (probe != GhosttyVtNative.GhosttyResult.OutOfSpace)
        {
            ThrowIfFailed(probe, "ghostty_focus_encode(probe)");
        }

        return EncodeBuffer(needed, (byte* buffer, nuint length, out nuint written) =>
            GhosttyVtNative.FocusEncode(@event, buffer, length, out written));
    }

    /// <summary>Encodes a focus event report as a UTF-8 string.</summary>
    public static string EncodeFocusString(GhosttyVtNative.GhosttyFocusEvent @event)
        => Encoding.UTF8.GetString(EncodeFocus(@event));

    /// <summary>Encodes a DECRPM mode report as UTF-8 bytes.</summary>
    public static unsafe byte[] EncodeModeReport(
        GhosttyVtNative.GhosttyMode mode,
        GhosttyVtNative.GhosttyModeReportState state)
    {
        GhosttyVtNative.GhosttyResult probe = GhosttyVtNative.ModeReportEncode(mode, state, null, 0, out nuint needed);
        if (probe != GhosttyVtNative.GhosttyResult.OutOfSpace)
        {
            ThrowIfFailed(probe, "ghostty_mode_report_encode(probe)");
        }

        return EncodeBuffer(needed, (byte* buffer, nuint length, out nuint written) =>
            GhosttyVtNative.ModeReportEncode(mode, state, buffer, length, out written));
    }

    /// <summary>Encodes a DECRPM mode report as a UTF-8 string.</summary>
    public static string EncodeModeReportString(
        GhosttyVtNative.GhosttyMode mode,
        GhosttyVtNative.GhosttyModeReportState state)
        => Encoding.UTF8.GetString(EncodeModeReport(mode, state));

    /// <summary>Encodes a terminal size report as UTF-8 bytes.</summary>
    public static unsafe byte[] EncodeSizeReport(
        GhosttyVtNative.GhosttySizeReportStyle style,
        GhosttyVtNative.GhosttySizeReportSize size)
    {
        GhosttyVtNative.GhosttyResult probe = GhosttyVtNative.SizeReportEncode(style, size, null, 0, out nuint needed);
        if (probe != GhosttyVtNative.GhosttyResult.OutOfSpace)
        {
            ThrowIfFailed(probe, "ghostty_size_report_encode(probe)");
        }

        return EncodeBuffer(needed, (byte* buffer, nuint length, out nuint written) =>
            GhosttyVtNative.SizeReportEncode(style, size, buffer, length, out written));
    }

    /// <summary>Encodes a terminal size report as a UTF-8 string.</summary>
    public static string EncodeSizeReportString(
        GhosttyVtNative.GhosttySizeReportStyle style,
        GhosttyVtNative.GhosttySizeReportSize size)
        => Encoding.UTF8.GetString(EncodeSizeReport(style, size));

    private unsafe delegate GhosttyVtNative.GhosttyResult EncodeDelegate(byte* buffer, nuint bufferLength, out nuint written);

    private static unsafe byte[] EncodeBuffer(nuint needed, EncodeDelegate encoder)
    {
        if (needed == 0)
        {
            return [];
        }

        byte[] data = new byte[checked((int)needed)];
        fixed (byte* dataPtr = data)
        {
            ThrowIfFailed(encoder(dataPtr, (nuint)data.Length, out nuint written), "encode");
            if (written == (nuint)data.Length)
            {
                return data;
            }

            byte[] resized = new byte[checked((int)written)];
            Array.Copy(data, resized, resized.Length);
            return resized;
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
