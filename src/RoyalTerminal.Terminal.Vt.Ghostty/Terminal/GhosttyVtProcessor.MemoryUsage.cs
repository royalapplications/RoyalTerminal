// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.GhosttySharp.Native;

namespace RoyalTerminal.Terminal;

public sealed partial class GhosttyVtProcessor : ITerminalMemoryUsageSource
{
    /// <inheritdoc />
    public TerminalMemoryUsage GetMemoryUsage()
    {
        GhosttyVtNative.GhosttyTerminalMemoryUsage usage = _terminal.GetMemoryUsage();
        return new(TerminalMemoryStorageKind.NativePages, usage.CompressionSupported,
            new(usage.PrimaryPages, usage.PrimaryVirtualBytes, usage.PrimaryResidentBytes,
                usage.PrimaryCompressedPages, usage.PrimaryCompressedBytes, usage.PrimaryImageBytes),
            new(usage.AlternatePages, usage.AlternateVirtualBytes, usage.AlternateResidentBytes,
                usage.AlternateCompressedPages, usage.AlternateCompressedBytes, usage.AlternateImageBytes));
    }
}
