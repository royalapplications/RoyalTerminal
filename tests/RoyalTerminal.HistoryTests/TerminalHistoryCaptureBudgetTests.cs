// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.HistoryTests;

public sealed class TerminalHistoryCaptureBudgetTests
{
    [Fact]
    public void VisitsOnlyBudgetedRowsAndOneRejectedRow()
    {
        var info = new TerminalHistoryBufferInfo(Guid.NewGuid(), Guid.NewGuid(), false, 80, 2, new(123, 1_000_000));
        var reader = new CountingReader();
        Assert.Equal(TerminalHistoryStatus.Success, TerminalHistoryCapture.Capture(reader, info,
            new(4, 4), out var tail, CancellationToken.None));
        Assert.Equal(new[] { 999_999, 999_998, 999_997 }, reader.Visited);
        Assert.Equal(2, tail!.Rows.Length);
        Assert.Equal(TerminalHistoryTruncation.RowLimit | TerminalHistoryTruncation.CharacterLimit, tail.Truncation);
        reader.Visited.Clear();
        TerminalHistoryCapture.Capture(reader, info, new(2, 100, info, info.AvailableRange), out var prefix, CancellationToken.None);
        Assert.Equal(new[] { 0, 1 }, reader.Visited);
        Assert.Equal(123, prefix!.CapturedRange.Start);
    }

    [Fact]
    public void CancellationBetweenRowsNeverReturnsPartialSnapshot()
    {
        using var cancellation = new CancellationTokenSource();
        var reader = new CountingReader { OnRead = cancellation.Cancel };
        var info = new TerminalHistoryBufferInfo(Guid.NewGuid(), Guid.NewGuid(), false, 80, 2, new(0, 100));
        Assert.Throws<OperationCanceledException>(() => TerminalHistoryCapture.Capture(reader, info,
            new(100, 1000), out _, cancellation.Token));
        Assert.Single(reader.Visited);
    }

    private sealed class CountingReader : ITerminalHistoryRowReader
    {
        internal readonly List<int> Visited = [];
        internal Action? OnRead;
        public bool TryReadHistoryRow(int row, int maxCharacters, CancellationToken cancellationToken,
            out TerminalHistoryRow? result)
        {
            Visited.Add(row);
            OnRead?.Invoke();
            result = maxCharacters >= 1 ? new("x", false) : null;
            return result is not null;
        }
    }
}
