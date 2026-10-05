// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.InteropServices;

namespace RoyalTerminal.Avalonia.App.Services.Notifications;

// Optional GNOME settings access. Missing GLib or schemas is a normal fallback,
// not a reason to initialize GTK, connect another display or launch gsettings.
internal static partial class LinuxDesktopThemeSettings
{
    internal static string? Read(string schemaName, string key)
    {
        if (!OperatingSystem.IsLinux()) return null;
        try
        {
            nint source = g_settings_schema_source_get_default();
            if (source == 0) return null;
            nint schema = g_settings_schema_source_lookup(source, schemaName, 1);
            if (schema == 0) return null;
            try
            {
                if (g_settings_schema_get_path(schema) == 0 || g_settings_schema_has_key(schema, key) == 0) return null;
                nint schemaKey = g_settings_schema_get_key(schema, key);
                if (schemaKey == 0) return null;
                try { if (g_variant_type_equal(g_settings_schema_key_get_value_type(schemaKey), "s") == 0) return null; }
                finally { g_settings_schema_key_unref(schemaKey); }
                nint settings = g_settings_new_full(schema, 0, 0);
                if (settings == 0) return null;
                try
                {
                    nint value = g_settings_get_string(settings, key);
                    if (value == 0) return null;
                    try { return Marshal.PtrToStringUTF8(value); }
                    finally { g_free(value); }
                }
                finally { g_object_unref(settings); }
            }
            finally { g_settings_schema_unref(schema); }
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        { return null; }
    }

    [LibraryImport("libgio-2.0.so.0")] private static partial nint g_settings_schema_source_get_default();
    [LibraryImport("libgio-2.0.so.0", StringMarshalling = StringMarshalling.Utf8)] private static partial nint g_settings_schema_source_lookup(nint source, string schema, int recursive);
    [LibraryImport("libgio-2.0.so.0", StringMarshalling = StringMarshalling.Utf8)] private static partial int g_settings_schema_has_key(nint schema, string key);
    [LibraryImport("libgio-2.0.so.0")] private static partial nint g_settings_schema_get_path(nint schema);
    [LibraryImport("libgio-2.0.so.0", StringMarshalling = StringMarshalling.Utf8)] private static partial nint g_settings_schema_get_key(nint schema, string key);
    [LibraryImport("libgio-2.0.so.0")] private static partial nint g_settings_schema_key_get_value_type(nint key);
    [LibraryImport("libgio-2.0.so.0")] private static partial void g_settings_schema_key_unref(nint key);
    [LibraryImport("libglib-2.0.so.0", StringMarshalling = StringMarshalling.Utf8)] private static partial int g_variant_type_equal(nint type, string other);
    [LibraryImport("libgio-2.0.so.0")] private static partial nint g_settings_new_full(nint schema, nint backend, nint path);
    [LibraryImport("libgio-2.0.so.0", StringMarshalling = StringMarshalling.Utf8)] private static partial nint g_settings_get_string(nint settings, string key);
    [LibraryImport("libgio-2.0.so.0")] private static partial void g_settings_schema_unref(nint schema);
    [LibraryImport("libgobject-2.0.so.0")] private static partial void g_object_unref(nint value);
    [LibraryImport("libglib-2.0.so.0")] private static partial void g_free(nint value);
}
