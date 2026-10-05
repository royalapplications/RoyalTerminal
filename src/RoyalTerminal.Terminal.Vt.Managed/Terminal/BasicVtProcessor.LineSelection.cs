// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Avalonia.Rendering;

namespace RoyalTerminal.Terminal;

public sealed partial class BasicVtProcessor
{
    /// <inheritdoc />
    public bool TryGetLineExtent(TerminalGridPosition position, ReadOnlySpan<uint> whitespace,
        bool semanticPromptBoundary, out TerminalLineExtent extent)
    {
        _screen.ThrowIfSnapshotMutationFailed();
        return TerminalLineSelection.TryResolve(_screen, position, whitespace, semanticPromptBoundary, out extent);
    }
}
