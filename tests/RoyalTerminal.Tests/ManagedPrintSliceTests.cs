// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Snapshots;
using Xunit;

namespace RoyalTerminal.Tests;

// Reference: Ghostty Terminal.printSlice/printRepeat (5b70f208b); Windows Terminal
// AdaptDispatch.PrintString/_WriteToBuffer and xterm.js InputHandler.print/REP.
// Follow Ghostty's last-codepoint REP, not xterm.js's whole-grapheme extension.
// One-byte feeds force the established scalar path without a test-only switch.
public sealed class ManagedPrintSliceTests
{
    public static TheoryData<string> Inputs => new()
    {
        "abcdefghijklmnopqrstuvwxyz0123456789",
        "λλλλλλλλλλλλλλλλλλλλλλλλλλλλλλλλ",
        "界界界界界界界界界界界界界界界界",
        "ABC界界λλxyz🙂🙂👩\u200d💻\u0301Z",
        "1234567界界界\u001b[1;7HABCD",
        "界界界界\u001b[1;2Habcdefghijk",
        "A\u0301B\u0301C\u0301\rabcdefghijk",
        "\u001b[?2027h🇵🇱🇵🇱🇵🇱🇵🇱👍🏽👍🏽각각",
        "\u001b[?2027lA\u0301λλλλ界界界界\u001b[?2027hABC",
        "\u001b[31;1;4:3mabcdefghijklmnop\r\u001b[32;3mqrstuvwxyz012345",
        "\u001b[1\"q\u001b]133;A\aabcdefghijklmnopqrstuvwxyz",
        "\u001b]8;;https://example.test\aABCDEF\u001b]8;;\a\rabcdefghijklmnop",
        "\u001b[?69h\u001b[3;7s\u001b[1;3Habcdefghijklmnopqrst界界界界",
        "\u001b[?69h\u001b[3;7s\u001b[1;8Habcdefghijklmnopqrst",
        "\u001b[2;3r\u001b[2;1Habcdefghijklmnopqrstuvwxyz0123456789",
        "abcdefgh\u001b[?7lXYZabcdefghijklmnop",
        "abcdefg\r\u001b[4h12345界界界\u001b[4lmnop",
        "\u001b(0qqqqqqqqqqqqqqqq\u001b(Babcdefgh",
        "\u001b(0_0`abcdefghijklmnopqrstuvwxyz{|}~\u001b(Bnormal",
        "\u001b(A###abc###éÿ###\u001b(B###",
        "\u001b(0qqq界qqqλqqq\u0301qqq\u001b[1100b",
        "\u001b(0\u001b*A\u001bN#qqqqqqqqqqqqqqqq\u001b[9b",
        "\u001b(A\u001b*0\u001bNq################\u001b[9b",
        "\u001b(0\u001b*A\u001bN界qqqqqqqqqqqqqqqq",
        "\u001b[31;1m\u001b(0qqqqqqqqqqqqqqqqqqqq\r\u001b[32mxxxxxxxxxxxxxxxxxxxx",
        "\u001b[?69h\u001b[3;7s\u001b[1;3H\u001b(0qqqqqqqqqqqqqqqqqqqq",
        "界界界界\r\u001b(0qqqqqqqqqqqqqqqqqqqq",
        "\u001b(0\u001b[?2027hqqqqqqqq\u0301qqqqqqqq",
        "\u001b*A\u001bN#abcdefghijklmnop",
        "\u001b*A\u001bN界界界界界界",
        "\u001b[1$}ignoredλλλλ\u001b[0$}abcdefghijklmnopqrstuvwxyz",
        "\u001b[?1049habcdefghijklmnopqrstuvwxyz界界\u001b[?1049lprimaryABC",
        "\u001b[?2026habcdefghijklmnopqrstuvwxyz\u001b[?2026lXYZ",
        "\u0600λλλλλλλλ\u001b[1;1Hλλλλλλλλ",
    };

