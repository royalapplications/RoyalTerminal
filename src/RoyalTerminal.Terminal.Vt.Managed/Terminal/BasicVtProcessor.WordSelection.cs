// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Avalonia.Rendering;

namespace RoyalTerminal.Terminal;

public sealed partial class BasicVtProcessor
{
    /// <inheritdoc />
    public bool TryGetWordExtent(TerminalGridPosition position, ReadOnlySpan<char> delimiters,
        out TerminalWordExtent extent)
    {
        _screen.ThrowIfSnapshotMutationFailed();
        return TerminalWordSelection.TryResolveAbsolute(_screen, position, delimiters, out extent);
    }

    /// <inheritdoc />
    public string? ReadBufferSelection(in TerminalSelectionRange selection, bool unwrap)
    {
        _screen.ThrowIfSnapshotMutationFailed();
        return ManagedPlainTextFormatter.Format(_screen,
            new TerminalSnapshotExportOptions(Unwrap: unwrap, Selection: selection), absoluteSelection: true);
    }
}
