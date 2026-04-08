// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.GhosttySharp.Native;

namespace RoyalTerminal.GhosttySharp;

/// <summary>
/// Managed lifetime wrapper for the official <c>GhosttyTerminal</c> libghostty-vt API.
/// </summary>
public sealed class GhosttyTerminal : IDisposable
{
    private nint _handle;
    private bool _disposed;
    private readonly bool _ownsHandle;

    /// <summary>
    /// Creates a new Ghostty VT terminal.
    /// </summary>
    public GhosttyTerminal(ushort columns, ushort rows, nuint maxScrollback = 10_000)
    {
        NativeLibraryLoader.Initialize();

        GhosttyVtNative.GhosttyTerminalOptions options = new()
        {
            Cols = columns,
            Rows = rows,
            MaxScrollback = maxScrollback,
        };

        ThrowIfFailed(
            GhosttyVtNative.TerminalNew(nint.Zero, out _handle, options),
            "ghostty_terminal_new");

        _ownsHandle = true;
    }

    internal GhosttyTerminal(nint handle, bool ownsHandle = false)
    {
        _handle = handle;
        _ownsHandle = ownsHandle;
    }

    /// <summary>Returns true when the native terminal handle is valid.</summary>
    public bool IsValid => _handle != nint.Zero && !_disposed;

