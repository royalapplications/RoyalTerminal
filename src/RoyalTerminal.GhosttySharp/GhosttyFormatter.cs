// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.GhosttySharp.Native;

namespace RoyalTerminal.GhosttySharp;

/// <summary>
/// Managed lifetime wrapper for the official <c>GhosttyFormatter</c> libghostty-vt API.
/// </summary>
public sealed class GhosttyFormatter : IDisposable
{
    private nint _handle;
    private bool _disposed;

    /// <summary>
    /// Creates a formatter bound to the given terminal.
    /// </summary>
    public GhosttyFormatter(
        GhosttyTerminal terminal,
        GhosttyVtNative.GhosttyFormatterFormat format = GhosttyVtNative.GhosttyFormatterFormat.Plain,
        bool unwrap = false,
        bool trim = false,
        GhosttySelection? selection = null)
    {
        ArgumentNullException.ThrowIfNull(terminal);
        NativeLibraryLoader.Initialize();

        GhosttyVtNative.GhosttyFormatterTerminalOptions options =
            CreateDefaultOptions(format, unwrap, trim);

        unsafe
        {
            if (selection is GhosttySelection selected)
            {
                GhosttyVtNative.GhosttySelectionRange nativeSelection = selected.ToNative();
                options.Selection = &nativeSelection;
                Initialize(terminal, options);
                return;
            }
        }

        Initialize(terminal, options);
    }

    /// <summary>
    /// Creates a formatter bound to the given terminal with explicit native options.
    /// </summary>
    public GhosttyFormatter(
        GhosttyTerminal terminal,
        GhosttyVtNative.GhosttyFormatterTerminalOptions options)
    {
        ArgumentNullException.ThrowIfNull(terminal);
        NativeLibraryLoader.Initialize();
        Initialize(terminal, options);
    }

    /// <summary>Returns true when the native formatter handle is valid.</summary>
    public bool IsValid => _handle != nint.Zero && !_disposed;

    /// <summary>Formats the current terminal snapshot to a UTF-8 byte buffer.</summary>
    public unsafe byte[] Format()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        GhosttyVtNative.GhosttyResult probe = GhosttyVtNative.FormatterFormatBuffer(_handle, null, 0, out nuint needed);
        if (probe != GhosttyVtNative.GhosttyResult.OutOfSpace)
        {
            ThrowIfFailed(probe, "ghostty_formatter_format_buf(probe)");
        }

        if (needed == 0)
        {
            return [];
        }

        byte[] data = new byte[checked((int)needed)];
        fixed (byte* dataPtr = data)
        {
            ThrowIfFailed(
                GhosttyVtNative.FormatterFormatBuffer(_handle, dataPtr, (nuint)data.Length, out nuint written),
                "ghostty_formatter_format_buf");

            if (written == (nuint)data.Length)
            {
                return data;
            }

            byte[] resized = new byte[checked((int)written)];
            Array.Copy(data, resized, resized.Length);
            return resized;
        }
    }

    /// <summary>Formats the current terminal snapshot as a UTF-8 string.</summary>
    public string FormatToString()
    {
        byte[] data = Format();
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
            GhosttyVtNative.FormatterFree(_handle);
            _handle = nint.Zero;
        }
    }

    private void Initialize(
        GhosttyTerminal terminal,
        GhosttyVtNative.GhosttyFormatterTerminalOptions options)
    {
        ThrowIfFailed(
            GhosttyVtNative.FormatterTerminalNew(nint.Zero, out _handle, terminal.Handle, options),
            "ghostty_formatter_terminal_new");
    }

    private static GhosttyVtNative.GhosttyFormatterTerminalOptions CreateDefaultOptions(
        GhosttyVtNative.GhosttyFormatterFormat format,
        bool unwrap,
        bool trim)
    {
        GhosttyVtNative.GhosttyFormatterTerminalOptions options =
            GhosttyVtNative.GhosttyFormatterTerminalOptions.CreateSized();
        options.Emit = format;
        options.Unwrap = unwrap;
        options.Trim = trim;
        return options;
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
