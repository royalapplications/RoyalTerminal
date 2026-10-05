// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.InteropServices;

namespace RoyalTerminal.Avalonia.App.Services.Links;

// Core Foundation's C resource API and Launch Services' UTI conformance query
// expose the same resource/type model as Ghostty's Swift URLResourceValues.
internal static partial class MacOsHyperlinkFileProbe
{
    internal static unsafe HyperlinkFileFacts Read(string canonical)
    {
        nint foundation = NativeLibrary.Load(CoreFoundation);
        try
        {
            nint services = NativeLibrary.Load(CoreServices);
            try
            {
                nint path = CFStringCreateWithCString(0, canonical, 0x08000100);
                if (path == 0) throw new IOException("Unable to represent local path.");
                try
                {
                    nint url = CFURLCreateWithFileSystemPath(0, path, 0 /* POSIX */, 0);
                    if (url == 0) throw new IOException("Unable to create file URL.");
                    try
                    {
                        Span<nint> keys = stackalloc nint[]
                        {
                            Constant(foundation, "kCFURLIsRegularFileKey"), Constant(foundation, "kCFURLIsDirectoryKey"),
                            Constant(foundation, "kCFURLIsExecutableKey"), Constant(foundation, "kCFURLTypeIdentifierKey"),
                        };
                        nint array;
                        fixed (nint* values = keys) array = CFArrayCreate(0, values, keys.Length, 0);
                        if (array == 0) throw new IOException("Unable to query resource properties.");
                        try
                        {
                            nint properties = CFURLCopyResourcePropertiesForKeys(url, array, out nint error);
                            if (error != 0) CFRelease(error);
                            if (properties == 0) return new(canonical, HyperlinkFileKind.Other, false, false);
                            try
                            {
                                HyperlinkFileKind kind = Boolean(properties, keys[1]) ? HyperlinkFileKind.Directory :
                                    Boolean(properties, keys[0]) ? HyperlinkFileKind.Regular : HyperlinkFileKind.Other;
                                nint type = CFDictionaryGetValue(properties, keys[3]);
                                bool unsafeType = type != 0 && (Conforms(type, services, "kUTTypeApplication") ||
                                    Conforms(type, services, "kUTTypeExecutable") || Conforms(type, services, "kUTTypeScript"));
                                return new(canonical, kind, Boolean(properties, keys[2]), unsafeType);
                            }
                            finally { CFRelease(properties); }
                        }
                        finally { CFRelease(array); }
                    }
                    finally { CFRelease(url); }
                }
                finally { CFRelease(path); }
            }
            finally { NativeLibrary.Free(services); }
        }
        finally { NativeLibrary.Free(foundation); }
    }

    private static nint Constant(nint library, string name) => Marshal.ReadIntPtr(NativeLibrary.GetExport(library, name));
    private static bool Conforms(nint type, nint library, string name) => UTTypeConformsTo(type, Constant(library, name)) != 0;
    private static bool Boolean(nint properties, nint key)
    {
        nint value = CFDictionaryGetValue(properties, key);
        return value != 0 && CFBooleanGetValue(value) != 0;
    }

    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const string CoreServices = "/System/Library/Frameworks/CoreServices.framework/CoreServices";
    [LibraryImport(CoreFoundation, StringMarshalling = StringMarshalling.Utf8)] private static partial nint CFStringCreateWithCString(nint allocator, string value, uint encoding);
    [LibraryImport(CoreFoundation)] private static partial nint CFURLCreateWithFileSystemPath(nint allocator, nint path, nint style, byte directory);
    [LibraryImport(CoreFoundation)] private static unsafe partial nint CFArrayCreate(nint allocator, nint* values, nint count, nint callbacks);
    [LibraryImport(CoreFoundation)] private static partial nint CFURLCopyResourcePropertiesForKeys(nint url, nint keys, out nint error);
    [LibraryImport(CoreFoundation)] private static partial nint CFDictionaryGetValue(nint dictionary, nint key);
    [LibraryImport(CoreFoundation)] private static partial byte CFBooleanGetValue(nint value);
    [LibraryImport(CoreFoundation)] private static partial void CFRelease(nint value);
    [LibraryImport(CoreServices)] private static partial byte UTTypeConformsTo(nint type, nint parent);
}
