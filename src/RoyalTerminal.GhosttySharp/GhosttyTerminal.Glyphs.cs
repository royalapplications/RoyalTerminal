// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers;
using RoyalTerminal.GhosttySharp.Native;
using RoyalTerminal.Terminal.Glyphs;

namespace RoyalTerminal.GhosttySharp;

/// <summary>A copied native glyph registration, independent of the terminal's lifetime.</summary>
/// <param name="Codepoint">Registered private-use codepoint.</param>
/// <param name="Registration">Owned outline and normalized rendering metadata.</param>
public readonly record struct GhosttyGlyphRegistration(uint Codepoint, TerminalGlyphRegistration Registration);

public sealed partial class GhosttyTerminal
{
    /// <summary>
    /// Reads glossary count and pending dirty flag without allocation or mutation.
    /// Inspect before render-state update clears native dirty flags. Requires the
    /// RoyalTerminal extension; serialize with all access to this terminal.
    /// </summary>
    public unsafe void GetGlyphGlossaryInfo(out uint count, out bool dirty)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        uint nativeCount = 0;
        byte nativeDirty = 0;
        ThrowIfFailed(GhosttyVtNative.GlyphGlossaryInfo(_handle, &nativeCount, &nativeDirty), "ghostty_royal_glyph_glossary_info");
        count = nativeCount;
        dirty = nativeDirty != 0;
    }

    /// <summary>
    /// Copies all registered glyphs in FIFO order. The results survive replacement,
    /// clearing, reset and terminal disposal. Native normalized constraints cannot
    /// recover request aliases: advance maps to height, cover to contain, baseline
    /// to start. Requires the RoyalTerminal extension. Serialize with terminal
    /// mutation throughout the call; a lifetime lease prevents premature freeing.
    /// </summary>
    public unsafe GhosttyGlyphRegistration[] GetGlyphRegistrations()
    {
        using NativeLifetimeLease lease = AcquireNativeLifetimeLease();
        GetGlyphGlossaryInfo(out uint count, out _);
        if (count > 1024) throw new InvalidOperationException("Native glyph count exceeds the protocol limit.");
        if (count == 0) return [];
        GhosttyGlyphRegistration[] registrations = new GhosttyGlyphRegistration[count];
        for (uint index = 0; index < count; index++)
        {
            GhosttyVtNative.RoyalGlyphMetadata metadata = GhosttyVtNative.RoyalGlyphMetadata.CreateSized();
            ThrowIfFailed(GhosttyVtNative.GlyphMetadata(_handle, index, &metadata), "ghostty_royal_glyph_metadata");
            if (metadata.PointCount > TerminalGlyphDecoder.MaxPayloadBytes / 12 || metadata.ContourCount > metadata.PointCount ||
                metadata.Width is not (1 or 2) || metadata.Sizing > 2 || metadata.Horizontal > 2 || metadata.Vertical > 2 ||
                metadata.UnitsPerEm == 0 || metadata.AdvanceWidth == 0 || metadata.LineHeight == 0 ||
                !TerminalGlyphGlossary.IsPrivateUse(metadata.Codepoint))
                throw new InvalidOperationException("Invalid native glyph metadata.");
            ushort[] contours = new ushort[metadata.ContourCount];
            TerminalGlyphPoint[] points = new TerminalGlyphPoint[metadata.PointCount];
            GhosttyVtNative.RoyalGlyphPoint[] scratch = ArrayPool<GhosttyVtNative.RoyalGlyphPoint>.Shared.Rent(Math.Max(1, points.Length));
            try
            {
                fixed (GhosttyVtNative.RoyalGlyphPoint* pointBuffer = scratch)
                fixed (ushort* contourBuffer = contours)
                    ThrowIfFailed(GhosttyVtNative.GlyphOutline(_handle, index, pointBuffer, (nuint)points.Length,
                        contourBuffer, (nuint)contours.Length), "ghostty_royal_glyph_outline");
                for (int i = 0; i < points.Length; i++)
                {
                    if (scratch[i].OnCurve > 1) throw new InvalidOperationException("Invalid native glyph point.");
                    points[i] = new(scratch[i].X, scratch[i].Y, scratch[i].OnCurve == 1);
                }
            }
            finally { ArrayPool<GhosttyVtNative.RoyalGlyphPoint>.Shared.Return(scratch); }
            int previous = -1;
            foreach (ushort end in contours)
            {
                if (end <= previous || end >= points.Length) throw new InvalidOperationException("Invalid native glyph contour.");
                previous = end;
            }
            if (previous + 1 != points.Length) throw new InvalidOperationException("Incomplete native glyph contours.");
            TerminalGlyphSize size = metadata.Sizing switch
            {
                0 => TerminalGlyphSize.Height,
                1 => TerminalGlyphSize.Contain,
                _ => TerminalGlyphSize.Stretch,
            };
            TerminalGlyphLayout layout = new(size, (TerminalGlyphAlignment)metadata.Horizontal,
                (TerminalGlyphAlignment)metadata.Vertical, metadata.PadTop, metadata.PadRight, metadata.PadBottom, metadata.PadLeft);
            registrations[index] = new(metadata.Codepoint, new(new(contours, points), metadata.UnitsPerEm,
                metadata.AdvanceWidth, metadata.LineHeight, (byte)metadata.Width, layout));
        }
        return registrations;
    }
}
