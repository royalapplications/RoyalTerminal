// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.GhosttySharp.Native;
using RoyalTerminal.Terminal;

namespace RoyalTerminal.GhosttySharp;

public sealed partial class GhosttyTerminal
{
    /// <summary>Copies active-screen semantic state. Serialize with terminal mutation.</summary>
    public unsafe TerminalPromptState GetPromptState()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        GhosttyVtNative.RoyalPromptState state = GhosttyVtNative.RoyalPromptState.CreateSized();
        ThrowIfFailed(GhosttyVtNative.PromptState(_handle, &state), "ghostty_royal_prompt_state");
        return new(state.Seen != 0, (TerminalSemanticContent)state.Content, state.ClearEol != 0,
            (TerminalPromptClick)state.Click, (TerminalPromptRedraw)state.Redraw);
    }

    /// <summary>
    /// Copies exact OSC 8 URI/ID bytes. Returns false with required lengths if
    /// either span is too small, without modifying either span. A zero URI length
    /// means no link; a zero ID length denotes the returned implicit numeric ID.
    /// Serialize with mutation/disposal throughout probing and retrying.
    /// </summary>
    public unsafe bool TryReadHyperlink(in GhosttyVtNative.GhosttyGridRef reference,
        Span<byte> uri, Span<byte> id, out int uriLength, out int idLength, out uint implicitId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        GhosttyVtNative.RoyalHyperlinkMetadata metadata = GhosttyVtNative.RoyalHyperlinkMetadata.CreateSized();
        GhosttyVtNative.GhosttyResult result;
        fixed (byte* uriBuffer = uri)
        fixed (byte* idBuffer = id)
            result = GhosttyVtNative.GridRefHyperlink(in reference, &metadata,
                uriBuffer, (nuint)uri.Length, idBuffer, (nuint)id.Length);
        if (result != GhosttyVtNative.GhosttyResult.OutOfSpace)
            ThrowIfFailed(result, "ghostty_royal_grid_ref_hyperlink");
        uriLength = checked((int)metadata.UriLength);
        idLength = checked((int)metadata.IdLength);
        implicitId = metadata.ImplicitId;
        return result == GhosttyVtNative.GhosttyResult.Success;
    }
}
