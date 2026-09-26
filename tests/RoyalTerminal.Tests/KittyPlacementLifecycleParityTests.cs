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
        internal TerminalScreen Screen { get; } = new(5, 5, 100);
        internal IVtProcessor Processor { get; }
        internal List<string> Replies { get; } = [];
        internal int TotalRows => Processor is GhosttyVtProcessor native
            ? checked((int)native.ViewportScrollState.TotalRows) : Screen.TotalRows;

        internal Session(bool native)
        {
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
