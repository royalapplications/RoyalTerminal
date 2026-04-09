// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.GhosttySharp.Native;

namespace RoyalTerminal.GhosttySharp;

/// <summary>
/// Managed lifetime wrapper for the official <c>GhosttyKeyEncoder</c> libghostty-vt API.
/// </summary>
public sealed class GhosttyKeyEncoder : IDisposable
{
    private nint _handle;
    private bool _disposed;

    /// <summary>
    /// Creates a new key encoder.
    /// </summary>
    public GhosttyKeyEncoder()
    {
        NativeLibraryLoader.Initialize();
        ThrowIfFailed(GhosttyVtNative.KeyEncoderNew(nint.Zero, out _handle), "ghostty_key_encoder_new");
    }

    /// <summary>Returns true when the native key-encoder handle is valid.</summary>
    public bool IsValid => _handle != nint.Zero && !_disposed;

    /// <summary>Copies key-encoding options from a terminal.</summary>
    public void SetFromTerminal(GhosttyTerminal terminal)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(terminal);
        GhosttyVtNative.KeyEncoderSetoptFromTerminal(_handle, terminal.Handle);
    }

    /// <summary>Encodes the supplied key event as terminal input bytes.</summary>
    public unsafe byte[] Encode(GhosttyKeyEvent keyEvent)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(keyEvent);

        GhosttyVtNative.GhosttyResult probe = GhosttyVtNative.KeyEncoderEncode(
            _handle,
            keyEvent.Handle,
            null,
            0,
            out nuint needed);

        if (probe != GhosttyVtNative.GhosttyResult.OutOfSpace &&
            probe != GhosttyVtNative.GhosttyResult.Success)
        {
            ThrowIfFailed(probe, "ghostty_key_encoder_encode(probe)");
        }

        if (needed == 0)
        {
            return [];
        }

        byte[] data = new byte[checked((int)needed)];
        fixed (byte* dataPtr = data)
        {
            ThrowIfFailed(
                GhosttyVtNative.KeyEncoderEncode(_handle, keyEvent.Handle, dataPtr, (nuint)data.Length, out nuint written),
                "ghostty_key_encoder_encode");

            if (written == (nuint)data.Length)
            {
                return data;
            }

            byte[] resized = new byte[checked((int)written)];
            Array.Copy(data, resized, resized.Length);
            return resized;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_handle != nint.Zero)
        {
            GhosttyVtNative.KeyEncoderFree(_handle);
            _handle = nint.Zero;
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
