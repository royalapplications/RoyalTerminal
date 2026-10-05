// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.GhosttySharp.Native;

namespace RoyalTerminal.GhosttySharp;

public sealed partial class GhosttyTerminal
{
    /// <summary>Copies raw title bytes without decoding. A short destination is untouched; requiredLength receives the needed size.</summary>
    public bool TryCopyTitle(Span<byte> destination, out int requiredLength)
        => TryCopyMetadata(GhosttyVtNative.GhosttyTerminalData.Title, destination, out requiredLength);

    /// <summary>Copies raw working-directory bytes. A short destination is untouched; requiredLength receives the needed size.</summary>
    public bool TryCopyWorkingDirectory(Span<byte> destination, out int requiredLength)
        => TryCopyMetadata(GhosttyVtNative.GhosttyTerminalData.Pwd, destination, out requiredLength);

    /// <summary>Sets arbitrary title bytes, without UTF-8 conversion or protocol length limits. Empty clears the title.</summary>
    public void SetTitleBytes(ReadOnlySpan<byte> title)
        => SetMetadata(GhosttyVtNative.GhosttyTerminalOption.Title, title);

    /// <summary>Sets arbitrary working-directory bytes, without URI/UTF-8 conversion. Empty clears the value.</summary>
    public void SetWorkingDirectoryBytes(ReadOnlySpan<byte> directory)
        => SetMetadata(GhosttyVtNative.GhosttyTerminalOption.Pwd, directory);

    private unsafe bool TryCopyMetadata(GhosttyVtNative.GhosttyTerminalData data, Span<byte> destination, out int requiredLength)
    {
        GhosttyVtNative.GhosttyString value = GetValue<GhosttyVtNative.GhosttyString>(data);
        requiredLength = checked((int)value.Len);
        if (destination.Length < requiredLength) return false;
        new ReadOnlySpan<byte>((void*)value.Ptr, requiredLength).CopyTo(destination);
        return true;
    }

    private unsafe void SetMetadata(GhosttyVtNative.GhosttyTerminalOption option, ReadOnlySpan<byte> bytes)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        fixed (byte* data = bytes)
        {
            GhosttyVtNative.GhosttyString value = new((nint)data, (nuint)bytes.Length);
            ThrowIfFailed(GhosttyVtNative.TerminalSet(_handle, option, &value), "ghostty_terminal_set(metadata)");
        }
    }
}
