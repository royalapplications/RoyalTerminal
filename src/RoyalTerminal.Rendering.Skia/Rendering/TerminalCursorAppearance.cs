// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Avalonia.Rendering;

/// <summary>Ghostty's cursor priority, independent of the VT engine and host UI.</summary>
internal static class TerminalCursorAppearance
{
    internal static CursorStyle? Resolve(CursorStyle requested, bool inViewport, bool passwordInput,
        bool visible, bool focused, bool blinking, bool blinkVisible)
    {
        if (!inViewport) return null;
        if (passwordInput) return CursorStyle.Lock;
        if (!visible) return null;
        if (!focused) return CursorStyle.BlockHollow;
        if (blinking && !blinkVisible) return null;
        return requested;
    }
}
