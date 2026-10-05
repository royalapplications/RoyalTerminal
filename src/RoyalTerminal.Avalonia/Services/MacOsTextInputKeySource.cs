// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.InteropServices;
using Avalonia.Input;
using Avalonia.Threading;

namespace RoyalTerminal.Avalonia.Services;

internal interface ITerminalTextInputKeySource
{
    bool TryGetKey(string text, out KeyEventArgs? key);
}

// Avalonia.Native's active NSTextInputClient sends unmodified printable keys
// only as TextInput. Recover the originating *current* NSEvent synchronously so
// Kitty reporting still sees physical identity/repeats; never synthesize a key
// for paste, asynchronous IME commits or programmatically injected text.
internal sealed partial class MacOsTextInputKeySource : ITerminalTextInputKeySource
{
    private const string ObjectiveC = "/usr/lib/libobjc.A.dylib";
    private const string AppKit = "/System/Library/Frameworks/AppKit.framework/AppKit";

    public bool TryGetKey(string text, out KeyEventArgs? key)
    {
        key = null;
        if (!OperatingSystem.IsMacOS() || !Dispatcher.UIThread.CheckAccess()) return false;
        if (!NativeLibrary.TryLoad(AppKit, out nint library)) return false;
        try
        {
            // Read the existing NSApp; do not create a Cocoa application in
            // headless/custom hosts by calling +sharedApplication.
            if (!NativeLibrary.TryGetExport(library, "NSApp", out nint symbol)) return false;
            nint app = Marshal.ReadIntPtr(symbol);
            if (app == 0) return false;
            nint current = Send(app, Selector("currentEvent"));
            if (current == 0 || Send(current, Selector("type")) != 10) return false;
            nint characters = Send(current, Selector("characters"));
            nint utf8 = characters == 0 ? 0 : Send(characters, Selector("UTF8String"));
            if (utf8 == 0 || Marshal.PtrToStringUTF8(utf8) != text) return false;
            key = CreateKey((ushort)Send(current, Selector("keyCode")), (nuint)Send(current, Selector("modifierFlags")), text);
            return key is not null;
        }
        finally { NativeLibrary.Free(library); }
    }

    internal static KeyEventArgs? CreateKey(ushort scanCode, nuint flags, string text)
    {
        if (scanCode == ushort.MaxValue) return null;
        for (PhysicalKey physical = PhysicalKey.Backquote; physical <= PhysicalKey.Undo; physical++)
        {
            if (MacOsKeyboardLayout.ScanCode(physical) != scanCode) continue;
            KeyModifiers modifiers = KeyModifiers.None;
            if ((flags & (1U << 17)) != 0) modifiers |= KeyModifiers.Shift;
            if ((flags & (1U << 18)) != 0) modifiers |= KeyModifiers.Control;
            if ((flags & (1U << 19)) != 0) modifiers |= KeyModifiers.Alt;
            if ((flags & (1U << 20)) != 0) modifiers |= KeyModifiers.Meta;
            return new() { PhysicalKey = physical, Key = physical.ToQwertyKey(), KeyModifiers = modifiers, KeySymbol = text };
        }
        return null;
    }

    [LibraryImport(ObjectiveC, EntryPoint = "sel_registerName", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint Selector(string name);
    [LibraryImport(ObjectiveC, EntryPoint = "objc_msgSend")]
    private static partial nint Send(nint receiver, nint selector);
}
