// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.GhosttySharp.Native;

namespace RoyalTerminal.GhosttySharp;

/// <summary>
/// Managed lifetime wrapper for the official <c>GhosttyMouseEvent</c> libghostty-vt API.
/// </summary>
public sealed class GhosttyMouseEvent : IDisposable
{
    private nint _handle;
    private bool _disposed;

    /// <summary>
    /// Creates a new mouse event.
    /// </summary>
    public GhosttyMouseEvent()
    {
        NativeLibraryLoader.Initialize();
        ThrowIfFailed(GhosttyVtNative.MouseEventNew(nint.Zero, out _handle), "ghostty_mouse_event_new");
    }

    /// <summary>Returns true when the native mouse-event handle is valid.</summary>
    public bool IsValid => _handle != nint.Zero && !_disposed;

    internal nint Handle
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _handle;
        }
    }

    /// <summary>Sets the mouse action.</summary>
    public void SetAction(GhosttyVtNative.GhosttyMouseAction action)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        GhosttyVtNative.MouseEventSetAction(_handle, action);
    }

    /// <summary>Gets the mouse action.</summary>
    public GhosttyVtNative.GhosttyMouseAction GetAction()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return GhosttyVtNative.MouseEventGetAction(_handle);
    }

    /// <summary>Sets the mouse button.</summary>
    public void SetButton(GhosttyVtNative.GhosttyMouseButtonId button)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        GhosttyVtNative.MouseEventSetButton(_handle, button);
    }

    /// <summary>Clears the mouse button.</summary>
    public void ClearButton()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        GhosttyVtNative.MouseEventClearButton(_handle);
    }

    /// <summary>Attempts to get the active mouse button.</summary>
    public bool TryGetButton(out GhosttyVtNative.GhosttyMouseButtonId button)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return GhosttyVtNative.MouseEventGetButton(_handle, out button);
    }

    /// <summary>Sets the keyboard modifiers held during the mouse event.</summary>
    public void SetModifiers(GhosttyVtNative.GhosttyVtMods modifiers)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        GhosttyVtNative.MouseEventSetMods(_handle, modifiers);
    }

    /// <summary>Gets the keyboard modifiers held during the mouse event.</summary>
    public GhosttyVtNative.GhosttyVtMods GetModifiers()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return GhosttyVtNative.MouseEventGetMods(_handle);
    }

    /// <summary>Sets the mouse position in surface-space pixels.</summary>
    public void SetPosition(float x, float y)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        GhosttyVtNative.MouseEventSetPosition(_handle, new GhosttyVtNative.GhosttyMousePosition
        {
            X = x,
            Y = y,
        });
    }

    /// <summary>Gets the mouse position in surface-space pixels.</summary>
    public GhosttyVtNative.GhosttyMousePosition GetPosition()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return GhosttyVtNative.MouseEventGetPosition(_handle);
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
            GhosttyVtNative.MouseEventFree(_handle);
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
