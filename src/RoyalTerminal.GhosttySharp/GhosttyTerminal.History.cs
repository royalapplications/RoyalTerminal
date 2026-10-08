// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.GhosttySharp.Native;

namespace RoyalTerminal.GhosttySharp;

public sealed partial class GhosttyTerminal
{
    internal unsafe bool TryGetHistoryInfo(out GhosttyVtNative.RoyalHistoryInfo info)
    {
        info = new() { Size = (nuint)sizeof(GhosttyVtNative.RoyalHistoryInfo) };
        try
        {
            ThrowIfFailed(GhosttyVtNative.RoyalHistoryGet(Handle, ref info), "ghostty_royal_history_info");
            return true;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }
}
