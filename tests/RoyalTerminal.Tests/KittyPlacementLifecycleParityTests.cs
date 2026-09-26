// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

/// <summary>Public-engine comparisons of Ghostty's pin-based placement lifecycle.</summary>
public sealed class KittyPlacementLifecycleParityTests
{
    public static IEnumerable<object[]> TallPlacements()
    {
        foreach (bool native in new[] { false, true })
        {
            yield return [native, "", 1, 1, uint.MaxValue, uint.MaxValue, 10, 4];
            yield return [native, "", 3, 1, uint.MaxValue, uint.MaxValue, 10, 4];
            yield return [native, "", 5, 1, uint.MaxValue, uint.MaxValue, 10, 4];
            yield return [native, "", 1, 1, 5u, 8u, 9, 4];
            yield return [native, "", 1, 1, 4u, 8u, 8, 4];
            yield return [native, "\x1b[1;3r", 2, 1, uint.MaxValue, uint.MaxValue, 10, 2];
            yield return [native, "\x1b[2;4r", 2, 1, uint.MaxValue, uint.MaxValue, 5, 3];
            yield return [native, "\x1b[?69h\x1b[2;4s", 1, 1, uint.MaxValue, uint.MaxValue, 5, 4];
        }
    }

    [Theory]
    [MemberData(nameof(TallPlacements))]
    public void CursorMovementReachesMarginThenScrollsAtMostOneScreen(bool native, string margins,
        int row, int column, uint columns, uint rows, int totalRows, int cursorRow)
    {
        using Session session = new(native);
        session.Write($"{margins}\x1b[{row};{column}H");
        session.Send($"a=T,i=1,s=1,v=1,c={columns},r={rows};AQIDBA==");
        Assert.Equal(totalRows, session.TotalRows);
        Assert.Equal(cursorRow, session.Processor.CursorRow);
        Assert.Equal(columns >= 5 ? 0 : 4, session.Processor.CursorCol);
    }

    public static IEnumerable<object[]> RelativeSelectors()
    {
        foreach (bool native in new[] { false, true })
            foreach (string selector in new[] { "p,x=3,y=3", "P,x=3,y=3", "q,x=3,y=3,z=5",
                         "Q,x=3,y=3,z=5", "x,x=3", "X,x=3", "y,y=3", "Y,y=3", "c", "C" })
                yield return [native, selector];
    }

