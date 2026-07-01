// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Terminal;

namespace RoyalTerminal.Tests;

internal sealed class InMemoryWorkspaceStore(TerminalWorkspaceDocument? document = null) : ITerminalWorkspaceStore
{
    public TerminalWorkspaceDocument Document { get; private set; } = document ?? new TerminalWorkspaceDocument();

    public int SaveCount { get; private set; }

    public ValueTask<TerminalWorkspaceDocument> LoadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Document);
    }

    public ValueTask SaveAsync(TerminalWorkspaceDocument document, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Document = document;
        SaveCount++;
        return ValueTask.CompletedTask;
    }
}
