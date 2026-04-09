// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.InteropServices;
using System.Text;
using RoyalTerminal.GhosttySharp.Native;

namespace RoyalTerminal.GhosttySharp;

/// <summary>
/// Managed helper for Ghostty's paste safety and encoding utilities.
/// </summary>
public static class GhosttyPaste
{
    /// <summary>
    /// Returns whether the supplied text is considered safe to paste.
    /// </summary>
    public static unsafe bool IsSafe(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        NativeLibraryLoader.Initialize();

        byte[] data = Encoding.UTF8.GetBytes(text);
        fixed (byte* dataPtr = data)
        {
            return GhosttyVtNative.PasteIsSafe(dataPtr, checked((nuint)data.Length));
        }
    }

    /// <summary>
    /// Encodes the supplied text using Ghostty's native paste encoder.
    /// </summary>
    public static unsafe byte[] Encode(string text, bool bracketedPaste)
    {
        ArgumentNullException.ThrowIfNull(text);
        NativeLibraryLoader.Initialize();

        byte[] data = Encoding.UTF8.GetBytes(text);
        nuint required = 0;
        fixed (byte* dataPtr = data)
        {
            GhosttyVtNative.GhosttyResult probe = GhosttyVtNative.PasteEncode(
                dataPtr,
                checked((nuint)data.Length),
                bracketedPaste,
                null,
                0,
                out required);
            if (probe != GhosttyVtNative.GhosttyResult.OutOfSpace)
            {
                ThrowIfFailed(probe, "ghostty_paste_encode(probe)");
            }

            if (required == 0)
            {
                return [];
            }

            byte[] result = new byte[checked((int)required)];
            fixed (byte* resultPtr = result)
            {
                ThrowIfFailed(
                    GhosttyVtNative.PasteEncode(
                        dataPtr,
                        checked((nuint)data.Length),
                        bracketedPaste,
                        resultPtr,
                        checked((nuint)result.Length),
                        out nuint written),
                    "ghostty_paste_encode");

                if (written == (nuint)result.Length)
                {
                    return result;
                }

                byte[] resized = new byte[checked((int)written)];
                Array.Copy(result, resized, resized.Length);
                return resized;
            }
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
