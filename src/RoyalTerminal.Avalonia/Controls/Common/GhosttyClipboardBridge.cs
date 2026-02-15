// Licensed under the MIT License.
// RoyalTerminal.Avalonia.Controls - Shared clipboard bridge helpers for GhosttySurface controls.

using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using RoyalTerminal.Avalonia.Diagnostics;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.GhosttySharp.Native;

namespace RoyalTerminal.Avalonia.Controls;

internal static class GhosttyClipboardBridge
{
    public static void HandleReadRequest(
        Control owner,
        GhosttySurface? surface,
        nint state,
        IGhosttyLogger logger)
    {
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                var topLevel = TopLevel.GetTopLevel(owner);
                var avClipboard = topLevel?.Clipboard;
                if (avClipboard is null || surface is null) return;

                var text = await avClipboard.TryGetTextAsync();
                if (text is not null)
                    surface.CompleteClipboardRequest(text, state, true);
            }
            catch (Exception ex)
            {
                logger.Error($"Clipboard read error: {ex.Message}", ex);
            }
        });
    }

    public static void HandleWriteRequest(
        Control owner,
        nint contentPtr,
        nuint len,
        IGhosttyLogger logger)
    {
        string? clipText;
        unsafe
        {
            clipText = ExtractClipboardText(contentPtr, len);
        }
        if (clipText is null) return;

        var textToWrite = clipText;
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                var topLevel = TopLevel.GetTopLevel(owner);
                var avClipboard = topLevel?.Clipboard;
                if (avClipboard is null) return;

                await avClipboard.SetTextAsync(textToWrite);
            }
            catch (Exception ex)
            {
                logger.Error($"Clipboard write error: {ex.Message}", ex);
            }
        });
    }

    public static async Task CopySelectionAsync(Control owner, GhosttySurface? surface)
    {
        if (surface is null) return;
        var text = surface.ReadSelection();
        if (string.IsNullOrEmpty(text)) return;

        var clipboard = TopLevel.GetTopLevel(owner)?.Clipboard;
        if (clipboard is not null)
            await clipboard.SetTextAsync(text);
    }

    public static async Task PasteAsync(Control owner, GhosttySurface? surface)
    {
        if (surface is null) return;

        var clipboard = TopLevel.GetTopLevel(owner)?.Clipboard;
        if (clipboard is null) return;

        var text = await clipboard.TryGetTextAsync();
        if (!string.IsNullOrEmpty(text))
            surface.SendText(text);
    }

    private static unsafe string? ExtractClipboardText(nint contentPtr, nuint len)
    {
        string? clipText = null;
        for (nuint i = 0; i < len; i++)
        {
            var content = (GhosttyClipboardContent*)((byte*)contentPtr +
                (nint)(i * (nuint)sizeof(GhosttyClipboardContent)));
            if (content->Data != null)
                clipText = Marshal.PtrToStringUTF8((nint)content->Data);
        }

        return clipText;
    }
}
