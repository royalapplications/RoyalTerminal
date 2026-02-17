// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia.Controls - Clipboard adapter for Ghostty controls.

using Avalonia.Controls;
using RoyalTerminal.Avalonia.Diagnostics;
using RoyalTerminal.GhosttySharp;

namespace RoyalTerminal.Avalonia.Controls;

internal sealed class GhosttyClipboardAdapter
{
    private readonly Control _owner;
    private readonly Func<IGhosttyLogger> _loggerAccessor;

    public GhosttyClipboardAdapter(Control owner, Func<IGhosttyLogger> loggerAccessor)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _loggerAccessor = loggerAccessor ?? throw new ArgumentNullException(nameof(loggerAccessor));
    }

    public void HandleReadRequest(GhosttySurface? surface, nint state)
    {
        GhosttyClipboardBridge.HandleReadRequest(_owner, surface, state, _loggerAccessor());
    }

    public void HandleWriteRequest(nint contentPtr, nuint len)
    {
        GhosttyClipboardBridge.HandleWriteRequest(_owner, contentPtr, len, _loggerAccessor());
    }

    public Task CopySelectionAsync(GhosttySurface? surface)
    {
        return GhosttyClipboardBridge.CopySelectionAsync(_owner, surface);
    }

    public Task PasteAsync(GhosttySurface? surface)
    {
        return GhosttyClipboardBridge.PasteAsync(_owner, surface);
    }
}
