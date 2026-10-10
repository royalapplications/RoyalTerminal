// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class OctoberFormatterRegressionTests
{
    public static IEnumerable<object[]> OriginCases()
    {
        foreach (bool native in new[] { false, true })
        foreach (bool region in new[] { false, true })
        foreach (string stream in new[] {
            "\u001b[3;8r\u001b[?6h\u001b[2;5H",
            "\u001b[?69h\u001b[3;8r\u001b[4;15s\u001b[?6h\u001b[2;3H",
            "\u001b[3;8r\u001b[?6h\u001b[2;20Hx" })
            yield return [native, region, stream];
    }

    [Theory]
    [MemberData(nameof(OriginCases))]
    public void StyledVtRestoresOriginCursorRelativeOnlyToExportedMargins(bool native, bool region, string stream)
    {
        if (native && !GhosttyVtProcessor.IsAvailable()) return;
        using IVtProcessor source = Create(native);
        using IVtProcessor target = Create(native);
        source.Process(Encoding.UTF8.GetBytes(stream));
        TerminalSnapshotExportOptions options = new(Extras:
            new(IncludeCursor: true, IncludeModes: true, IncludeScrollingRegion: region, IncludeCharsets: true));
        Assert.True(((ITerminalSnapshotExportSource)source).TryExportSnapshot(TerminalSnapshotExportFormat.StyledVt,
            options, out string snapshot));
        target.Process(Encoding.UTF8.GetBytes(snapshot));
        Assert.Equal(source.CursorCol, target.CursorCol);
        Assert.Equal(source.CursorRow, target.CursorRow);
        // Printing after replay also checks the right-edge pending-wrap state.
        source.Process("X"u8);
        target.Process("X"u8);
        Assert.Equal(source.CursorCol, target.CursorCol);
        Assert.Equal(source.CursorRow, target.CursorRow);
    }

    private static IVtProcessor Create(bool native) => native
        ? new GhosttyVtProcessor(new TerminalScreen(20, 10, 0))
        : new BasicVtProcessor(new TerminalScreen(20, 10, 0));
}
