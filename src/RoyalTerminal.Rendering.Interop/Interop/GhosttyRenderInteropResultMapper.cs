// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Rendering.Interop - Native result translation helpers.

using System.Runtime.InteropServices;
using RoyalTerminal.Rendering.Interop.Native;

namespace RoyalTerminal.Rendering.Interop;

/// <summary>
/// Converts native renderer status codes to managed result enums and messages.
/// </summary>
public static class GhosttyRenderInteropResultMapper
{
    /// <summary>
    /// Maps a native integer result code to a managed enum value.
    /// </summary>
    public static GhosttyRenderInteropResult FromNativeCode(int resultCode) =>
        resultCode switch
        {
            0 => GhosttyRenderInteropResult.Ok,
            1 => GhosttyRenderInteropResult.InvalidArgument,
            2 => GhosttyRenderInteropResult.UnsupportedBackend,
            3 => GhosttyRenderInteropResult.UnsupportedPlatform,
            4 => GhosttyRenderInteropResult.InvalidTarget,
            5 => GhosttyRenderInteropResult.RenderFailed,
            6 => GhosttyRenderInteropResult.OutOfMemory,
            _ => GhosttyRenderInteropResult.Unknown,
        };

    /// <summary>
    /// Returns a native renderer result message.
    /// </summary>
    public static string GetMessage(int resultCode)
    {
        try
        {
            GhosttyRendererNativeLibraryLoader.Initialize();
            nint messagePtr = GhosttyRendererNative.RenderResultMessage(resultCode);
            string? nativeMessage = Marshal.PtrToStringUTF8(messagePtr);
            if (!string.IsNullOrWhiteSpace(nativeMessage))
            {
                return nativeMessage;
            }
        }
        catch (DllNotFoundException)
        {
            // Use fallback message when native library is unavailable.
        }
        catch (EntryPointNotFoundException)
        {
            // Use fallback message when the loaded library is older than expected.
        }
        catch (BadImageFormatException)
        {
            // Use fallback message when native library is unavailable.
        }

        GhosttyRenderInteropResult mapped = FromNativeCode(resultCode);
        return mapped == GhosttyRenderInteropResult.Unknown
            ? $"Renderer returned unknown result code: {resultCode}."
            : $"Renderer returned '{mapped}'.";
    }
}
