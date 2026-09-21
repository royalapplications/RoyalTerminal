// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using RoyalTerminal.GhosttySharp.Native;

namespace RoyalTerminal.GhosttySharp;

/// <summary>
/// Screen-level formatter extras for styled Ghostty exports.
/// </summary>
public readonly record struct GhosttyFormatterScreenOptions(
    bool IncludeCursor = false,
    bool IncludeStyle = false,
    bool IncludeHyperlinks = false,
    bool IncludeProtection = false,
    bool IncludeKittyKeyboard = false,
    bool IncludeCharsets = false);

/// <summary>
/// Terminal-level formatter extras for styled Ghostty exports.
/// </summary>
public readonly record struct GhosttyFormatterExtraOptions(
    bool IncludePalette = false,
    bool IncludeModes = false,
    bool IncludeScrollingRegion = false,
    bool IncludeTabstops = false,
    bool IncludeWorkingDirectory = false,
    bool IncludeKeyboardModes = false,
    GhosttyFormatterScreenOptions Screen = default);

/// <summary>
/// Managed formatter options that map to <c>GhosttyFormatterTerminalOptions</c>.
/// </summary>
public readonly record struct GhosttyFormatterOptions(
    GhosttyVtNative.GhosttyFormatterFormat Format = GhosttyVtNative.GhosttyFormatterFormat.Plain,
    bool Unwrap = false,
    bool Trim = false,
    GhosttyFormatterExtraOptions Extra = default,
    GhosttySelection? Selection = null);

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
        : this(
            terminal,
            new GhosttyFormatterOptions(
                Format: format,
                Unwrap: unwrap,
                Trim: trim,
                Selection: selection))
    {
    }

    /// <summary>
    /// Creates a formatter bound to the given terminal using managed formatter options.
    /// </summary>
    public GhosttyFormatter(GhosttyTerminal terminal, in GhosttyFormatterOptions options)
    {
        ArgumentNullException.ThrowIfNull(terminal);
        NativeLibraryLoader.Initialize();

        unsafe
        {
            GhosttyVtNative.GhosttySelectionRange nativeSelection = default;
            GhosttyVtNative.GhosttyFormatterTerminalOptions nativeOptions =
                CreateNativeOptions(in options, &nativeSelection);
            Initialize(terminal, nativeOptions);
        }
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
        byte* output = null;
        ThrowIfFailed(
            GhosttyVtNative.FormatterFormatAlloc(_handle, null, &output, out nuint written),
            "ghostty_formatter_format_alloc");

        if (written == 0 || output is null)
        {
            return [];
        }

        try
        {
            byte[] data = new byte[checked((int)written)];
            Marshal.Copy((nint)output, data, 0, data.Length);
            return data;
        }
        finally
        {
            GhosttyVtNative.Free(null, output, written);
        }
    }

    /// <summary>Formats the current terminal snapshot as a UTF-8 string.</summary>
    public string FormatToString()
    {
        byte[] data = Format();
        return data.Length == 0 ? string.Empty : Encoding.UTF8.GetString(data);
    }

    /// <summary>Streams the current terminal snapshot without building an intermediate native buffer.</summary>
    public unsafe void WriteTo(Stream destination)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.CanWrite)
        {
            throw new ArgumentException("The destination stream is not writable.", nameof(destination));
        }

        WriterContext context = new(destination);
        GCHandle contextHandle = GCHandle.Alloc(context);
        try
        {
            GhosttyVtNative.GhosttyWriter writer = new(
                (nint)(delegate* unmanaged[Cdecl]<nint, byte*, nuint, byte>)&Write,
                GCHandle.ToIntPtr(contextHandle));
            GhosttyVtNative.GhosttyResult result = GhosttyVtNative.FormatterFormat(_handle, writer);
            context.Failure?.Throw();
            ThrowIfFailed(result, "ghostty_formatter_format");
        }
        finally
        {
            contextHandle.Free();
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

    private static unsafe GhosttyVtNative.GhosttyFormatterTerminalOptions CreateNativeOptions(
        in GhosttyFormatterOptions options,
        GhosttyVtNative.GhosttySelectionRange* selectionStorage)
    {
        GhosttyVtNative.GhosttyFormatterTerminalOptions nativeOptions =
            GhosttyVtNative.GhosttyFormatterTerminalOptions.CreateSized();
        nativeOptions.Emit = options.Format;
        nativeOptions.Unwrap = options.Unwrap;
        nativeOptions.Trim = options.Trim;
        nativeOptions.Extra = new GhosttyVtNative.GhosttyFormatterTerminalExtra
        {
            Size = (nuint)Marshal.SizeOf<GhosttyVtNative.GhosttyFormatterTerminalExtra>(),
            Palette = options.Extra.IncludePalette,
            Modes = options.Extra.IncludeModes,
            ScrollingRegion = options.Extra.IncludeScrollingRegion,
            Tabstops = options.Extra.IncludeTabstops,
            Pwd = options.Extra.IncludeWorkingDirectory,
            Keyboard = options.Extra.IncludeKeyboardModes,
            Screen = new GhosttyVtNative.GhosttyFormatterScreenExtra
            {
                Size = (nuint)Marshal.SizeOf<GhosttyVtNative.GhosttyFormatterScreenExtra>(),
                Cursor = options.Extra.Screen.IncludeCursor,
                Style = options.Extra.Screen.IncludeStyle,
                Hyperlink = options.Extra.Screen.IncludeHyperlinks,
                Protection = options.Extra.Screen.IncludeProtection,
                KittyKeyboard = options.Extra.Screen.IncludeKittyKeyboard,
                Charsets = options.Extra.Screen.IncludeCharsets,
            },
        };

        if (options.Selection is GhosttySelection selection)
        {
            *selectionStorage = selection.ToNative();
            nativeOptions.Selection = selectionStorage;
        }

        return nativeOptions;
    }

    private static void ThrowIfFailed(GhosttyVtNative.GhosttyResult result, string operation)
    {
        if (result == GhosttyVtNative.GhosttyResult.Success)
        {
            return;
        }

        throw new InvalidOperationException($"{operation} failed with {result}.");
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe byte Write(nint userdata, byte* data, nuint length)
    {
        WriterContext? context = GCHandle.FromIntPtr(userdata).Target as WriterContext;
        if (context is null || length > int.MaxValue)
        {
            return 0;
        }

        try
        {
            context.Stream.Write(new ReadOnlySpan<byte>(data, checked((int)length)));
            return 1;
        }
        catch (Exception exception)
        {
            context.Failure = System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception);
            return 0;
        }
    }

    private sealed class WriterContext(Stream stream)
    {
        public Stream Stream { get; } = stream;

        public System.Runtime.ExceptionServices.ExceptionDispatchInfo? Failure { get; set; }
    }
}
