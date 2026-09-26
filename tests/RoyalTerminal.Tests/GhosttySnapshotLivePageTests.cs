// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal.Snapshots;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class GhosttySnapshotLivePageTests
{
    [Fact]
    public void LiveCellsPreserveStylesRawLinksMetadataAndIndependentWideTail()
    {
        TerminalScreen owner = new(5, 1);
        int first = owner.RegisterHyperlink([0xFF, 0xFE], [0x80], 0);
        int second = owner.RegisterHyperlink([0xFF, 0xFE], [], 17);
        int third = owner.RegisterHyperlink([0xFF, 0xFE], [], 18);
        TerminalRow row = new(5)
        {
            WrapsToNext = true, IsWrapContinuation = true,
            SemanticPrompt = TerminalSemanticPrompt.PromptContinuation,
        };
        TerminalCell wide = TerminalCell.Empty();
        wide.Codepoint = '界'; wide.Width = 2; wide.IsProtected = true;
        wide.SemanticContent = TerminalSemanticContent.Prompt;
        wide.ForegroundIdentity = TerminalColorIdentity.Palette(42);
        wide.BackgroundIdentity = TerminalColorIdentity.Rgb(0x123456);
        wide.UnderlineIdentity = TerminalColorIdentity.Rgb(0x987654);
        wide.HasUnderlineColor = true; wide.UnderlineStyle = TerminalUnderlineStyle.Curly;
        wide.Attributes = (CellAttributes)255; wide.Decorations = CellDecorations.Overline;
        wide.HyperlinkId = first;
        row[0] = wide;
        TerminalCell tail = TerminalCell.Empty();
        tail.Width = 0; tail.BackgroundIdentity = TerminalColorIdentity.Palette(0);
        tail.SemanticContent = TerminalSemanticContent.Input; tail.HyperlinkId = second;
        row[1] = tail;
        TerminalCell grapheme = TerminalCell.Empty();
        grapheme.Codepoint = 'a'; grapheme.Grapheme = "a\u0301"; grapheme.HyperlinkId = third;
        row[2] = grapheme; row[3] = grapheme;
        TerminalCell spacer = TerminalCell.Empty(); spacer.Width = 0; spacer.IsWideSpacerHead = true;
        row[4] = spacer;

        GhosttySnapshotPage page = Reframe(GhosttySnapshotLivePage.Capture([row], owner, 5));
        Assert.Equal(3, page.HyperlinkCount);
        Assert.Equal(2, page.StyleCount);
        TerminalScreen restoredOwner = new(20, 1);
        // Existing identities must not collide with page-local IDs.
        restoredOwner.RegisterHyperlink("unrelated"u8, [], 1);
        TerminalRow restored = Assert.Single(GhosttySnapshotLivePage.Decode(page, restoredOwner));
        Assert.Equal(5, restored.Columns);
        Assert.True(restored.WrapsToNext); Assert.True(restored.IsWrapContinuation);
        Assert.Equal(row.SemanticPrompt, restored.SemanticPrompt);
        for (int column = 0; column < 5; column++)
        {
            TerminalCell expected = row[column], actual = restored[column];
            Assert.Equal(expected.Codepoint, actual.Codepoint);
            Assert.Equal(expected.Grapheme, actual.Grapheme);
            Assert.Equal(expected.Width, actual.Width);
            Assert.Equal(expected.IsWideSpacerHead, actual.IsWideSpacerHead);
            Assert.Equal(expected.IsProtected, actual.IsProtected);
            Assert.Equal(expected.SemanticContent, actual.SemanticContent);
            Assert.Equal(expected.Attributes, actual.Attributes);
            Assert.Equal(expected.Decorations, actual.Decorations);
            Assert.Equal(expected.ForegroundIdentity, actual.ForegroundIdentity);
            Assert.Equal(expected.BackgroundIdentity, actual.BackgroundIdentity);
            Assert.Equal(expected.UnderlineIdentity, actual.UnderlineIdentity);
            Assert.Equal(expected.UnderlineStyle, actual.UnderlineStyle);
            if (expected.HyperlinkId == 0) { Assert.Equal(0, actual.HyperlinkId); continue; }
            Assert.True(owner.TryGetHyperlink(expected.HyperlinkId, out TerminalHyperlink? a));
            Assert.True(restoredOwner.TryGetHyperlink(actual.HyperlinkId, out TerminalHyperlink? b));
            Assert.Equal(a!.UriBytes.ToArray(), b!.UriBytes.ToArray());
            Assert.Equal(a.ExplicitId.ToArray(), b.ExplicitId.ToArray());
            Assert.Equal(a.ImplicitId, b.ImplicitId);
        }
        Assert.Equal(0xFF123456u, restored[0].Background);
        Assert.Equal(restoredOwner.Theme.Palette[42], restored[0].Foreground);
        Assert.Equal(0xFF987654u, restored[0].UnderlineColor);
        Assert.NotEqual(restored[1].HyperlinkId, restored[2].HyperlinkId);
        Assert.Equal(restored[2].HyperlinkId, restored[3].HyperlinkId);
        page.WritePayloadTo(Stream.Null);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) page.WritePayloadTo(Stream.Null);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Theory]
    [InlineData(2, 42u, 42u)]
    [InlineData(3, 0x563412u, 0x123456u)]
    [InlineData(3, 0u, 0u)]
    public void InlineBackgroundOverridesStyleWithoutTurningIntoText(int kind, uint content, uint expected)
    {
        GhosttySnapshotStyle style = new(default, new(2, 255, 0, 0), default, 1);
        GhosttySnapshotGrid grid = GhosttySnapshotGrid.FromOwnedCells(1, [0],
            [(uint)kind | ((ulong)content << 2) | (1UL << 26)], []);
        GhosttySnapshotPage page = GhosttySnapshotPage.FromOwnedGrid(grid, new() { [1] = style }, []);
        TerminalScreen owner = new(1, 1);
        TerminalRow row = Assert.Single(GhosttySnapshotLivePage.Decode(Reframe(page), owner));
        Assert.Equal(0, row[0].Codepoint);
        Assert.True(row[0].HasBackground);
        Assert.Equal(CellAttributes.Bold, row[0].Attributes);
        Assert.Equal(kind == 2 ? TerminalColorIdentity.Palette((byte)expected) : TerminalColorIdentity.Rgb(expected),
            row[0].BackgroundIdentity);
        Assert.Equal(kind == 2 ? owner.Theme.Palette[(int)expected] : 0xFF000000 | expected, row[0].Background);
        TerminalRow again = Assert.Single(GhosttySnapshotLivePage.Decode(
            Reframe(GhosttySnapshotLivePage.Capture([row], owner, 1)), owner));
        Assert.Equal(row[0].BackgroundIdentity, again[0].BackgroundIdentity);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(63)]
    [InlineData(64)]
    [InlineData(65)]
    [InlineData(200)]
    public void SupplementarySuffixesStayLosslessOnWireButBoundedInLiveCells(int suffixCount)
    {
        TerminalScreen owner = new(1, 1);
        TerminalRow row = new(1);
        TerminalCell cell = TerminalCell.Empty();
        cell.Codepoint = 0x1F600;
        cell.Grapheme = "😀" + string.Concat(Enumerable.Repeat("\U000E0100", suffixCount));
        row[0] = cell;
        GhosttySnapshotPage page = Reframe(GhosttySnapshotLivePage.Capture([row], owner, 1));
        Assert.Equal(suffixCount, page.Grid.Suffix(0, 0).Length);
        string expected = "😀" + string.Concat(Enumerable.Repeat("\U000E0100", Math.Min(suffixCount, 64)));
        Assert.Equal(expected, GhosttySnapshotLivePage.Decode(page, owner)[0][0].Grapheme);
        Assert.Equal(suffixCount, Reframe(page).Grid.Suffix(0, 0).Length);
    }

    [Theory]
    [InlineData("wrong")]
    [InlineData("a\u0000")]
    public void InvalidLiveGraphemesAreRejected(string grapheme)
    {
        TerminalRow row = new(1);
        TerminalCell cell = TerminalCell.Empty(); cell.Codepoint = 'a'; cell.Grapheme = grapheme; row[0] = cell;
        Assert.Throws<InvalidDataException>(() => GhosttySnapshotLivePage.Capture([row], new(1, 1), 1));
        Assert.Equal(grapheme, row[0].Grapheme);
    }

    [Fact]
    public void InvalidUtf16IsRejectedWithoutTestAttributeStringNormalization()
        => InvalidLiveGraphemesAreRejected(new string(['a', '\uD800']));

    [Theory]
    [InlineData(0)] // Orphan wide tail.
    [InlineData(1)] // Wide head without its tail.
    [InlineData(2)] // Spacer head not at the wrapped right edge.
    [InlineData(3)] // Unknown decoration.
    [InlineData(4)] // Grapheme on an empty cell.
    [InlineData(5)] // Missing hyperlink.
    public void InvalidLiveCellMetadataIsRejected(int variant)
    {
        TerminalRow row = new(1);
        TerminalCell cell = TerminalCell.Empty();
        switch (variant)
        {
            case 0: cell.Width = 0; break;
            case 1: cell.Width = 2; break;
            case 2: cell.Width = 0; cell.IsWideSpacerHead = true; break;
            case 3: cell.Decorations = (CellDecorations)2; break;
            case 4: cell.Grapheme = "\0a"; break;
            case 5: cell.HyperlinkId = 42; break;
        }
        row[0] = cell;
        Assert.Throws<InvalidDataException>(() => GhosttySnapshotLivePage.Capture([row], new(1, 1), 1));
    }

    [Fact]
    public void NativeHyperlinkBudgetRequiresPageSplitInsteadOfSilentDataLoss()
    {
        TerminalScreen owner = new(900, 1);
        TerminalRow row = new(900);
        for (int column = 0; column < row.Columns; column++)
        {
            TerminalCell cell = TerminalCell.Empty(); cell.Codepoint = 'x';
            cell.HyperlinkId = owner.RegisterHyperlink("link"u8, [], (uint)column);
            row[column] = cell;
        }
        Assert.Throws<InvalidDataException>(() => GhosttySnapshotLivePage.Capture([row], owner, 900));
    }

    [Fact]
    public void InvalidGeometryAndBudgetsFailBeforeCellAllocation()
    {
        TerminalScreen owner = new(1, 1);
        Assert.Throws<InvalidDataException>(() => GhosttySnapshotLivePage.Capture([], owner, 1));
        Assert.Throws<InvalidDataException>(() => GhosttySnapshotLivePage.Capture([new(0)], owner, 1));
        Assert.Throws<InvalidDataException>(() => GhosttySnapshotLivePage.Capture([new(2), new(1)], owner, 4));
        Assert.Throws<InvalidDataException>(() => GhosttySnapshotLivePage.Capture([new(2)], owner, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => GhosttySnapshotLivePage.Capture([new(1)], owner, -1));
    }

    private static GhosttySnapshotPage Reframe(GhosttySnapshotPage page)
    {
        using MemoryStream stream = new();
        page.WritePayloadTo(stream);
        return GhosttySnapshotPage.Read(stream.ToArray(), 1_000_000, 1_000_000, 1_000_000);
    }
}
