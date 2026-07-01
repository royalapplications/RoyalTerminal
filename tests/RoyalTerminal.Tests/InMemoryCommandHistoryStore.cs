// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Terminal;

namespace RoyalTerminal.Tests;

internal sealed class InMemoryCommandHistoryStore(TerminalCommandHistoryDocument? document = null) : ITerminalCommandHistoryStore
{
    public TerminalCommandHistoryDocument Document { get; private set; } = document ?? new TerminalCommandHistoryDocument();

    public int SaveCount { get; private set; }

    public ValueTask<TerminalCommandHistoryDocument> LoadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Document);
    }

    public ValueTask SaveAsync(TerminalCommandHistoryDocument document, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Document = document;
        SaveCount++;
        return ValueTask.CompletedTask;
    }
}