    [Theory]
    [MemberData(nameof(Inputs))]
    public void BulkAndScalarHaveIdenticalCellsCursorAndContinuation(string input)
    {
        foreach (int columns in new[] { 1, 2, 8, 17 })
        foreach (bool snapshot in new[] { false, true })
        {
            using BasicVtProcessor seed = new(new TerminalScreen(columns, 4, 100));
            byte[] initial = seed.GetBinarySnapshot();
            using ManagedTerminalSnapshot? bulkSnapshot = snapshot ? ManagedTerminalSnapshot.Restore(initial) : null;
            using ManagedTerminalSnapshot? scalarSnapshot = snapshot ? ManagedTerminalSnapshot.Restore(initial) : null;
            TerminalScreen bulkScreen = bulkSnapshot?.Screen ?? new(columns, 4, 100);
            TerminalScreen scalarScreen = scalarSnapshot?.Screen ?? new(columns, 4, 100);
            using BasicVtProcessor? bulkOwner = snapshot ? null : new(bulkScreen);
            using BasicVtProcessor? scalarOwner = snapshot ? null : new(scalarScreen);
            BasicVtProcessor bulk = bulkSnapshot?.Processor ?? bulkOwner!;
            BasicVtProcessor scalar = scalarSnapshot?.Processor ?? scalarOwner!;
            byte[] bytes = Encoding.UTF8.GetBytes(input);
            bulk.Process(bytes);
            for (int index = 0; index < bytes.Length; index++) scalar.Process(bytes.AsSpan(index, 1));
            AssertManagedEqual(bulk, bulkScreen, scalar, scalarScreen);
            // Exercise pending wrap, the last graphic character and later cluster joins.
            bulk.Process("\u001b[3b\u0301Q"u8); scalar.Process("\u001b[3b\u0301Q"u8);
            AssertManagedEqual(bulk, bulkScreen, scalar, scalarScreen);
        }
    }

    [Theory]
    [InlineData("q")]
    [InlineData("λ")]
    [InlineData("界")]
    [InlineData("🙂")]
    public void RepeatMatchesExplicitScalarWritesAcrossBoundedChunks(string text)
    {
        foreach (int columns in new[] { 1, 8, 80 })
        {
            TerminalScreen actual = new(columns, 4, 1000), expected = new(columns, 4, 1000);
            using BasicVtProcessor repeat = new(actual), scalar = new(expected);
            byte[] graphic = Encoding.UTF8.GetBytes(text);
            repeat.Process(graphic); repeat.Process("\u001b[1100b"u8);
            for (int count = 0; count < 1101; count++)
                for (int index = 0; index < graphic.Length; index++) scalar.Process(graphic.AsSpan(index, 1));
            AssertManagedEqual(repeat, actual, scalar, expected);
        }
    }

    [Fact]
    public void DecoderLeavesInvalidIncompleteAndControlBoundariesToStreamingParser()
    {
        byte[] input = [0xCE, 0xBB, 0x41, 0xF0, 0x80, 0xC2, 0x1B, 0x5B, 0x33, 0x31, 0x6D, 0xCE, 0xBB, 0xE2];
        TerminalScreen expected = new(40, 3);
        using BasicVtProcessor scalar = new(expected);
        for (int index = 0; index < input.Length; index++) scalar.Process(input.AsSpan(index, 1));
        for (int split = 0; split <= input.Length; split++)
        {
            TerminalScreen actual = new(40, 3);
            using BasicVtProcessor bulk = new(actual);
            bulk.Process(input.AsSpan(0, split)); bulk.Process(input.AsSpan(split));
            AssertManagedEqual(bulk, actual, scalar, expected);
        }
    }

    [Fact]
    public void DecoderIgnoresEveryUtf8EncodedC1WithoutChangingPrintOrParserState()
    {
        for (int codepoint = 0x80; codepoint <= 0x9F; codepoint++)
        {
            byte[] input = Encoding.UTF8.GetBytes("λλ" + char.ConvertFromUtf32(codepoint) + "AB\u001b[2b");
            TerminalScreen expected = new(4, 3);
            using BasicVtProcessor scalar = new(expected);
            for (int index = 0; index < input.Length; index++) scalar.Process(input.AsSpan(index, 1));
            for (int split = 0; split <= input.Length; split++)
            {
                TerminalScreen actual = new(4, 3);
                using BasicVtProcessor bulk = new(actual);
                bulk.Process(input.AsSpan(0, split)); bulk.Process(input.AsSpan(split));
                AssertManagedEqual(bulk, actual, scalar, expected);
                Assert.True(bulk.IsParserGround);
            }
        }
    }

