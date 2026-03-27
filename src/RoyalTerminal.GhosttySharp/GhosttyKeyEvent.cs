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

    /// <summary>Sets the UTF-8 text payload for this event.</summary>
    public unsafe void SetText(string? text)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (string.IsNullOrEmpty(text))
        {
            GhosttyVtNative.KeyEventSetUtf8(_handle, null, 0);
            return;
        }

        byte[] utf8 = Encoding.UTF8.GetBytes(text);
        fixed (byte* utf8Ptr = utf8)
        {
            GhosttyVtNative.KeyEventSetUtf8(_handle, utf8Ptr, (nuint)utf8.Length);
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