    [Theory]
    [MemberData(nameof(RelativeSelectors))]
    public void GeometricDeletesDoNotDirectlySelectRelativePlacement(bool native, string selector)
    {
        using Session session = new(native);
        session.Send("a=T,i=1,p=1,s=1,v=1,c=1,r=1,C=1;AQIDBA==");
        session.Send("a=T,i=2,p=1,P=1,Q=1,H=2,V=2,z=5,s=1,v=1,c=1,r=1,C=1;BQYHCA==");
        session.Write("\x1b[3;3H");
        session.Send($"a=d,d={selector}");
        Assert.Equal(2, session.Screen.GetKittyPlacements().Length);
        session.Send("a=d,d=P,x=1,y=1");
        Assert.Empty(session.Screen.GetKittyPlacements().ToArray());
        session.Send("a=p,i=2,C=1");
        Assert.Equal("\x1b_Gi=2;ENOENT: image not found\x1b\\", session.Replies[^1]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RelativePlacementIsStillSelectedByItsOwnZValue(bool native)
    {
        using Session session = new(native);
        session.Send("a=T,i=1,p=1,s=1,v=1,C=1;AQIDBA==");
        session.Send("a=T,i=2,p=1,P=1,Q=1,H=2,V=2,z=5,s=1,v=1,C=1;BQYHCA==");
        session.Send("a=d,d=Z,z=5");
        Assert.Equal(1, Assert.Single(session.Screen.GetKittyPlacements().ToArray()).ImageId);
        session.Send("a=p,i=2,C=1");
        Assert.Equal("\x1b_Gi=2;ENOENT: image not found\x1b\\", session.Replies[^1]);
    }

    public static IEnumerable<object[]> OutsideSelectors()
    {
        foreach (bool native in new[] { false, true })
            foreach (string selector in new[] { "p,x=6,y=5", "P,x=5,y=6", "q,x=6,y=5,z=0",
                         "Q,x=5,y=6,z=0", "x,x=6", "X,x=4294967295", "y,y=6", "Y,y=4294967295" })
                yield return [native, selector];
    }

    [Theory]
    [MemberData(nameof(OutsideSelectors))]
    public void OversizedPlacementDoesNotMakeOutOfScreenDeleteCoordinatesValid(bool native, string selector)
    {
        using Session session = new(native);
        session.Send("a=T,i=1,p=1,s=1,v=1,c=20,r=20,C=1;AQIDBA==");
        session.Send($"a=d,d={selector}");
        Assert.Equal(1, Assert.Single(session.Screen.GetKittyPlacements().ToArray()).ImageId);
        session.Send("a=d,d=p,x=5,y=5");
        Assert.Empty(session.Screen.GetKittyPlacements().ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void VisibleDeletionIncludesEmptyCroppedPlacementsAnchoredInActiveScreen(bool native)
    {
        using Session session = new(native);
        session.Send("a=T,i=1,p=1,s=1,v=1,x=1,y=1,C=1;AQIDBA==");
        Assert.Empty(session.Screen.GetKittyPlacements().ToArray());
        session.Send("a=d,d=A");
        session.Send("a=p,i=1,C=1");
        Assert.Equal("\x1b_Gi=1;ENOENT: image not found\x1b\\", session.Replies[^1]);
    }

    public static IEnumerable<object[]> NewestUnnumberedSelectors()
    {
        foreach (bool native in new[] { false, true })
            foreach (string number in new[] { "", ",I=0" })
                foreach (char action in new[] { 'n', 'N' })
                    foreach (uint placement in new[] { 0u, 2u, 99u })
                        yield return [native, number, action, placement];
    }

    [Theory]
    [MemberData(nameof(NewestUnnumberedSelectors))]
    public void NewestDeleteWithoutNumberSelectsUnnumberedImageNotSuppliedId(
        bool native, string number, char action, uint placement)
    {
        using Session session = new(native);
        session.Send("a=T,i=41,p=2,s=1,v=1,C=1;AQIDBA==");
        session.Send("a=T,i=42,p=2,s=1,v=1,C=1;BQYHCA==");
        session.Send("a=T,I=7,p=2,s=1,v=1,C=1;CQoLDA==");
        session.Send($"a=d,d={action},i=41{number},p={placement}");
        int[] ids = session.Screen.GetKittyPlacements().ToArray().Select(p => p.ImageId).ToArray();
        Assert.Contains(41, ids);
        Assert.Contains(1, ids);
        Assert.Equal(placement == 99, ids.Contains(42));
        session.Send("a=p,i=42,p=2,C=1");
        Assert.Equal(action == 'N' && placement != 99
            ? "\x1b_Gi=42,p=2;ENOENT: image not found\x1b\\"
            : "\x1b_Gi=42,p=2;OK\x1b\\", session.Replies[^1]);
    }

    public static IEnumerable<object[]> DegeneratePlacements()
    {
        foreach (bool native in new[] { false, true })
        {
            yield return [native, "x=1,c=2,r=3", 1, 1];
            yield return [native, "y=1,c=2,r=3", 1, 1];
            yield return [native, "c=1,X=9", 32, 1];
            yield return [native, "r=1,Y=9", 1, 32];
        }
    }

    public static IEnumerable<object[]> ImageRetirementCommands()
    {
        foreach (bool native in new[] { false, true })
            foreach (bool otherPlacement in new[] { false, true })
                foreach (string command in new[] { "a=t,i=1,f=99,s=1,v=1;AQIDBA==",
                             "a=t,i=1,s=1,v=1,m=1;AQID", "a=d,d=F,i=1" })
                    yield return [native, otherPlacement, command];
    }

    [Theory]
    [MemberData(nameof(ImageRetirementCommands))]
    public void ImageRetirementFreesOrphanSubtreesButNotIndependentlyPlacedOrUnusedImages(
        bool native, bool otherPlacement, string command)
    {
        using Session session = new(native);
        session.Send("a=T,i=1,p=1,s=1,v=1,C=1;AQIDBA==");
        session.Send("a=T,i=2,p=1,P=1,Q=1,s=1,v=1,C=1;BQYHCA==");
        session.Send("a=T,i=3,p=1,P=2,Q=1,s=1,v=1,C=1;CQoLDA==");
        session.Send("a=t,i=4,s=1,v=1;DQ4PEA==");
        if (otherPlacement) session.Send("a=p,i=2,p=2,C=1");
        session.Send(command);
        session.Send("a=p,i=2,C=1");
        Assert.Equal(otherPlacement ? "\x1b_Gi=2;OK\x1b\\" : "\x1b_Gi=2;ENOENT: image not found\x1b\\", session.Replies[^1]);
        session.Send("a=p,i=3,C=1");
        Assert.Equal("\x1b_Gi=3;ENOENT: image not found\x1b\\", session.Replies[^1]);
        session.Send("a=p,i=4,C=1");
        Assert.Equal("\x1b_Gi=4;OK\x1b\\", session.Replies[^1]);
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 600)]
    [InlineData(true, 600)]
    public void ErasingHistoryMigratesRemovedPinsToOriginAndRetainsActivePins(bool native, int lines)
    {
        using Session session = new(native, 2000);
        session.Write("\x1b[1;3H");
        session.Send("a=T,i=1,p=1,s=1,v=1,C=1;AQIDBA==");
        session.Send("a=T,i=2,p=1,P=1,Q=1,H=1,V=1,s=1,v=1,C=1;BQYHCA==");
        session.Write("\x1b[5;1H" + new string('\n', lines));
        session.Write("\x1b[4;4H");
        session.Send("a=T,i=3,p=1,s=1,v=1,C=1;CQoLDA==");
        session.Write("\x1b[3J");
        TerminalKittyImagePlacement[] placements = session.Screen.GetKittyPlacements().ToArray();
        Assert.Equal(3, placements.Length);
        Assert.Equal((0, 0), (placements[0].ViewportColumn, placements[0].ViewportRow));
        Assert.Equal((1, 1), (placements[1].ViewportColumn, placements[1].ViewportRow));
        Assert.Equal((3, 3), (placements[2].ViewportColumn, placements[2].ViewportRow));
        Assert.Equal(5, session.TotalRows);
    }

    public static IEnumerable<object[]> MarginlessPinScrolls()
    {
        foreach (bool native in new[] { false, true })
            foreach (bool alternate in new[] { false, true })
                foreach (int row in new[] { 0, 2, 4 })
                    foreach (string scroll in new[] { "\x1b[T", "\x1b[99T", "\x1b[H\x1bM", "\x1b[99S", "\x1b[5;1H\n" })
                        if (alternate || scroll.Contains('T') || scroll.EndsWith('M'))
                            yield return [native, alternate, row, scroll];
    }

    [Theory]
    [MemberData(nameof(MarginlessPinScrolls))]
    public void MarginlessScrollingPreservesNativePinLifetimeAndRelativeChildren(
        bool native, bool alternate, int row, string scroll)
    {
        using Session session = new(native);
        if (alternate) session.Write("\x1b[?1049h");
        session.Write($"\x1b[{row + 1};3H");
        session.Send("a=T,i=1,p=1,s=1,v=1,c=2,r=2,C=1;AQIDBA==");
        session.Send("a=T,i=2,p=1,P=1,Q=1,H=1,V=0,s=1,v=1,c=1,r=1,C=1;BQYHCA==");
        session.Write(scroll);
        int expectedRow = scroll.Contains('S') ? 0 : scroll.EndsWith('\n') ? Math.Max(0, row - 1) : row;
        int expectedColumn = scroll.EndsWith('\n') && row == 0 ? 0 : 2;
        TerminalKittyImagePlacement[] placements = session.Screen.GetKittyPlacements().ToArray();
        Assert.Equal(2, placements.Length);
        Assert.Equal((expectedColumn, expectedRow, 20, 20, 1, 1),
            (placements[0].ViewportColumn, placements[0].ViewportRow, placements[0].WidthPx,
                placements[0].HeightPx, placements[0].SourceWidth, placements[0].SourceHeight));
        Assert.Equal((expectedColumn + 1, expectedRow), (placements[1].ViewportColumn, placements[1].ViewportRow));
        session.Send("a=d,d=I,i=1");
        Assert.Empty(session.Screen.GetKittyPlacements().ToArray());
    }

    [Theory]
    [MemberData(nameof(DegeneratePlacements))]
    public void EmptySourceOrDestinationIsNotPublishedButRetainsProtocolPlacement(
        bool native, string geometry, int width, int height)
    {
        using Session session = new(native);
        string pixels = Convert.ToBase64String(Enumerable.Repeat((byte)255, width * height * 4).ToArray());
        session.Send($"a=T,i=1,p=1,s={width},v={height},{geometry},C=1;{pixels}");
        Assert.Empty(session.Screen.GetKittyPlacements().ToArray());
        Assert.False(session.Screen.TryGetKittyImageSource(1, out _));
        // Normal redisplay reuses the retained image. Replacing that placement
        // with an empty one must still let d=A find and free its active pin.
        session.Send("a=p,i=1,p=1,C=1");
        Assert.Single(session.Screen.GetKittyPlacements().ToArray());
        session.Send($"a=p,i=1,p=1,{geometry},C=1");
        Assert.Empty(session.Screen.GetKittyPlacements().ToArray());
        session.Send("a=d,d=A");
        session.Send("a=p,i=1,C=1");
        Assert.Equal("\x1b_Gi=1;ENOENT: image not found\x1b\\", session.Replies[^1]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HardResetRestartsBothBuffersAutomaticImageIdentifiers(bool native)
    {
        using Session session = new(native);
        session.Send("a=T,s=1,v=1,C=1;AQIDBA==");
        session.Write("\x1b[?1049h");
        session.Send("a=T,s=1,v=1,C=1;BQYHCA==");
        session.Write("\x1b" + "c");
        session.Send("a=T,s=1,v=1,C=1;CQoLDA==");
        Assert.Equal(2147483647, Assert.Single(session.Screen.GetKittyPlacements().ToArray()).ImageId);
        session.Write("\x1b[?1049h");
        session.Send("a=T,s=1,v=1,C=1;DQ4PEA==");
        Assert.Equal(2147483647, Assert.Single(session.Screen.GetKittyPlacements().ToArray()).ImageId);
    }

    private sealed class Session : IDisposable
    {
        internal TerminalScreen Screen { get; }
        internal IVtProcessor Processor { get; }
        internal List<string> Replies { get; } = [];
        internal int TotalRows => Processor is GhosttyVtProcessor native
            ? checked((int)native.ViewportScrollState.TotalRows) : Screen.TotalRows;

        internal Session(bool native, int scrollback = 100)
        {
            Screen = new(5, 5, scrollback);
            if (native && (!GhosttyVtProcessor.IsAvailable() || !GhosttyVtHelpers.GetBuildFeatures().KittyGraphics))
            {
                Assert.NotEqual("1", Environment.GetEnvironmentVariable("ROYALTERMINAL_REQUIRE_NATIVE_TESTS"));
                Assert.Skip("Native Ghostty Kitty graphics runtime unavailable.");
            }
            Processor = native ? new GhosttyVtProcessor(Screen) : new BasicVtProcessor(Screen);
            Processor.NotifyResize(5, 5, 50, 50);
            Processor.ResponseCallback = bytes => Replies.Add(Encoding.ASCII.GetString(bytes));
        }

        internal void Send(string command) => Write($"\x1b_G{command}\x1b\\");
        internal void Write(string text) => Processor.Process(Encoding.ASCII.GetBytes(text));
        public void Dispose() => Processor.Dispose();
    }
}
