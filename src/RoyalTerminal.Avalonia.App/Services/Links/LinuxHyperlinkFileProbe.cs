// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.InteropServices;
using System.Collections.Immutable;

namespace RoyalTerminal.Avalonia.App.Services.Links;

internal static partial class LinuxHyperlinkFileProbe
{
    internal static HyperlinkFileFacts Read(string canonical)
    {
        nint file = g_file_new_for_path(canonical);
        if (file == 0) throw new IOException("Unable to create local file query.");
        try
        {
            nint info = g_file_query_info(file, "standard::type,standard::content-type,access::can-execute", 0, 0, out nint error);
            if (error != 0) g_error_free(error);
            if (info == 0) return new(canonical, HyperlinkFileKind.Other, false, false);
            try
            {
                HyperlinkFileKind kind = g_file_info_get_file_type(info) switch
                {
                    1 => HyperlinkFileKind.Regular,
                    2 => HyperlinkFileKind.Directory,
                    _ => HyperlinkFileKind.Other,
                };
                if (kind == HyperlinkFileKind.Other) return new(canonical, kind, false, false);
                if (g_file_info_has_attribute(info, "access::can-execute") == 0)
                    throw new IOException("File execution permission is unavailable.");
                nint type = g_file_info_get_content_type(info);
                return new(canonical, kind, g_file_info_get_attribute_boolean(info, "access::can-execute") != 0,
                    type != 0 && IsUnsafeType(type));
            }
            finally { g_object_unref(info); }
        }
        finally { g_object_unref(file); }
    }

    private static bool IsUnsafeType(nint type)
    {
        // Do not use g_content_type_can_be_executable: GLib includes all
        // text/plain descendants there, which would also reject ordinary notes.
        if (g_content_type_is_a(type, "application/x-executable") != 0 ||
            g_content_type_is_a(type, "application/x-sharedlib") != 0 ||
            g_content_type_is_a(type, "application/x-desktop") != 0) return true;
        foreach (string parent in ScriptTypes)
            if (g_content_type_is_a(type, parent) != 0) return true;
        return false;
    }

    internal static bool IsScriptType(string? type)
    {
        foreach (string parent in ScriptTypes)
            if (string.Equals(type, parent, StringComparison.Ordinal)) return true;
        return false;
    }

    private static readonly ImmutableArray<string> ScriptTypes =
    [
        "application/x-shellscript", "text/x-shellscript", "application/x-csh", "text/x-csh",
        "application/x-perl", "text/x-perl", "application/x-python", "text/x-python",
        "application/x-ruby", "text/x-ruby", "application/javascript", "text/javascript",
        "application/ecmascript", "text/ecmascript", "application/x-php", "text/x-php",
        "application/x-lua", "text/x-lua", "application/x-tcl", "text/x-tcl",
    ];

    [LibraryImport("libgio-2.0.so.0", StringMarshalling = StringMarshalling.Utf8)] private static partial nint g_file_new_for_path(string path);
    [LibraryImport("libgio-2.0.so.0", StringMarshalling = StringMarshalling.Utf8)] private static partial nint g_file_query_info(nint file, string attributes, int flags, nint cancellable, out nint error);
    [LibraryImport("libgio-2.0.so.0")] private static partial int g_file_info_get_file_type(nint info);
    [LibraryImport("libgio-2.0.so.0")] private static partial nint g_file_info_get_content_type(nint info);
    [LibraryImport("libgio-2.0.so.0", StringMarshalling = StringMarshalling.Utf8)] private static partial int g_file_info_has_attribute(nint info, string attribute);
    [LibraryImport("libgio-2.0.so.0", StringMarshalling = StringMarshalling.Utf8)] private static partial int g_file_info_get_attribute_boolean(nint info, string attribute);
    [LibraryImport("libgio-2.0.so.0", StringMarshalling = StringMarshalling.Utf8)] private static partial int g_content_type_is_a(nint type, string parent);
    [LibraryImport("libgobject-2.0.so.0")] private static partial void g_object_unref(nint value);
    [LibraryImport("libglib-2.0.so.0")] private static partial void g_error_free(nint error);
}
