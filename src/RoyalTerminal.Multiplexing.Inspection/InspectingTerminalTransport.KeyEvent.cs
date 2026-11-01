// Copyright (c) Royal Apps. Licensed under the MIT license.
using System.Buffers;
using System.Text;
using RoyalTerminal.Terminal;
namespace RoyalTerminal.Multiplexing.Inspection;

public sealed partial class InspectingTerminalTransport
{
    /// <summary>Encode one layout-aware client press or repeat under the canonical VT modes. Does not write to the process.</summary>
    public byte[] EncodeKeyEvent(string keyId, string text, int unshifted, int modifiers, int consumed, bool repeat)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyId);
        if (keyId.Length > 48 || keyId.Any(c => !char.IsAsciiLetterOrDigit(c))) throw new ArgumentException("Invalid physical key identifier.");
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length != 0 && (Rune.DecodeFromUtf16(text,out var rune,out int read) != OperationStatus.Done || read != text.Length || Rune.IsControl(rune)))
            throw new ArgumentException("The generated text must be a single printable scalar.");
        if (unshifted != 0 && (!Rune.IsValid(unshifted) || Rune.IsControl(new Rune(unshifted)))) throw new ArgumentException("Invalid unshifted scalar.");
        if ((modifiers & ~63) != 0 || (consumed & ~1) != 0 || (consumed & ~modifiers) != 0) throw new ArgumentException("Invalid keyboard modifier mask.");
        var key = new TerminalKeyEncodingRequest(keyId,repeat?TerminalInputAction.Repeat:TerminalInputAction.Press,text.Length == 0 ? null : text,
            (TerminalModifiers)modifiers,UnshiftedCodepoint:(uint)unshifted,ConsumedModifiers:(TerminalModifiers)consumed);
        lock (_gate)
        {
            ThrowIfDisposed();
            if (!_processor.TryEncodeKey(key,out var bytes)) throw new ArgumentException("The terminal cannot encode this key in its current keyboard mode.");
            return bytes;
        }
    }
}