    [Theory]
    [InlineData("abcdefghijklmnopqrst")]
    [InlineData("λλλλλλλλλλλλλλλλλλλλ")]
    [InlineData("界界界界界界界界界界")]
    public void RowTemplateDetachesCowStorageOnceAndPreservesHeldMetadata(string input)
    {
        using BasicVtProcessor seed = new(new(80, 3));
        using ManagedTerminalSnapshot terminal = ManagedTerminalSnapshot.Restore(seed.GetBinarySnapshot());
        terminal.Processor.Process("\u001b[31;1moriginal\r\u001b[32;3m"u8);
        TerminalScreen held = terminal.Screen.CreateStateCopy();
        TerminalCell[] retained = held.GetViewportRow(0).ReadOnlyCells.ToArray();
        TerminalRow row = terminal.Screen.GetViewportRow(0);
        ulong revision = row.SnapshotMetadataRevision;
        terminal.Processor.Process(Encoding.UTF8.GetBytes(input));
        Assert.Equal(retained, held.GetViewportRow(0).ReadOnlyCells.ToArray());
        Assert.NotSame(row.SearchStorageIdentity, held.GetViewportRow(0).SearchStorageIdentity);
        Assert.Same(terminal.Screen.GetViewportRow(1).SearchStorageIdentity, held.GetViewportRow(1).SearchStorageIdentity);
        // A single writable span, not erase/write/indexer scopes per character.
        Assert.InRange(row.SnapshotMetadataRevision - revision, 1UL, 2UL);
        using ManagedTerminalSnapshot restored = ManagedTerminalSnapshot.Restore(terminal.Processor.GetBinarySnapshot());
        for (int column = 0; column < row.Columns; column++)
        {
            TerminalCell expected = row.ReadOnlyCells[column], actual = restored.Screen.GetViewportRow(0).ReadOnlyCells[column];
            Assert.Equal(expected.Codepoint, actual.Codepoint);
            Assert.Equal(expected.Width, actual.Width);
            Assert.Equal(GhosttySnapshotLivePage.EncodeStyle(in expected), GhosttySnapshotLivePage.EncodeStyle(in actual));
            Assert.Equal(expected.Foreground, actual.Foreground);
            Assert.Equal(expected.Background, actual.Background);
            // PAGE stores color identity, not the renderer's derived HasBackground.
            Assert.Equal(expected.BackgroundIdentity, actual.BackgroundIdentity);
        }
    }

    [Theory]
    [MemberData(nameof(Inputs))]
    public void NativeDifferentialChecksWholeBufferPrinting(string input)
    {
        if (!GhosttyVtProcessor.IsAvailable())
        {
            Assert.NotEqual("1", Environment.GetEnvironmentVariable("ROYALTERMINAL_REQUIRE_NATIVE_TESTS"));
            Assert.Skip("Native VT library is unavailable.");
        }
        TerminalScreen actual = new(8, 4, 100), expected = new(8, 4, 100);
        using BasicVtProcessor managed = new(actual);
        using GhosttyVtProcessor native = new(expected);
        byte[] bytes = Encoding.UTF8.GetBytes(input);
        managed.Process(bytes); native.Process(bytes);
        Assert.Equal((native.CursorRow, native.CursorCol), (managed.CursorRow, managed.CursorCol));
        for (int row = 0; row < 4; row++)
        for (int column = 0; column < 8; column++)
        {
            TerminalCell e = expected.GetViewportRow(row).ReadOnlyCells[column];
            TerminalCell a = actual.GetViewportRow(row).ReadOnlyCells[column];
            Assert.Equal((e.Codepoint, e.Width, e.Grapheme, e.IsProtected, e.IsWideSpacerHead),
                (a.Codepoint, a.Width, a.Grapheme, a.IsProtected, a.IsWideSpacerHead));
            if (e.HasContent)
            {
                Assert.Equal((e.ForegroundIdentity, e.BackgroundIdentity, e.Attributes, e.UnderlineStyle, e.SemanticContent),
                    (a.ForegroundIdentity, a.BackgroundIdentity, a.Attributes, a.UnderlineStyle, a.SemanticContent));
                expected.TryGetHyperlinkUrl(e.HyperlinkId, out string? eUrl);
                actual.TryGetHyperlinkUrl(a.HyperlinkId, out string? aUrl);
                Assert.Equal(eUrl, aUrl);
            }
        }
    }

