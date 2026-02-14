// Licensed under the MIT License.
// GhosttySharp - .NET bindings for the Ghostty terminal emulator.

using GhosttySharp.Native;

namespace GhosttySharp;

/// <summary>
/// Managed wrapper for the Ghostty inspector.
/// Provides input forwarding and lifecycle management for the debug inspector.
/// </summary>
public sealed class GhosttyInspector : IDisposable
{
    private nint _handle;
    private readonly nint _surfaceHandle;
    private readonly bool _ownsHandle;
    private bool _disposed;

    /// <summary>Gets the native inspector handle.</summary>
    public nint Handle
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _handle;
        }
    }

    /// <summary>Returns true if the inspector handle is valid.</summary>
    public bool IsValid => _handle != nint.Zero && !_disposed;

    /// <summary>Creates an inspector for the given surface.</summary>
    public GhosttyInspector(GhosttySurface surface)
    {
        _surfaceHandle = surface.Handle;
        _handle = GhosttyNative.SurfaceInspector(_surfaceHandle);
        if (_handle == nint.Zero)
            throw new InvalidOperationException("Failed to create Ghostty inspector.");
        _ownsHandle = true;
    }

    /// <summary>
    /// Creates a managed wrapper for an existing inspector handle.
    /// Intended for testing and advanced interop scenarios.
    /// </summary>
    internal GhosttyInspector(nint handle, nint surfaceHandle, bool ownsHandle = false)
    {
        _handle = handle;
        _surfaceHandle = surfaceHandle;
        _ownsHandle = ownsHandle;
    }

    /// <summary>Sets the focus state of the inspector.</summary>
    public void SetFocus(bool focused)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        GhosttyNative.InspectorSetFocus(_handle, (byte)(focused ? 1 : 0));
    }

    /// <summary>Sets the content scale (DPI) of the inspector.</summary>
    public void SetContentScale(double x, double y)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        GhosttyNative.InspectorSetContentScale(_handle, x, y);
    }

    /// <summary>Sets the inspector viewport size in pixels.</summary>
    public void SetSize(uint width, uint height)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        GhosttyNative.InspectorSetSize(_handle, width, height);
    }

    /// <summary>Sends a mouse button event to the inspector.</summary>
    public void SendMouseButton(GhosttyMouseState state, GhosttyMouseButton button, GhosttyMods mods)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        GhosttyNative.InspectorMouseButton(_handle, state, button, mods);
    }

    /// <summary>Sends a mouse position update to the inspector.</summary>
    public void SendMousePos(double x, double y)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        GhosttyNative.InspectorMousePos(_handle, x, y);
    }

    /// <summary>Sends a mouse scroll event to the inspector.</summary>
    public void SendMouseScroll(double x, double y, int scrollMods = 0)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        GhosttyNative.InspectorMouseScroll(_handle, x, y, scrollMods);
    }

    /// <summary>Sends a key event to the inspector.</summary>
    public void SendKey(GhosttyInputAction action, GhosttyKey key, GhosttyMods mods)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        GhosttyNative.InspectorKey(_handle, action, key, mods);
    }

    /// <summary>Sends text input to the inspector.</summary>
    public unsafe void SendText(string text)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var bytes = System.Text.Encoding.UTF8.GetBytes(text + '\0');
        fixed (byte* ptr = bytes)
        {
            GhosttyNative.InspectorText(_handle, ptr);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_handle != nint.Zero)
        {
            if (_ownsHandle)
            {
                GhosttyNative.InspectorFree(_surfaceHandle);
            }
            _handle = nint.Zero;
        }
    }
}
