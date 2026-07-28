// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.GhosttySharp.Native;
using RoyalTerminal.IntegrationTests.TestInfrastructure;
using Xunit;

namespace RoyalTerminal.IntegrationTests;

public class GhosttyRenderStateExtendedTests
{
    [GhosttyNativeFact]
    public void TwoPhaseUpdateExposesSelectionStylingAndUtf8()
    {
        using GhosttyTerminal terminal = new(10, 3);
        using GhosttyRenderState renderState = new();

        terminal.Write(Encoding.UTF8.GetBytes("\u001b[31mAe\u0301B"));
        Assert.True(
            terminal.TryGetGridReference(
                GhosttyVtNative.GhosttyPoint.Active(0, 0),
                out GhosttyVtNative.GhosttyGridRef start));
        Assert.True(
            terminal.TryGetGridReference(
                GhosttyVtNative.GhosttyPoint.Active(1, 0),
                out GhosttyVtNative.GhosttyGridRef end));
        terminal.SetSelection(new RoyalTerminal.GhosttySharp.GhosttySelection(start, end));

        renderState.BeginUpdate(terminal);
        renderState.EndUpdate();
        renderState.BeginRows();
        Assert.True(renderState.MoveNextRow());
        Assert.True(renderState.TryGetCurrentRowSelection(out ushort startX, out ushort endX));
        Assert.Equal((ushort)0, startX);
        Assert.Equal((ushort)1, endX);

        renderState.BeginCurrentRowCells();
        Assert.True(renderState.MoveNextCell());
        Assert.True(renderState.GetCurrentCellSelected());
        Assert.True(renderState.GetCurrentCellHasStyling());

        Assert.True(renderState.MoveNextCell());
        Assert.True(renderState.GetCurrentCellSelected());
        Assert.Equal(
            "e\u0301",
            Encoding.UTF8.GetString(renderState.CopyCurrentCellGraphemeUtf8()));

        Assert.True(renderState.MoveNextCell());
        Assert.False(renderState.GetCurrentCellSelected());
    }
}