    [Theory]
    [InlineData(7)]
    [InlineData(31)]
    [InlineData(2027)]
    public void MixedEditsResizeAndRetainedSnapshotsMatchScalarAtEveryCheckpoint(int seed)
    {
        using BasicVtProcessor initial = new(new(17, 4, 40));
        byte[] snapshot = initial.GetBinarySnapshot();
        using ManagedTerminalSnapshot bulk = ManagedTerminalSnapshot.Restore(snapshot);
        using ManagedTerminalSnapshot scalar = ManagedTerminalSnapshot.Restore(snapshot);
        Random random = new(seed);
        string[] commands = ["\r", "\n", "\u001b[H", "\u001b[2;3H", "\u001b[3;1H", "\u001b[31;1m",
            "\u001b[0m", "\u001b[32;3m", "\u001b[2K", "\u001b[3P", "\u001b[2@", "\u001b[L", "\u001b[M",
            "\u001b[?7l", "\u001b[?7h", "\u001b[?2027l", "\u001b[?2027h", "\u001b[2J", "\u001b[3J",
            "\u001b]8;;https://example.test\a", "\u001b]8;;\a", "\u001b[1\"q", "\u001b[0\"q"];
        string[] runs = ["abcde", "λλλλλλλλ", "界界界界", "🙂🙂🙂", "A\u0301B\u0301", "🇵🇱🇵🇱", "각", "\u0600λ", "xyz0123456789"];
        for (int step = 0; step < 300; step++)
        {
            TerminalScreen held = bulk.Screen.CreateStateCopy();
            TerminalCell[][] heldCells = Enumerable.Range(0, held.TotalRows).Select(row => held.GetRow(row).ReadOnlyCells.ToArray()).ToArray();
            byte[] input = Encoding.UTF8.GetBytes(commands[random.Next(commands.Length)] + runs[random.Next(runs.Length)]);
            bulk.Processor.Process(input);
            for (int index = 0; index < input.Length; index++) scalar.Processor.Process(input.AsSpan(index, 1));
            if (step % 19 == 0)
            {
                int columns = random.Next(1, 26);
                bool reflow = step % 2 == 0;
                bulk.Processor.ResizeScreen(columns, 4, 0, 0, reflowOnResize: reflow);
                scalar.Processor.ResizeScreen(columns, 4, 0, 0, reflowOnResize: reflow);
            }
            AssertManagedEqual(bulk.Processor, bulk.Screen, scalar.Processor, scalar.Screen);
            for (int row = 0; row < held.TotalRows; row++) Assert.Equal(heldCells[row], held.GetRow(row).ReadOnlyCells.ToArray());
        }
    }

    [Fact]
    public void RowBatchClearsOverlappingRasterGraphicsAndHiddenResizeStorage()
    {
        TerminalScreen actual = new(20, 4), expected = new(20, 4);
        using BasicVtProcessor bulk = new(actual), scalar = new(expected);
        bulk.SixelGraphicsEnabled = scalar.SixelGraphicsEnabled = true;
        bulk.NotifyResize(20, 4, 200, 40); scalar.NotifyResize(20, 4, 200, 40);
        byte[] setup = "01234567890123456789\r\u001bPq#1;2;100;0;0#1~~~\u001b\\\u001b[1;1H"u8.ToArray();
        bulk.Process(setup); scalar.Process(setup);
        Assert.True(actual.HasRasterGraphics);
        actual.Resize(8, 4, reflowOnResize: false); expected.Resize(8, 4, reflowOnResize: false);
        byte[] bytes = "abcdefgh"u8.ToArray();
        bulk.Process(bytes);
        for (int index = 0; index < bytes.Length; index++) scalar.Process(bytes.AsSpan(index, 1));
        AssertManagedEqual(bulk, actual, scalar, expected);
        Assert.Equal(expected.HasRasterGraphics, actual.HasRasterGraphics);
        Assert.False(actual.HasRasterGraphics);
        for (int row = 0; row < 4; row++)
            Assert.Equal(expected.GetViewportRow(row).ReadOnlyPreservedCells.ToArray(), actual.GetViewportRow(row).ReadOnlyPreservedCells.ToArray());
    }

    private static void AssertManagedEqual(BasicVtProcessor actual, TerminalScreen actualScreen,
        BasicVtProcessor expected, TerminalScreen expectedScreen)
    {
        Assert.Equal((expected.CursorRow, expected.CursorCol), (actual.CursorRow, actual.CursorCol));
        Assert.Equal(expected.GetContinuation(), actual.GetContinuation());
        Assert.Equal(expectedScreen.TotalRows, actualScreen.TotalRows);
        for (int row = 0; row < expectedScreen.TotalRows; row++)
        {
            TerminalRow e = expectedScreen.GetRow(row), a = actualScreen.GetRow(row);
            Assert.Equal((e.WrapsToNext, e.IsWrapContinuation, e.SemanticPrompt), (a.WrapsToNext, a.IsWrapContinuation, a.SemanticPrompt));
            Assert.Equal(e.ReadOnlyCells.ToArray(), a.ReadOnlyCells.ToArray());
            Assert.Equal(e.SnapshotAllocation?.Capacity, a.SnapshotAllocation?.Capacity);
        }
    }
}