    internal nint Handle
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _handle;
        }
    }

    /// <summary>Writes VT bytes into the terminal parser.</summary>
    public unsafe void Write(ReadOnlySpan<byte> data)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (data.IsEmpty)
        {
            return;
        }

        fixed (byte* dataPtr = data)
        {
            GhosttyVtNative.TerminalVtWrite(_handle, dataPtr, (nuint)data.Length);
        }
    }

    /// <summary>Resets the terminal to its initial state.</summary>
    public void Reset()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        GhosttyVtNative.TerminalReset(_handle);
    }

    /// <summary>Resizes the terminal grid and associated cell size metadata.</summary>
    public void Resize(ushort columns, ushort rows, uint cellWidthPx = 0, uint cellHeightPx = 0)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ThrowIfFailed(
            GhosttyVtNative.TerminalResize(_handle, columns, rows, cellWidthPx, cellHeightPx),
            "ghostty_terminal_resize");
    }

    /// <summary>Scrolls the terminal viewport.</summary>
    public void ScrollViewport(GhosttyVtNative.GhosttyTerminalScrollViewport behavior)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        GhosttyVtNative.TerminalScrollViewport(_handle, behavior);
    }

    /// <summary>Gets the current value of a VT mode.</summary>
    public bool GetMode(GhosttyVtNative.GhosttyMode mode)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ThrowIfFailed(
            GhosttyVtNative.TerminalModeGet(_handle, mode, out bool value),
            "ghostty_terminal_mode_get");
        return value;
    }

    /// <summary>Sets the current value of a VT mode.</summary>
    public void SetMode(GhosttyVtNative.GhosttyMode mode, bool value)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ThrowIfFailed(
            GhosttyVtNative.TerminalModeSet(_handle, mode, value),
            "ghostty_terminal_mode_set");
    }

    /// <summary>Sets the shared callback userdata pointer.</summary>
    public unsafe void SetUserdata(nint userdata)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ThrowIfFailed(
            GhosttyVtNative.TerminalSet(_handle, GhosttyVtNative.GhosttyTerminalOption.Userdata, (void*)userdata),
            "ghostty_terminal_set(userdata)");
    }

    /// <summary>Configures the write-pty callback.</summary>
    public unsafe void SetWritePtyCallback(nint callback)
    {
        SetPointerOption(GhosttyVtNative.GhosttyTerminalOption.WritePty, callback);
    }

    /// <summary>Configures the bell callback.</summary>
    public unsafe void SetBellCallback(nint callback)
    {
        SetPointerOption(GhosttyVtNative.GhosttyTerminalOption.Bell, callback);
    }

    /// <summary>Configures the title-changed callback.</summary>
    public unsafe void SetTitleChangedCallback(nint callback)
    {
        SetPointerOption(GhosttyVtNative.GhosttyTerminalOption.TitleChanged, callback);
    }

    /// <summary>Configures the enquiry callback.</summary>
    public unsafe void SetEnquiryCallback(nint callback)
    {
        SetPointerOption(GhosttyVtNative.GhosttyTerminalOption.Enquiry, callback);
    }

    /// <summary>Configures the XTVERSION callback.</summary>
    public unsafe void SetXtversionCallback(nint callback)
    {
        SetPointerOption(GhosttyVtNative.GhosttyTerminalOption.Xtversion, callback);
    }

    /// <summary>Configures the terminal-size callback.</summary>
    public unsafe void SetSizeCallback(nint callback)
    {
        SetPointerOption(GhosttyVtNative.GhosttyTerminalOption.Size, callback);
    }

    /// <summary>Configures the color-scheme callback.</summary>
    public unsafe void SetColorSchemeCallback(nint callback)
    {
        SetPointerOption(GhosttyVtNative.GhosttyTerminalOption.ColorScheme, callback);
    }

    /// <summary>Configures the device-attributes callback.</summary>
    public unsafe void SetDeviceAttributesCallback(nint callback)
    {
        SetPointerOption(GhosttyVtNative.GhosttyTerminalOption.DeviceAttributes, callback);
    }

    /// <summary>Clears a configurable terminal option.</summary>
    public unsafe void ClearOption(GhosttyVtNative.GhosttyTerminalOption option)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ThrowIfFailed(GhosttyVtNative.TerminalSet(_handle, option, null), $"ghostty_terminal_set({option})");
    }

    /// <summary>Sets the default foreground color.</summary>
    public unsafe void SetForegroundColor(GhosttyVtNative.GhosttyColorRgb color)
    {
        SetStructOption(GhosttyVtNative.GhosttyTerminalOption.ColorForeground, color, "ghostty_terminal_set(color_foreground)");
    }

    /// <summary>Sets the default background color.</summary>
    public unsafe void SetBackgroundColor(GhosttyVtNative.GhosttyColorRgb color)
    {
        SetStructOption(GhosttyVtNative.GhosttyTerminalOption.ColorBackground, color, "ghostty_terminal_set(color_background)");
    }

    /// <summary>Sets the default cursor color.</summary>
    public unsafe void SetCursorColor(GhosttyVtNative.GhosttyColorRgb color)
    {
        SetStructOption(GhosttyVtNative.GhosttyTerminalOption.ColorCursor, color, "ghostty_terminal_set(color_cursor)");
    }

    /// <summary>Sets the default 256-color palette.</summary>
    public unsafe void SetPalette(ReadOnlySpan<GhosttyVtNative.GhosttyColorRgb> palette)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (palette.Length != 256)
        {
            throw new ArgumentException("Palette must contain exactly 256 colors.", nameof(palette));
        }

        fixed (GhosttyVtNative.GhosttyColorRgb* palettePtr = palette)
        {
            ThrowIfFailed(
                GhosttyVtNative.TerminalSet(_handle, GhosttyVtNative.GhosttyTerminalOption.ColorPalette, palettePtr),
                "ghostty_terminal_set(color_palette)");
        }
    }

    /// <summary>Sets the Kitty image storage limit in bytes for the terminal.</summary>
    public void SetKittyImageStorageLimit(ulong bytes)
    {
        SetStructOption(
            GhosttyVtNative.GhosttyTerminalOption.KittyImageStorageLimit,
            bytes,
            "ghostty_terminal_set(kitty_image_storage_limit)");
    }

    /// <summary>Enables or disables the Kitty file medium.</summary>
    public void SetKittyImageMediumFile(bool enabled)
    {
        SetStructOption(
            GhosttyVtNative.GhosttyTerminalOption.KittyImageMediumFile,
            enabled,
            "ghostty_terminal_set(kitty_image_medium_file)");
    }

    /// <summary>Enables or disables the Kitty temporary-file medium.</summary>
    public void SetKittyImageMediumTempFile(bool enabled)
    {
        SetStructOption(
            GhosttyVtNative.GhosttyTerminalOption.KittyImageMediumTempFile,
            enabled,
            "ghostty_terminal_set(kitty_image_medium_temp_file)");
    }

    /// <summary>Enables or disables the Kitty shared-memory medium.</summary>
    public void SetKittyImageMediumSharedMemory(bool enabled)
    {
        SetStructOption(
            GhosttyVtNative.GhosttyTerminalOption.KittyImageMediumSharedMemory,
            enabled,
            "ghostty_terminal_set(kitty_image_medium_shared_mem)");
    }

    /// <summary>Gets the terminal width in cells.</summary>
    public ushort GetColumns() => GetValue<ushort>(GhosttyVtNative.GhosttyTerminalData.Cols);

    /// <summary>Gets the terminal height in cells.</summary>
    public ushort GetRows() => GetValue<ushort>(GhosttyVtNative.GhosttyTerminalData.Rows);

    /// <summary>Gets the cursor column in active-screen coordinates.</summary>
    public ushort GetCursorX() => GetValue<ushort>(GhosttyVtNative.GhosttyTerminalData.CursorX);

    /// <summary>Gets the cursor row in active-screen coordinates.</summary>
    public ushort GetCursorY() => GetValue<ushort>(GhosttyVtNative.GhosttyTerminalData.CursorY);

    /// <summary>Gets the active screen buffer.</summary>
    public GhosttyVtNative.GhosttyTerminalScreen GetActiveScreen()
        => GetValue<GhosttyVtNative.GhosttyTerminalScreen>(GhosttyVtNative.GhosttyTerminalData.ActiveScreen);

    /// <summary>Gets whether the cursor is visible.</summary>
    public bool GetCursorVisible() => GetValue<bool>(GhosttyVtNative.GhosttyTerminalData.CursorVisible);

    /// <summary>Gets the kitty keyboard protocol flags.</summary>
    public GhosttyVtNative.GhosttyKittyKeyFlags GetKittyKeyboardFlags()
        => GetValue<GhosttyVtNative.GhosttyKittyKeyFlags>(GhosttyVtNative.GhosttyTerminalData.KittyKeyboardFlags);

    /// <summary>Gets whether any mouse tracking mode is active.</summary>
    public bool GetMouseTracking() => GetValue<bool>(GhosttyVtNative.GhosttyTerminalData.MouseTracking);

    /// <summary>Gets the total terminal width in pixels.</summary>
    public uint GetWidthPx() => GetValue<uint>(GhosttyVtNative.GhosttyTerminalData.WidthPx);

    /// <summary>Gets the total terminal height in pixels.</summary>
    public uint GetHeightPx() => GetValue<uint>(GhosttyVtNative.GhosttyTerminalData.HeightPx);

    /// <summary>Gets the current terminal title.</summary>
    public string GetTitle() => GetBorrowedString(GhosttyVtNative.GhosttyTerminalData.Title);

    /// <summary>Gets the current terminal working directory.</summary>
    public string GetWorkingDirectory() => GetBorrowedString(GhosttyVtNative.GhosttyTerminalData.Pwd);

    /// <summary>Gets the current effective terminal scrollbar state.</summary>
    public GhosttyVtNative.GhosttyTerminalScrollbar GetScrollbar()
        => GetValue<GhosttyVtNative.GhosttyTerminalScrollbar>(GhosttyVtNative.GhosttyTerminalData.Scrollbar);

    /// <summary>Gets the Kitty image storage limit when Kitty Graphics is available.</summary>
    public bool TryGetKittyImageStorageLimit(out ulong bytes)
        => TryGetValue(GhosttyVtNative.GhosttyTerminalData.KittyImageStorageLimit, out bytes);

    /// <summary>Gets whether the Kitty file medium is enabled.</summary>
    public bool TryGetKittyImageMediumFile(out bool enabled)
        => TryGetValue(GhosttyVtNative.GhosttyTerminalData.KittyImageMediumFile, out enabled);

    /// <summary>Gets whether the Kitty temporary-file medium is enabled.</summary>
    public bool TryGetKittyImageMediumTempFile(out bool enabled)
        => TryGetValue(GhosttyVtNative.GhosttyTerminalData.KittyImageMediumTempFile, out enabled);

    /// <summary>Gets whether the Kitty shared-memory medium is enabled.</summary>
    public bool TryGetKittyImageMediumSharedMemory(out bool enabled)
        => TryGetValue(GhosttyVtNative.GhosttyTerminalData.KittyImageMediumSharedMemory, out enabled);

    /// <summary>Gets the current active-screen Kitty Graphics storage handle.</summary>
    public unsafe bool TryGetKittyGraphics(out GhosttyKittyGraphics? graphics)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        nint handle = nint.Zero;
        GhosttyVtNative.GhosttyResult result =
            GhosttyVtNative.TerminalGet(_handle, GhosttyVtNative.GhosttyTerminalData.KittyGraphics, &handle);
        if (result == GhosttyVtNative.GhosttyResult.NoValue || handle == nint.Zero)
        {
            graphics = null;
            return false;
        }

        ThrowIfFailed(result, "ghostty_terminal_get(kitty_graphics)");
        graphics = new GhosttyKittyGraphics(this, handle);
        return true;
    }

    /// <summary>Gets the effective terminal foreground color when available.</summary>
    public bool TryGetForegroundColor(out GhosttyVtNative.GhosttyColorRgb color)
        => TryGetColor(GhosttyVtNative.GhosttyTerminalData.ColorForeground, out color);

    /// <summary>Gets the effective terminal background color when available.</summary>
    public bool TryGetBackgroundColor(out GhosttyVtNative.GhosttyColorRgb color)
        => TryGetColor(GhosttyVtNative.GhosttyTerminalData.ColorBackground, out color);

    /// <summary>Gets the effective terminal cursor color when available.</summary>
    public bool TryGetCursorColor(out GhosttyVtNative.GhosttyColorRgb color)
        => TryGetColor(GhosttyVtNative.GhosttyTerminalData.ColorCursor, out color);

    /// <summary>Copies the current terminal palette into the provided span.</summary>
    public unsafe void GetPalette(Span<GhosttyVtNative.GhosttyColorRgb> destination)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (destination.Length < 256)
        {
            throw new ArgumentException("Destination span must contain at least 256 colors.", nameof(destination));
        }

        fixed (GhosttyVtNative.GhosttyColorRgb* destinationPtr = destination)
        {
            ThrowIfFailed(
                GhosttyVtNative.TerminalGet(_handle, GhosttyVtNative.GhosttyTerminalData.ColorPalette, destinationPtr),
                "ghostty_terminal_get(color_palette)");
        }
    }

    /// <summary>Attempts to resolve a point to a grid reference.</summary>
    public bool TryGetGridReference(GhosttyVtNative.GhosttyPoint point, out GhosttyVtNative.GhosttyGridRef reference)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        reference = GhosttyVtNative.GhosttyGridRef.CreateSized();
        GhosttyVtNative.GhosttyResult result = GhosttyVtNative.TerminalGridRef(_handle, point, ref reference);
        if (result == GhosttyVtNative.GhosttyResult.InvalidValue)
        {
            reference = default;
            return false;
        }

        ThrowIfFailed(result, "ghostty_terminal_grid_ref");
        return true;
    }

    /// <summary>Reads the hyperlink URI attached to a resolved grid reference.</summary>
    public unsafe string? GetHyperlinkUri(in GhosttyVtNative.GhosttyGridRef reference)
    {
        GhosttyVtNative.GhosttyResult probe = GhosttyVtNative.GridRefHyperlinkUri(in reference, null, 0, out nuint needed);
        if (probe == GhosttyVtNative.GhosttyResult.Success && needed == 0)
        {
            return null;
        }

        if (probe != GhosttyVtNative.GhosttyResult.OutOfSpace)
        {
            ThrowIfFailed(probe, "ghostty_grid_ref_hyperlink_uri(probe)");
        }

        if (needed == 0)
        {
            return null;
        }

        byte[] buffer = new byte[checked((int)needed)];
        fixed (byte* bufferPtr = buffer)
        {
            ThrowIfFailed(
                GhosttyVtNative.GridRefHyperlinkUri(in reference, bufferPtr, (nuint)buffer.Length, out nuint written),
                "ghostty_grid_ref_hyperlink_uri");
            return written == 0 ? null : System.Text.Encoding.UTF8.GetString(buffer, 0, checked((int)written));
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
        if (_ownsHandle && _handle != nint.Zero)
        {
            GhosttyVtNative.TerminalFree(_handle);
        }

        _handle = nint.Zero;
    }

    private unsafe string GetBorrowedString(GhosttyVtNative.GhosttyTerminalData data)
    {
        GhosttyVtNative.GhosttyString value = GetValue<GhosttyVtNative.GhosttyString>(data);
        return value.ToUtf8String();
    }

    private unsafe bool TryGetColor(
        GhosttyVtNative.GhosttyTerminalData data,
        out GhosttyVtNative.GhosttyColorRgb color)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        GhosttyVtNative.GhosttyColorRgb value = default;
        GhosttyVtNative.GhosttyResult result = GhosttyVtNative.TerminalGet(_handle, data, &value);
        if (result == GhosttyVtNative.GhosttyResult.NoValue)
        {
            color = default;
            return false;
        }

        ThrowIfFailed(result, $"ghostty_terminal_get({data})");
        color = value;
        return true;
    }

    private unsafe bool TryGetValue<T>(GhosttyVtNative.GhosttyTerminalData data, out T value) where T : unmanaged
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        T copy = default;
        GhosttyVtNative.GhosttyResult result = GhosttyVtNative.TerminalGet(_handle, data, &copy);
        if (result == GhosttyVtNative.GhosttyResult.NoValue)
        {
            value = default;
            return false;
        }

        ThrowIfFailed(result, $"ghostty_terminal_get({data})");
        value = copy;
        return true;
    }

    private unsafe void SetPointerOption(GhosttyVtNative.GhosttyTerminalOption option, nint value)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ThrowIfFailed(
            GhosttyVtNative.TerminalSet(_handle, option, (void*)value),
            $"ghostty_terminal_set({option})");
    }

    private unsafe void SetStructOption<T>(
        GhosttyVtNative.GhosttyTerminalOption option,
        T value,
        string operation) where T : unmanaged
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        T copy = value;
        ThrowIfFailed(GhosttyVtNative.TerminalSet(_handle, option, &copy), operation);
    }

    private unsafe T GetValue<T>(GhosttyVtNative.GhosttyTerminalData data) where T : unmanaged
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        T value = default;
        GhosttyVtNative.GhosttyResult result = GhosttyVtNative.TerminalGet(_handle, data, &value);
        ThrowIfFailed(result, $"ghostty_terminal_get({data})");
        return value;
    }

    private static void ThrowIfFailed(GhosttyVtNative.GhosttyResult result, string operation)
    {
        if (result == GhosttyVtNative.GhosttyResult.Success)
        {
            return;
        }

        throw new InvalidOperationException($"{operation} failed with {result}.");
    }

    internal static GhosttyVtNative.GhosttyColorRgb ToNativeColor(uint argb)
    {
        return new GhosttyVtNative.GhosttyColorRgb
        {
            R = (byte)((argb >> 16) & 0xFF),
            G = (byte)((argb >> 8) & 0xFF),
            B = (byte)(argb & 0xFF),
        };
    }

    internal static uint ToArgb(GhosttyVtNative.GhosttyColorRgb color)
    {
        return 0xFF000000u | ((uint)color.R << 16) | ((uint)color.G << 8) | color.B;
    }
}
