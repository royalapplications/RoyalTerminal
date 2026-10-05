// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.InteropServices;

namespace RoyalTerminal.Avalonia.App.Services.Links;

// Query associations only; never execute shell commands to discover a handler.
// Launch Services returns a stable bundle ID; GIO/Windows return a display name.
internal sealed partial class DesktopHyperlinkHandlerResolver : ITerminalHyperlinkHandlerResolver
{
    public async ValueTask<string?> ResolveAsync(string scheme, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Uri.CheckSchemeName(scheme)) return null;
        string? result = await Task.Run(() => Resolve(scheme), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    private static string? Resolve(string scheme)
    {
        try
        {
            if (OperatingSystem.IsMacOS()) return ResolveMacOs(scheme);
            if (OperatingSystem.IsWindows()) return ResolveWindows(scheme);
            if (OperatingSystem.IsLinux()) return ResolveLinux(scheme);
            return null;
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            return null;
        }
    }

    private static string? ResolveLinux(string scheme)
    {
        nint app = g_app_info_get_default_for_uri_scheme(scheme);
        if (app == 0) return null;
        try { return Marshal.PtrToStringUTF8(g_app_info_get_display_name(app)); }
        finally { g_object_unref(app); }
    }

    private static unsafe string? ResolveWindows(string scheme)
    {
        const uint isProtocol = 0x1000, friendlyAppName = 4;
        uint length = 0;
        if (AssocQueryStringW(isProtocol, friendlyAppName, scheme, null, null, ref length) != 1 || length is < 2 or > 32768)
            return null;
        char[] buffer = new char[length];
        fixed (char* output = buffer)
        {
            if (AssocQueryStringW(isProtocol, friendlyAppName, scheme, null, output, ref length) != 0) return null;
            // Limit by the owned allocation even if associations change between queries.
            int terminator = buffer.AsSpan().IndexOf('\0');
            return terminator > 0 ? new string(output, 0, terminator) : null;
        }
    }

    private static unsafe string? ResolveMacOs(string scheme)
    {
        const uint utf8 = 0x08000100;
        nint input = CFStringCreateWithCString(0, scheme, utf8);
        if (input == 0) return null;
        try
        {
            nint handler = LSCopyDefaultHandlerForURLScheme(input);
            if (handler == 0) return null;
            try
            {
                nint capacity = CFStringGetMaximumSizeForEncoding(CFStringGetLength(handler), utf8) + 1;
                if (capacity is < 2 or > 32768) return null;
                byte[] bytes = new byte[(int)capacity];
                fixed (byte* output = bytes)
                    return CFStringGetCString(handler, output, capacity, utf8) != 0 ? Marshal.PtrToStringUTF8((nint)output) : null;
            }
            finally { CFRelease(handler); }
        }
        finally { CFRelease(input); }
    }

    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const string CoreServices = "/System/Library/Frameworks/CoreServices.framework/CoreServices";
    [LibraryImport("libgio-2.0.so.0", StringMarshalling = StringMarshalling.Utf8)] private static partial nint g_app_info_get_default_for_uri_scheme(string scheme);
    [LibraryImport("libgio-2.0.so.0")] private static partial nint g_app_info_get_display_name(nint app);
    [LibraryImport("libgobject-2.0.so.0")] private static partial void g_object_unref(nint app);
    [LibraryImport("shlwapi.dll", StringMarshalling = StringMarshalling.Utf16)] private static unsafe partial int AssocQueryStringW(uint flags, uint kind, string association, string? extra, char* output, ref uint length);
    [LibraryImport(CoreServices)] private static partial nint LSCopyDefaultHandlerForURLScheme(nint scheme);
    [LibraryImport(CoreFoundation, StringMarshalling = StringMarshalling.Utf8)] private static partial nint CFStringCreateWithCString(nint allocator, string text, uint encoding);
    [LibraryImport(CoreFoundation)] private static partial nint CFStringGetLength(nint text);
    [LibraryImport(CoreFoundation)] private static partial nint CFStringGetMaximumSizeForEncoding(nint length, uint encoding);
    [LibraryImport(CoreFoundation)] private static unsafe partial byte CFStringGetCString(nint text, byte* buffer, nint capacity, uint encoding);
    [LibraryImport(CoreFoundation)] private static partial void CFRelease(nint value);
}
