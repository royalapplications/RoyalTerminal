// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.GhosttySharp.Native;

namespace RoyalTerminal.GhosttySharp;

/// <summary>
/// Managed lifetime wrapper for the official <c>GhosttyKeyEvent</c> libghostty-vt API.
/// </summary>
public sealed class GhosttyKeyEvent : IDisposable
{
    private nint _handle;
    private bool _disposed;
    // Native set_utf8 borrows this memory until replacement/event disposal.
    // A pinned object-heap buffer is retained across setter/encoder calls.
    private byte[]? _utf8Buffer;

    /// <summary>
    /// Creates a new key event.
    /// </summary>
    public GhosttyKeyEvent()
    {
        NativeLibraryLoader.Initialize();
        ThrowIfFailed(GhosttyVtNative.KeyEventNew(nint.Zero, out _handle), "ghostty_key_event_new");
    }

    /// <summary>Returns true when the native key-event handle is valid.</summary>
    public bool IsValid => _handle != nint.Zero && !_disposed;

    internal nint Handle
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _handle;
        }
    }

    /// <summary>Sets the key action.</summary>
    public void SetAction(GhosttyVtNative.GhosttyVtKeyAction action)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        GhosttyVtNative.KeyEventSetAction(_handle, action);
    }

    /// <summary>Sets the logical key identity.</summary>
    public void SetKey(GhosttyVtNative.GhosttyVtKey key)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        GhosttyVtNative.KeyEventSetKey(_handle, key);
    }

    /// <summary>Sets held modifier flags.</summary>
    public void SetModifiers(GhosttyVtNative.GhosttyVtMods modifiers)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        GhosttyVtNative.KeyEventSetMods(_handle, modifiers);
    }

    /// <summary>Sets consumed modifier flags.</summary>
    public void SetConsumedModifiers(GhosttyVtNative.GhosttyVtMods modifiers)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        GhosttyVtNative.KeyEventSetConsumedMods(_handle, modifiers);
    }

    /// <summary>Sets whether the event is part of active composition.</summary>
    public void SetComposing(bool composing)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        GhosttyVtNative.KeyEventSetComposing(_handle, composing);
    }

    /// <summary>Copies text into event-owned pinned UTF-8 storage, retained until replacement or disposal.</summary>
    public unsafe void SetText(string? text)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (string.IsNullOrEmpty(text))
        {
            GhosttyVtNative.KeyEventSetUtf8(_handle, null, 0);
            return;
        }

        int length = Encoding.UTF8.GetByteCount(text);
        if (_utf8Buffer is null || _utf8Buffer.Length < length)
            _utf8Buffer = GC.AllocateUninitializedArray<byte>(Math.Max(32, length), pinned: true);
        Encoding.UTF8.GetBytes(text, _utf8Buffer);
        fixed (byte* utf8Ptr = _utf8Buffer)
        {
            GhosttyVtNative.KeyEventSetUtf8(_handle, utf8Ptr, (nuint)length);
        }
    }

    /// <summary>Sets the unshifted codepoint associated with the physical key.</summary>
    public void SetUnshiftedCodepoint(uint codepoint)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        GhosttyVtNative.KeyEventSetUnshiftedCodepoint(_handle, codepoint);
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
            GhosttyVtNative.KeyEventFree(_handle);
            _handle = nint.Zero;
        }
        _utf8Buffer = null;
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
