// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Diagnostics;
using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class ManagedHistoryCompressionTests(ITestOutputHelper output)
{
    [Fact]
    public void RowCompressionPreservesEveryFieldAndCopyOnWriteOwnership()
    {
        TerminalRow row = new(128) { SemanticPrompt = TerminalSemanticPrompt.Prompt, WrapsToNext = true, IsWrapContinuation = true };
        for (int i = 0; i < row.Columns; i++)
            row[i] = new TerminalCell
            {
                Codepoint = 0x1f600 + i, Grapheme = i % 3 == 0 ? "e\u0301😀\ud800" : i % 3 == 1 ? "" : null,
                Foreground = 0xff123456, Background = 0xff456789,
                ForegroundIdentity = TerminalColorIdentity.Palette(3), BackgroundIdentity = TerminalColorIdentity.Rgb(0x456789),
                UnderlineIdentity = TerminalColorIdentity.Palette(4), UnderlineColor = 0xffabcdef,
                HyperlinkId = 12, Attributes = (CellAttributes)i, UnderlineStyle = TerminalUnderlineStyle.Curly,
                HasUnderlineColor = true, Decorations = CellDecorations.Overline, HasBackground = true,
                Width = (byte)(i % 3), IsWideSpacerHead = i % 2 == 0, IsProtected = true,
                SemanticContent = TerminalSemanticContent.Input,
            };
        TerminalRow original = row.CreateStateCopy();
        Assert.True(row.TryCompressCells());
        Assert.True(row.IsCompressed);
        TerminalRow copy = row.CreateStateCopy();
        Assert.Equal(row.RenderId, copy.RenderId);
        Assert.True(row.IsCompressed);
        Assert.True(copy.IsCompressed);
        TerminalRow editable = row.CreateStateCopy();
        editable[0].Codepoint = 'Y';
        Assert.True(row.IsCompressed);
        Assert.Equal('Y', editable.ReadOnlyCells[0].Codepoint);
        Assert.Equal(row.LogicalCellBytes, original.LogicalCellBytes);
        Assert.True(row.ResidentCellBytes < row.LogicalCellBytes);
        Assert.Equal(128, row.PreservedColumns);
        Assert.True(row.IsCompressed); // Metadata reads never inflate.
        Assert.Equal(original.ReadOnlyCells.ToArray(), copy.ReadOnlyCells.ToArray());
        Assert.False(copy.IsCompressed);
        Assert.True(row.IsCompressed);
        copy[0].Codepoint = 'X';
        Assert.Equal(original.ReadOnlyCells.ToArray(), row.ReadOnlyCells.ToArray());
        Assert.Equal(TerminalSemanticPrompt.Prompt, row.SemanticPrompt);
        Assert.True(row.WrapsToNext);
        Assert.True(row.IsWrapContinuation);
        Assert.Equal(0UL, row.CompressedCellBytes);
        Assert.False(new TerminalRow(0).TryCompressCells());
    }

    [Fact]
    public void IncrementalSnapshotCompressionSavesMemoryAndReadsRestoreOnlyAccessedRows()
    {
        byte[] bytes = CreateSnapshot(1200);
        // Warm both paths before recording a diagnostic comparison, without timing assertions.
        using (ManagedTerminalSnapshot warm = ManagedTerminalSnapshot.Restore(bytes, new() { CompressHistory = true })) { }
        long start = Stopwatch.GetTimestamp();
        using ManagedTerminalSnapshot plain = ManagedTerminalSnapshot.Restore(bytes);
        TimeSpan plainTime = Stopwatch.GetElapsedTime(start);
        start = Stopwatch.GetTimestamp();
        using ManagedTerminalSnapshot compact = ManagedTerminalSnapshot.Restore(bytes, new() { CompressHistory = true });
        TimeSpan compactTime = Stopwatch.GetElapsedTime(start);
        TerminalMemoryUsage expected = plain.Processor.GetMemoryUsage(), compressed = compact.Processor.GetMemoryUsage();
        Assert.Equal(TerminalMemoryStorageKind.ManagedRows, compressed.StorageKind);
        Assert.True(compressed.CompressionSupported);
        Assert.Equal(expected.Primary.Units, compressed.Primary.Units);
        Assert.Equal(expected.Primary.LogicalBytes, compressed.Primary.LogicalBytes);
        Assert.True(compressed.Primary.CompressedUnits > 100);
        Assert.True(compressed.Primary.ResidentBytes < expected.Primary.ResidentBytes / 2);
        Assert.Equal(compressed, compact.Processor.GetMemoryUsage());
        output.WriteLine($"Rows={compressed.Primary.Units}, uncompressed={expected.Primary.ResidentBytes} B, compressed resident={compressed.Primary.ResidentBytes} B, encoded={compressed.Primary.CompressedBytes} B; decode plain={plainTime.TotalMilliseconds:F2} ms, compressed={compactTime.TotalMilliseconds:F2} ms.");

        TerminalScreen held = compact.Screen.CreateStateCopy();
        Assert.Equal(compressed, compact.Processor.GetMemoryUsage());
        Assert.True(held.GetRow(0).IsCompressed);
        Assert.Equal(plain.Screen.GetRow(0).ReadOnlyCells.ToArray(), compact.Screen.GetRow(0).ReadOnlyCells.ToArray());
        Assert.Equal(compressed.Primary.CompressedUnits - 1, compact.Processor.GetMemoryUsage().Primary.CompressedUnits);
        Assert.True(held.GetRow(0).IsCompressed);
        compact.Screen.GetRow(0)[0].Codepoint = 'X';
        Assert.Equal(plain.Screen.GetRow(0).ReadOnlyCells[0].Codepoint, held.GetRow(0).ReadOnlyCells[0].Codepoint);

        using ManagedTerminalSnapshot reread = ManagedTerminalSnapshot.Restore(bytes, new() { CompressHistory = true });
        Assert.True(plain.Processor.TryExportSnapshot(TerminalSnapshotExportFormat.StyledVt, new(), out string before));
        Assert.True(reread.Processor.TryExportSnapshot(TerminalSnapshotExportFormat.StyledVt, new(), out string after));
        Assert.Equal(before, after);
        // Reflow, metadata rebuilding and output remain ordinary row consumers.
        plain.Processor.ResizeScreen(53, 20, 530, 400);
        reread.Processor.ResizeScreen(53, 20, 530, 400);
        plain.Processor.Process("\u001b[0mNEXT"u8); reread.Processor.Process("\u001b[0mNEXT"u8);
        Assert.Equal(plain.Screen.TotalRows, reread.Screen.TotalRows);
        for (int i = 0; i < plain.Screen.TotalRows; i++)
            Assert.Equal(plain.Screen.GetRow(i).ReadOnlyCells.ToArray(), reread.Screen.GetRow(i).ReadOnlyCells.ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompressionKeepsHistoryAdmissionAndOutputHoldSemantics(bool hold)
    {
        using ManagedTerminalSnapshotDecoder decoder = new(CreateSnapshot(300), new() { CompressHistory = true });
        using ManagedTerminalSnapshot terminal = decoder.Ready();
        Assert.Equal(0UL, terminal.Processor.GetMemoryUsage().Primary.CompressedUnits);
        if (hold) terminal.Processor.Process("\u001b[?2026h"u8);
        ManagedTerminalSnapshotProgress progress = decoder.Next()!.Value;
        Assert.True(progress.RowsApplied > 0);
        Assert.True(terminal.Processor.GetMemoryUsage().Primary.CompressedUnits > 0);
        if (hold) terminal.Processor.Process("\u001b[?2026l"u8);
        terminal.Screen.ScrollbackLimit = 0;
        while (decoder.Next() is { } next) Assert.Equal(0, next.RowsApplied);
        terminal.Processor.Process("\u001bcOK"u8);
        Assert.Equal(0UL, terminal.Processor.GetMemoryUsage().Primary.CompressedUnits);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BackendMemoryQueriesTrackBothScreensAndKittyStorage(bool native)
    {
        if (native && !GhosttyVtProcessor.IsAvailable()) return;
        using IVtProcessor processor = native ? new GhosttyVtProcessor(new(80, 24)) : new BasicVtProcessor(new(80, 24));
        ITerminalMemoryUsageSource source = Assert.IsAssignableFrom<ITerminalMemoryUsageSource>(processor);
        TerminalMemoryUsage initial = source.GetMemoryUsage();
        Assert.Equal(native ? TerminalMemoryStorageKind.NativePages : TerminalMemoryStorageKind.ManagedRows, initial.StorageKind);
        Assert.True(initial.Primary.Units > 0);
        Assert.True(initial.Primary.ResidentBytes > 0);
        Assert.Equal(0UL, initial.Alternate.Units);
        processor.Process("\u001b_Gf=32,s=1,v=1,i=1;AQIDBA==\u001b\\\u001b[?47hALT"u8);
        TerminalMemoryUsage both = source.GetMemoryUsage();
        Assert.True(both.Alternate.Units > 0);
        Assert.Equal(4UL, both.Primary.ImageBytes);
        Assert.Equal(0UL, both.Alternate.ImageBytes);
        Assert.Equal(both, source.GetMemoryUsage());
    }

    private static byte[] CreateSnapshot(int lines)
    {
        using BasicVtProcessor source = new(new TerminalScreen(80, 24, 2000));
        for (int i = 0; i < lines; i++)
            source.Process(Encoding.UTF8.GetBytes($"\u001b]8;id=item;https://example.com/{i % 7}\u001b\\\u001b[1;38;5;42;48;2;12;34;56mBuild {i:D5}: 界e\u0301😀 ready\u001b]8;;\u001b\\\r\n"));
        return source.GetBinarySnapshot();
    }
}
