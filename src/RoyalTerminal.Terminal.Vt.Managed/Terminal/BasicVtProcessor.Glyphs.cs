// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.InteropServices;
using RoyalTerminal.Terminal.Glyphs;

namespace RoyalTerminal.Terminal;

public sealed partial class BasicVtProcessor
{
    internal const int GlyphCommandMaximumBytes = 1024 * 1024;
    internal const int GlyphCommandRetainedBytes = 4096;
    internal int GlyphCommandCapacity => _apcBuffer.Capacity;
    internal Action<TerminalGlyphAllocation>? GlyphAllocationCheckpoint { get; set; }

    private void ClearGlyphCommandBuffer()
    {
        _apcBuffer.Clear();
        if (_apcBuffer.Capacity > GlyphCommandRetainedBytes) _apcBuffer.Capacity = 0;
    }

    private void AppendGlyphCommand(ReadOnlySpan<byte> payload)
    {
        if (!_apcGlyphEnabled || _apcTruncated) return;
        if (payload.Length > GlyphCommandMaximumBytes - _apcBuffer.Count)
        {
            // Native apc.Handler deinitializes the parser and ignores the rest
            // on a byte-limit refusal or allocation failure. Never execute a
            // retained prefix, or report the recognized command as unknown.
            _apcTruncated = true;
            ClearGlyphCommandBuffer();
            return;
        }
        int offset = _apcBuffer.Count, required = offset + payload.Length;
        try
        {
            if (required > _apcBuffer.Capacity)
            {
                GlyphAllocationCheckpoint?.Invoke(TerminalGlyphAllocation.CommandBuffer);
                _apcBuffer.Capacity = (int)Math.Min(GlyphCommandMaximumBytes,
                    Math.Max(required, Math.Max(4, (long)_apcBuffer.Capacity * 2)));
            }
        }
        catch (OutOfMemoryException)
        {
            _apcTruncated = true;
            ClearGlyphCommandBuffer();
            return;
        }
        CollectionsMarshal.SetCount(_apcBuffer, required);
        payload.CopyTo(CollectionsMarshal.AsSpan(_apcBuffer)[offset..]);
    }

    private void ExecuteGlyphCommand()
    {
        if (!_apcGlyphEnabled || _apcTruncated) return;
        ManagedGlyphProtocol.Result result = ManagedGlyphProtocol.Execute(CollectionsMarshal.AsSpan(_apcBuffer),
            ref _screen.GlyphGlossaryStorage, GlyphAllocationCheckpoint);
        // Ghostty marks register/clear requests dirty before writing their
        // responses. A host exception cannot hide an already executed edit.
        if (result.Mutated) _screen.NotifyGlyphGlossaryChanged();
        ManagedGlyphProtocol.Send(in result, ResponseCallback, GlyphCoverageSource);
    }
}
