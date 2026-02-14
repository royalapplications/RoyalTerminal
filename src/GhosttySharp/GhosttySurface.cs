// Licensed under the MIT License.
// GhosttySharp - .NET bindings for the Ghostty terminal emulator.

using System.Runtime.InteropServices;
using System.Text;
using GhosttySharp.Native;

namespace GhosttySharp;

/// <summary>
/// Managed wrapper for a Ghostty terminal surface.
/// Provides high-level methods for input forwarding, text reading, and split management.
/// Uses Span-based APIs for zero-alloc text transfer where possible.
/// </summary>
public sealed class GhosttySurface : IDisposable
{
    private nint _handle;
    private bool _disposed;
    private readonly bool _ownsHandle;

    /// <summary>Gets the native surface handle.</summary>
    public nint Handle
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _handle;
        }
    }

    /// <summary>Returns true if the surface handle is valid.</summary>
    public bool IsValid => _handle != nint.Zero && !_disposed;

    /// <summary>Gets the surface size information.</summary>
    public GhosttySurfaceSize Size
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return GhosttyNative.SurfaceSize(_handle);
        }
    }

    /// <summary>Gets whether the mouse is currently captured by the terminal application.</summary>
    public bool IsMouseCaptured
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return GhosttyNative.SurfaceMouseCaptured(_handle) != 0;
        }
    }

    /// <summary>Gets whether the terminal has an active text selection.</summary>
    public bool HasSelection
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return GhosttyNative.SurfaceHasSelection(_handle) != 0;
        }
    }

    /// <summary>Gets whether the terminal process has exited.</summary>
    public bool ProcessExited
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return GhosttyNative.SurfaceProcessExited(_handle) != 0;
        }
    }

    /// <summary>Gets whether quit confirmation is needed.</summary>
    public bool NeedsConfirmQuit
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return GhosttyNative.SurfaceNeedsConfirmQuit(_handle) != 0;
        }
    }

    /// <summary>Creates a new surface within the given app.</summary>
    public unsafe GhosttySurface(GhosttyApp app)
    {
        var config = GhosttyNative.SurfaceConfigNew();
        _handle = GhosttyNative.SurfaceNew(app.Handle, &config);
        if (_handle == nint.Zero)
            throw new InvalidOperationException("Failed to create Ghostty surface.");
        _ownsHandle = true;
    }

    /// <summary>Creates a new surface with custom configuration.</summary>
    public unsafe GhosttySurface(GhosttyApp app, ref GhosttySurfaceConfig config)
    {
        fixed (GhosttySurfaceConfig* configPtr = &config)
        {
            _handle = GhosttyNative.SurfaceNew(app.Handle, configPtr);
        }
        if (_handle == nint.Zero)
            throw new InvalidOperationException("Failed to create Ghostty surface.");
        _ownsHandle = true;
    }

    /// <summary>Wraps an existing native surface handle.</summary>
    internal GhosttySurface(nint handle, bool ownsHandle = false)
    {
        _handle = handle;
        _ownsHandle = ownsHandle;
    }

    /// <summary>Refreshes the surface rendering.</summary>
    public void Refresh()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        GhosttyNative.SurfaceRefresh(_handle);
    }

    /// <summary>Triggers a draw of the surface.</summary>
    public void Draw()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        GhosttyNative.SurfaceDraw(_handle);
    }

    /// <summary>Sets the content scale (DPI scaling).</summary>
    public void SetContentScale(double x, double y)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        GhosttyNative.SurfaceSetContentScale(_handle, x, y);
    }

    /// <summary>Sets the focus state of the surface.</summary>
    public void SetFocus(bool focused)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        GhosttyNative.SurfaceSetFocus(_handle, (byte)(focused ? 1 : 0));
    }

    /// <summary>Sets the occlusion state of the surface.</summary>
    public void SetOcclusion(bool occluded)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        GhosttyNative.SurfaceSetOcclusion(_handle, (byte)(occluded ? 1 : 0));
    }

    /// <summary>Sets the surface size in pixels.</summary>
    public void SetSize(uint width, uint height)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        GhosttyNative.SurfaceSetSize(_handle, width, height);
    }

    /// <summary>Sets the color scheme.</summary>
    public void SetColorScheme(GhosttyColorScheme scheme)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        GhosttyNative.SurfaceSetColorScheme(_handle, scheme);
    }

    /// <summary>Gets translated keyboard modifiers for the surface.</summary>
    public GhosttyMods GetKeyTranslationMods(GhosttyMods mods)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return GhosttyNative.SurfaceKeyTranslationMods(_handle, mods);
    }

    /// <summary>Sends a key event to the surface.</summary>
    public bool SendKey(GhosttyInputKey key)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return GhosttyNative.SurfaceKey(_handle, key) != 0;
    }

    /// <summary>Checks if a key event would trigger a binding.</summary>
    public unsafe bool IsKeyBinding(GhosttyInputKey key, out GhosttyBindingFlags flags)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        fixed (GhosttyBindingFlags* flagsPtr = &flags)
        {
            return GhosttyNative.SurfaceKeyIsBinding(_handle, key, flagsPtr) != 0;
        }
    }

    /// <summary>Sends UTF-8 text to the surface. Uses Span for zero-copy.</summary>
    public unsafe void SendText(ReadOnlySpan<byte> utf8Text)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        fixed (byte* ptr = utf8Text)
        {
            GhosttyNative.SurfaceText(_handle, ptr, (nuint)utf8Text.Length);
        }
    }

    /// <summary>Sends text string to the surface.</summary>
    public unsafe void SendText(string text)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var bytes = Encoding.UTF8.GetBytes(text);
        fixed (byte* ptr = bytes)
        {
            GhosttyNative.SurfaceText(_handle, ptr, (nuint)bytes.Length);
        }
    }

    /// <summary>Sends preedit (IME composition) text to the surface.</summary>
    public unsafe void SendPreedit(ReadOnlySpan<byte> utf8Text)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        fixed (byte* ptr = utf8Text)
        {
            GhosttyNative.SurfacePreedit(_handle, ptr, (nuint)utf8Text.Length);
        }
    }

    /// <summary>Sends a mouse button event.</summary>
    public bool SendMouseButton(GhosttyMouseState state, GhosttyMouseButton button, GhosttyMods mods)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return GhosttyNative.SurfaceMouseButton(_handle, state, button, mods) != 0;
    }

    /// <summary>Sends mouse position update.</summary>
    public void SendMousePos(double x, double y, GhosttyMods mods)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        GhosttyNative.SurfaceMousePos(_handle, x, y, mods);
    }

    /// <summary>Sends mouse scroll event.</summary>
    public void SendMouseScroll(double x, double y, int scrollMods = 0)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        GhosttyNative.SurfaceMouseScroll(_handle, x, y, scrollMods);
    }

    /// <summary>Sends mouse pressure (force touch) event.</summary>
    public void SendMousePressure(uint stage, double pressure)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        GhosttyNative.SurfaceMousePressure(_handle, stage, pressure);
    }

    /// <summary>Gets the IME cursor position and size.</summary>
    public unsafe (double X, double Y, double Width, double Height) GetImePoint()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        double x, y, w, h;
        GhosttyNative.SurfaceImePoint(_handle, &x, &y, &w, &h);
        return (x, y, w, h);
    }

    /// <summary>Requests the surface to close.</summary>
    public void RequestClose()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        GhosttyNative.SurfaceRequestClose(_handle);
    }

    /// <summary>Creates a new split in the given direction.</summary>
    public void Split(GhosttySplitDirection direction)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        GhosttyNative.SurfaceSplit(_handle, direction);
    }

    /// <summary>Focuses a neighboring split.</summary>
    public void FocusSplit(GhosttyGotoSplit direction)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        GhosttyNative.SurfaceSplitFocus(_handle, direction);
    }

    /// <summary>Resizes a split in the given direction.</summary>
    public void ResizeSplit(GhosttyResizeSplitDirection direction, ushort amount)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        GhosttyNative.SurfaceSplitResize(_handle, direction, amount);
    }

    /// <summary>Equalizes all split sizes.</summary>
    public void EqualizeSplits()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        GhosttyNative.SurfaceSplitEqualize(_handle);
    }

    /// <summary>Executes a binding action by name.</summary>
    public unsafe bool ExecuteBindingAction(string action)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var bytes = Encoding.UTF8.GetBytes(action);
        fixed (byte* ptr = bytes)
        {
            return GhosttyNative.SurfaceBindingAction(_handle, ptr, (nuint)bytes.Length) != 0;
        }
    }

    /// <summary>Completes a clipboard read request.</summary>
    public unsafe void CompleteClipboardRequest(string? data, nint state, bool confirmed)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (data is null)
        {
            GhosttyNative.SurfaceCompleteClipboardRequest(_handle, null, state, (byte)(confirmed ? 1 : 0));
        }
        else
        {
            var bytes = Encoding.UTF8.GetBytes(data + '\0');
            fixed (byte* ptr = bytes)
            {
                GhosttyNative.SurfaceCompleteClipboardRequest(_handle, ptr, state, (byte)(confirmed ? 1 : 0));
            }
        }
    }

    /// <summary>Reads the current selection text. Returns null if no selection.</summary>
    public unsafe string? ReadSelection()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        GhosttyText text = default;
        if (GhosttyNative.SurfaceReadSelection(_handle, &text) == 0)
            return null;

        try
        {
            if (text.TextPtr == null || text.TextLen == 0)
                return null;
            return Encoding.UTF8.GetString(new ReadOnlySpan<byte>(text.TextPtr, checked((int)text.TextLen)));
        }
        finally
        {
            GhosttyNative.SurfaceFreeText(_handle, &text);
        }
    }

    /// <summary>Reads text from the terminal at the given selection range. Returns null if failed.</summary>
    public unsafe string? ReadText(GhosttySelection selection)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        GhosttyText text = default;
        if (GhosttyNative.SurfaceReadText(_handle, selection, &text) == 0)
            return null;

        try
        {
            if (text.TextPtr == null || text.TextLen == 0)
                return null;
            return Encoding.UTF8.GetString(new ReadOnlySpan<byte>(text.TextPtr, checked((int)text.TextLen)));
        }
        finally
        {
            GhosttyNative.SurfaceFreeText(_handle, &text);
        }
    }

    /// <summary>
    /// Reads selection text as UTF-8 bytes using copy-on-read semantics.
    /// The returned span points to managed memory and is safe to use after this method returns.
    /// </summary>
    public bool TryReadSelectionUtf8(out ReadOnlySpan<byte> utf8Text)
    {
        if (TryReadSelectionUtf8Copy(out byte[]? utf8Bytes))
        {
            utf8Text = utf8Bytes;
            return true;
        }

        utf8Text = default;
        return false;
    }

    /// <summary>
    /// Reads selection text as a copied UTF-8 byte array.
    /// Native memory is always released before returning.
    /// </summary>
    public unsafe bool TryReadSelectionUtf8Copy(out byte[]? utf8Text)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        utf8Text = null;

        GhosttyText text = default;
        if (GhosttyNative.SurfaceReadSelection(_handle, &text) == 0)
            return false;

        try
        {
            if (text.TextPtr == null || text.TextLen == 0)
                return false;

            int length = checked((int)text.TextLen);
            utf8Text = new byte[length];
            new ReadOnlySpan<byte>(text.TextPtr, length).CopyTo(utf8Text);
            return true;
        }
        finally
        {
            GhosttyNative.SurfaceFreeText(_handle, &text);
        }
    }

    /// <summary>Updates the surface configuration.</summary>
    public void UpdateConfig(GhosttyConfig config)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        GhosttyNative.SurfaceUpdateConfig(_handle, config.Handle);
    }

    /// <summary>Gets the inherited config for new surfaces from this one.</summary>
    public GhosttySurfaceConfig GetInheritedConfig(GhosttySurfaceContext context)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return GhosttyNative.SurfaceInheritedConfig(_handle, context);
    }

    // -------------------------------------------------------------------
    // Screen State Reading (Custom Rendering)
    // -------------------------------------------------------------------

    /// <summary>
    /// Locks the terminal screen state for reading cell data.
    /// Must be paired with ScreenUnlock(). While locked, you can call
    /// GetCursorInfo() and GetRowCells() safely.
    /// </summary>
    public void ScreenLock()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        GhosttyNative.SurfaceScreenLock(_handle);
    }

    /// <summary>
    /// Unlocks the terminal screen state after reading.
    /// </summary>
    public void ScreenUnlock()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        GhosttyNative.SurfaceScreenUnlock(_handle);
    }

    /// <summary>
    /// Gets cursor position and style. Must be called while screen is locked.
    /// </summary>
    public unsafe GhosttyCursorInfo GetCursorInfo()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        GhosttyCursorInfo info;
        GhosttyNative.SurfaceCursorInfo(_handle, &info);
        return info;
    }

    /// <summary>
    /// Gets resolved cell data for a viewport row. Must be called while screen is locked.
    /// Returns the number of cells actually written to the buffer.
    /// </summary>
    public unsafe uint GetRowCells(uint row, Span<GhosttyCellInfo> cells)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        fixed (GhosttyCellInfo* ptr = cells)
        {
            return GhosttyNative.SurfaceGetRowCells(_handle, row, ptr, (uint)cells.Length);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_handle != nint.Zero && _ownsHandle)
        {
            GhosttyNative.SurfaceFree(_handle);
            _handle = nint.Zero;
        }
    }
}
