// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers;
using RoyalTerminal.Avalonia.Rendering;

namespace RoyalTerminal.Terminal;

public sealed partial class BasicVtProcessor
{
    private void HandleDecRequestStatusString(ReadOnlySpan<byte> request)
    {
        if (ResponseCallback is not { } callback) return;
        Span<byte> response = stackalloc byte[ManagedStatusReplyFormatter.MaximumBytes];
        int written;
        if (request.SequenceEqual("m"u8))
        {
            TerminalCell pen = new()
            {
                Attributes = _currentAttrs, Decorations = _currentDecorations,
                UnderlineStyle = _currentUnderlineStyle,
                ForegroundIdentity = GetColorIdentity(_currentFgKind, _currentFgPaletteIndex, _currentFg),
                BackgroundIdentity = CurrentBackgroundIdentity,
            };
            written = ManagedStatusReplyFormatter.Sgr(response, in pen);
        }
        else if (request.SequenceEqual("r"u8))
            written = ManagedStatusReplyFormatter.Margins(response, _scrollTop + 1, _scrollBottom + 1, horizontal: false);
        else if (request.SequenceEqual("s"u8) && _extendedDecModes.Contains(ManagedDecModeFlag.LeftRightMargins))
            written = ManagedStatusReplyFormatter.Margins(response, _scrollLeft + 1, RightMargin + 1, horizontal: true);
        else if (request.SequenceEqual(" q"u8))
            written = ManagedStatusReplyFormatter.Cursor(response, CursorStyleReport);
        else written = ManagedStatusReplyFormatter.Unsupported(response);
        // The public callback transfers an owned mutable array; never expose
        // stack, pooled, shared or subsequently reused parser storage.
        callback(response[..written].ToArray());
    }

    private void HandleTerminfoQueries(ReadOnlySpan<byte> payload)
    {
        if (ResponseCallback is null) { _dcsBuffer.Clear(); return; }
        // Preserve the batch across callbacks without borrowing live List
        // storage. Most queries fit the stack; the parser bounds large batches
        // at 1 MiB and pooled storage is returned even if a callback throws.
        byte[]? rented = null;
        Span<byte> copied = payload.Length <= 512
            ? stackalloc byte[512]
            : (rented = ArrayPool<byte>.Shared.Rent(payload.Length));
        copied = copied[..payload.Length];
        payload.CopyTo(copied);
        _dcsBuffer.Clear();
        try
        {
            ReadOnlySpan<byte> keys = copied;
            while (!keys.IsEmpty)
            {
                int separator = keys.IndexOf((byte)';');
                ReadOnlySpan<byte> key = separator < 0 ? keys : keys[..separator];
                if (ResponseCallback is { } callback &&
                    GhosttyXtgettcap.TryCreateResponse(key, _options.TerminfoName, out byte[] response))
                    callback(response);
                if (separator < 0) break;
                keys = keys[(separator + 1)..];
            }
        }
        finally
        {
            if (rented is not null) ArrayPool<byte>.Shared.Return(rented, clearArray: true);
            else copied.Clear();
        }
    }
}
