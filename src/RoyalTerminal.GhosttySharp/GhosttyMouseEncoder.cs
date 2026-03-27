// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.GhosttySharp.Native;

namespace RoyalTerminal.GhosttySharp;

/// <summary>
/// Managed lifetime wrapper for the official <c>GhosttyMouseEncoder</c> libghostty-vt API.
/// </summary>
public sealed class GhosttyMouseEncoder : IDisposable
{
    private nint _handle;
    private bool _disposed;

    /// <summary>
    /// Creates a new mouse encoder.
    /// </summary>
    public GhosttyMouseEncoder()
    {
        NativeLibraryLoader.Initialize();
        ThrowIfFailed(GhosttyVtNative.MouseEncoderNew(nint.Zero, out _handle), "ghostty_mouse_encoder_new");
    }

    /// <summary>Returns true when the native mouse-encoder handle is valid.</summary>
    public bool IsValid => _handle != nint.Zero && !_disposed;

    /// <summary>Copies tracking mode and format from a terminal.</summary>
    public void SetFromTerminal(GhosttyTerminal terminal)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(terminal);
        GhosttyVtNative.MouseEncoderSetoptFromTerminal(_handle, terminal.Handle);
    }

    /// <summary>Sets the mouse tracking mode.</summary>
    public unsafe void SetTrackingMode(GhosttyVtNative.GhosttyMouseTrackingMode mode)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        SetOption(GhosttyVtNative.GhosttyMouseEncoderOption.Event, mode);
    }

    /// <summary>Sets the mouse output format.</summary>
    public unsafe void SetFormat(GhosttyVtNative.GhosttyMouseFormat format)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        SetOption(GhosttyVtNative.GhosttyMouseEncoderOption.Format, format);
    }

    /// <summary>Sets the surface and cell geometry used for coordinate encoding.</summary>
    public unsafe void SetSize(GhosttyVtNative.GhosttyMouseEncoderSize size)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        SetOption(GhosttyVtNative.GhosttyMouseEncoderOption.Size, size);
    }

    /// <summary>Sets whether any mouse button is currently pressed.</summary>
    public unsafe void SetAnyButtonPressed(bool value)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        SetOption(GhosttyVtNative.GhosttyMouseEncoderOption.AnyButtonPressed, value);
    }

    /// <summary>Sets whether duplicate motion in the same cell should be suppressed.</summary>
    public unsafe void SetTrackLastCell(bool value)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        SetOption(GhosttyVtNative.GhosttyMouseEncoderOption.TrackLastCell, value);
    }

    /// <summary>Clears internal encoder state such as last-cell tracking.</summary>
    public void Reset()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        GhosttyVtNative.MouseEncoderReset(_handle);
    }

    /// <summary>Encodes the given mouse event as UTF-8 bytes.</summary>
    public unsafe byte[] Encode(GhosttyMouseEvent mouseEvent)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(mouseEvent);

        GhosttyVtNative.GhosttyResult probe = GhosttyVtNative.MouseEncoderEncode(
            _handle,
            mouseEvent.Handle,
            null,
            0,
            out nuint needed);

        if (probe != GhosttyVtNative.GhosttyResult.OutOfSpace &&
            probe != GhosttyVtNative.GhosttyResult.Success)
        {
            ThrowIfFailed(probe, "ghostty_mouse_encoder_encode(probe)");
        }

        if (needed == 0)
        {
            return [];
        }

        byte[] data = new byte[checked((int)needed)];
        fixed (byte* dataPtr = data)
        {
            ThrowIfFailed(
                GhosttyVtNative.MouseEncoderEncode(_handle, mouseEvent.Handle, dataPtr, (nuint)data.Length, out nuint written),
                "ghostty_mouse_encoder_encode");

            if (written == (nuint)data.Length)
            {
                return data;
            }

            byte[] resized = new byte[checked((int)written)];
            Array.Copy(data, resized, resized.Length);
            return resized;
        }
    }

    /// <summary>Encodes the given mouse event as a UTF-8 string.</summary>
    public string EncodeToString(GhosttyMouseEvent mouseEvent)
    {
        byte[] data = Encode(mouseEvent);
        return data.Length == 0 ? string.Empty : Encoding.UTF8.GetString(data);
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
            GhosttyVtNative.MouseEncoderFree(_handle);
            _handle = nint.Zero;
        }
    }

    private unsafe void SetOption<T>(GhosttyVtNative.GhosttyMouseEncoderOption option, T value)
        where T : unmanaged
    {
        T copy = value;
        GhosttyVtNative.MouseEncoderSetopt(_handle, option, &copy);
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
