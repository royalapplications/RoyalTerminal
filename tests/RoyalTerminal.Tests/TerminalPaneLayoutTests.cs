// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Tests - Reusable terminal pane layout coverage.

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using RoyalTerminal.Avalonia.Controls;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class TerminalPaneLayoutTests
{
    [AvaloniaFact]
    public void SplitNode_CollectsLeafControls_AndFindsResizeSplit()
    {
        TerminalControl firstControl = new();
        TerminalControl secondControl = new();
        ScrollViewer firstContainer = TerminalPaneLayout.CreatePaneScrollViewer(firstControl);
        ScrollViewer secondContainer = TerminalPaneLayout.CreatePaneScrollViewer(secondControl);
        TerminalPaneNode first = new("first");
        TerminalPaneNode second = new("second");
        TerminalPaneNode split = new("split");
        first.SetLeaf(firstControl, firstContainer);
        second.SetLeaf(secondControl, secondContainer);

        Grid splitGrid = TerminalPaneLayout.CreateSplitGrid(
            TerminalPaneSplitOrientation.Horizontal,
            0.25,
            first.Visual,
            second.Visual);
        split.SetSplit(TerminalPaneSplitOrientation.Horizontal, 0.25, first, second, splitGrid);

        IReadOnlyList<TerminalControl> leaves = TerminalPaneLayout.CollectLeafControls(split);

        Assert.Equal([firstControl, secondControl], leaves);
        Assert.Same(split, TerminalPaneLayout.FindResizeSplit(first, wantsHorizontal: true));
        Assert.Null(TerminalPaneLayout.FindResizeSplit(first, wantsHorizontal: false));
        Assert.True(TerminalPaneLayout.ContainsPaneNode(split, second));
        Assert.Equal(1, TerminalPaneLayout.GetPaneOrdinal(leaves, firstControl));
        Assert.Equal(2, TerminalPaneLayout.GetPaneOrdinal(leaves, secondControl));
    }

    [AvaloniaFact]
    public void ApplySplitRatio_ClampsAndUpdatesGridDefinitions()
    {
        TerminalControl firstControl = new();
        TerminalControl secondControl = new();
        TerminalPaneNode first = new("first");
        TerminalPaneNode second = new("second");
        TerminalPaneNode split = new("split");
        first.SetLeaf(firstControl, TerminalPaneLayout.CreatePaneScrollViewer(firstControl));
        second.SetLeaf(secondControl, TerminalPaneLayout.CreatePaneScrollViewer(secondControl));
        Grid splitGrid = TerminalPaneLayout.CreateSplitGrid(
            TerminalPaneSplitOrientation.Vertical,
            0.5,
            first.Visual,
            second.Visual);
        split.SetSplit(TerminalPaneSplitOrientation.Vertical, 0.5, first, second, splitGrid);

        TerminalPaneLayout.ApplySplitRatio(split, 0.99);

        Assert.Equal(TerminalPaneLayout.MaximumSplitRatio, split.Ratio);
        Assert.Equal(TerminalPaneLayout.MaximumSplitRatio, splitGrid.RowDefinitions[0].Height.Value);
        Assert.Equal(1.0 - TerminalPaneLayout.MaximumSplitRatio, splitGrid.RowDefinitions[2].Height.Value);
    }

    [AvaloniaFact]
    public void DirectionalPaneLookup_UsesPanePositions()
    {
        TerminalControl leftControl = new();
        TerminalControl rightControl = new();
        ScrollViewer leftContainer = TerminalPaneLayout.CreatePaneScrollViewer(leftControl);
        ScrollViewer rightContainer = TerminalPaneLayout.CreatePaneScrollViewer(rightControl);
        TerminalPaneNode left = new("left");
        TerminalPaneNode right = new("right");
        TerminalPaneNode split = new("split");
        left.SetLeaf(leftControl, leftContainer);
        right.SetLeaf(rightControl, rightContainer);
        Grid splitGrid = TerminalPaneLayout.CreateSplitGrid(
            TerminalPaneSplitOrientation.Horizontal,
            0.5,
            left.Visual,
            right.Visual);
        split.SetSplit(TerminalPaneSplitOrientation.Horizontal, 0.5, left, right, splitGrid);
        IReadOnlyList<TerminalControl> leaves = [leftControl, rightControl];
        Dictionary<TerminalControl, TerminalPaneNode> nodes = new()
        {
            [leftControl] = left,
            [rightControl] = right,
        };
        Window window = new()
        {
            Width = 640,
            Height = 360,
            Content = split.Visual,
        };

        try
        {
            window.Show();
            window.Measure(new global::Avalonia.Size(640, 360));
            window.Arrange(new global::Avalonia.Rect(0, 0, 640, 360));

            TerminalControl? target = TerminalPaneLayout.FindDirectionalPane(
                leaves,
                nodes,
                split.Visual,
                leftControl,
                TerminalPaneDirection.Right);

            Assert.Same(rightControl, target);
            Assert.Same(rightControl, TerminalPaneLayout.FindSequentialPane(leaves, leftControl, forward: true));
        }
        finally
        {
            window.Close();
        }
    }
}
