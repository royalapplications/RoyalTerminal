// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Rendering.Interop.Ghostty - Native render theme payload.

using System.Runtime.InteropServices;

namespace RoyalTerminal.Rendering.Interop.Ghostty.Native;

[StructLayout(LayoutKind.Sequential)]
internal struct GhosttyRenderThemeNative
{
    public uint DefaultForegroundArgb;
    public uint DefaultBackgroundArgb;
    public uint CursorArgb;
}
