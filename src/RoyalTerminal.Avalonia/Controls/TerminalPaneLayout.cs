// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia - Reusable terminal pane layout helpers.

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace RoyalTerminal.Avalonia.Controls;

/// <summary>
/// Reusable helpers for composing, navigating, and resizing terminal pane trees.
/// </summary>
public static class TerminalPaneLayout
{
    /// <summary>
    /// Minimum supported split ratio.
    /// </summary>
    public const double MinimumSplitRatio = 0.05;

    /// <summary>
    /// Maximum supported split ratio.
    /// </summary>
    public const double MaximumSplitRatio = 0.95;

    private const double DirectionTolerance = 0.5;

    /// <summary>
    /// Creates the default scroll viewer used to host a terminal pane leaf.
    /// </summary>
    /// <param name="terminal">Terminal control hosted by the scroll viewer.</param>
    /// <returns>A scroll viewer configured for terminal pane hosting.</returns>
    public static ScrollViewer CreatePaneScrollViewer(TerminalControl terminal)
    {
        ArgumentNullException.ThrowIfNull(terminal);

        return new ScrollViewer
        {
            Content = terminal,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
    }

    /// <summary>
    /// Creates a split grid with two child visuals and a splitter between them.
    /// </summary>
    /// <param name="orientation">Split orientation.</param>
    /// <param name="ratio">First child size ratio.</param>
    /// <param name="first">First child visual.</param>
    /// <param name="second">Second child visual.</param>
    /// <param name="splitterThickness">Splitter thickness in pixels.</param>
    /// <returns>A grid containing the split children and splitter.</returns>
    public static Grid CreateSplitGrid(
        TerminalPaneSplitOrientation orientation,
        double ratio,
        Control first,
        Control second,
        double splitterThickness = 4.0)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);

        double normalizedRatio = NormalizeRatio(ratio);
        double normalizedThickness = splitterThickness > 0 ? splitterThickness : 4.0;
        Grid grid = new()
        {
            Background = Brushes.Transparent,
        };

        if (orientation == TerminalPaneSplitOrientation.Vertical)
        {
            grid.RowDefinitions.Add(new RowDefinition(new GridLength(normalizedRatio, GridUnitType.Star)));
            grid.RowDefinitions.Add(new RowDefinition(new GridLength(normalizedThickness, GridUnitType.Pixel)));
            grid.RowDefinitions.Add(new RowDefinition(new GridLength(1.0 - normalizedRatio, GridUnitType.Star)));
            Grid.SetRow(first, 0);
            Grid.SetRow(second, 2);
            grid.Children.Add(first);
            GridSplitter splitter = new()
            {
                Height = normalizedThickness,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Center,
                Background = Brushes.Transparent,
            };
            Grid.SetRow(splitter, 1);
            grid.Children.Add(splitter);
            grid.Children.Add(second);
            return grid;
        }

        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(normalizedRatio, GridUnitType.Star)));
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(normalizedThickness, GridUnitType.Pixel)));
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1.0 - normalizedRatio, GridUnitType.Star)));
        Grid.SetColumn(first, 0);
        Grid.SetColumn(second, 2);
        grid.Children.Add(first);
        GridSplitter columnSplitter = new()
        {
            Width = normalizedThickness,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Stretch,
            Background = Brushes.Transparent,
        };
        Grid.SetColumn(columnSplitter, 1);
        grid.Children.Add(columnSplitter);
        grid.Children.Add(second);
        return grid;
    }

    /// <summary>
    /// Collects terminal controls from leaf nodes in display order.
    /// </summary>
    /// <param name="rootNode">Root pane node.</param>
    /// <returns>Terminal controls from leaf panes.</returns>
    public static IReadOnlyList<TerminalControl> CollectLeafControls(TerminalPaneNode? rootNode)
    {
        if (rootNode is null)
        {
            return [];
        }

        List<TerminalControl> controls = [];
        CollectLeafControls(rootNode, controls);
        return controls;
    }

    /// <summary>
    /// Determines whether a pane tree contains the target node.
    /// </summary>
    /// <param name="root">Root pane node.</param>
    /// <param name="target">Target pane node.</param>
    /// <returns><c>true</c> when the target is contained by the tree; otherwise, <c>false</c>.</returns>
    public static bool ContainsPaneNode(TerminalPaneNode? root, TerminalPaneNode target)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (root is null)
        {
            return false;
        }

        if (ReferenceEquals(root, target))
        {
            return true;
        }

        return ContainsPaneNode(root.First, target) || ContainsPaneNode(root.Second, target);
    }

    /// <summary>
    /// Finds the nearest ancestor split matching the requested orientation.
    /// </summary>
    /// <param name="activeNode">Active leaf pane node.</param>
    /// <param name="wantsHorizontal">Whether a horizontal split should be found.</param>
    /// <returns>The matching split node, or <c>null</c> when none exists.</returns>
    public static TerminalPaneNode? FindResizeSplit(TerminalPaneNode activeNode, bool wantsHorizontal)
    {
        ArgumentNullException.ThrowIfNull(activeNode);

        TerminalPaneNode? node = activeNode.Parent;
        while (node is not null)
        {
            bool isHorizontal = node.Orientation == TerminalPaneSplitOrientation.Horizontal;
            if (isHorizontal == wantsHorizontal)
            {
                return node;
            }

            node = node.Parent;
        }

        return null;
    }

    /// <summary>
    /// Calculates the split ratio delta for resizing the focused pane.
    /// </summary>
    /// <param name="splitNode">Split node being resized.</param>
    /// <param name="activeNode">Active leaf pane node.</param>
    /// <param name="direction">Resize direction.</param>
    /// <param name="step">Ratio step to apply.</param>
    /// <returns>Signed ratio delta.</returns>
    public static double GetFocusedPaneResizeDelta(
        TerminalPaneNode splitNode,
        TerminalPaneNode activeNode,
        TerminalPaneDirection direction,
        double step = 0.05)
    {
        ArgumentNullException.ThrowIfNull(splitNode);
        ArgumentNullException.ThrowIfNull(activeNode);

        double requestedDelta = direction is TerminalPaneDirection.Right or TerminalPaneDirection.Down
            ? step
            : -step;
        return ContainsPaneNode(splitNode.First, activeNode)
            ? requestedDelta
            : -requestedDelta;
    }

    /// <summary>
    /// Applies a split ratio to a split node and its grid rows or columns.
    /// </summary>
    /// <param name="splitNode">Split node to update.</param>
    /// <param name="ratio">First child size ratio.</param>
    public static void ApplySplitRatio(TerminalPaneNode splitNode, double ratio)
    {
        ArgumentNullException.ThrowIfNull(splitNode);

        splitNode.Ratio = NormalizeRatio(ratio);
        if (splitNode.SplitGrid is null)
        {
            return;
        }

        if (splitNode.Orientation == TerminalPaneSplitOrientation.Vertical)
        {
            splitNode.SplitGrid.RowDefinitions[0].Height = new GridLength(splitNode.Ratio, GridUnitType.Star);
            splitNode.SplitGrid.RowDefinitions[2].Height = new GridLength(1.0 - splitNode.Ratio, GridUnitType.Star);
            return;
        }

        splitNode.SplitGrid.ColumnDefinitions[0].Width = new GridLength(splitNode.Ratio, GridUnitType.Star);
        splitNode.SplitGrid.ColumnDefinitions[2].Width = new GridLength(1.0 - splitNode.Ratio, GridUnitType.Star);
    }

    /// <summary>
    /// Replaces a child pane visual and node inside a split parent.
    /// </summary>
    /// <param name="parent">Parent split node.</param>
    /// <param name="oldChild">Child node being replaced.</param>
    /// <param name="newChild">Replacement child node.</param>
    /// <param name="childIndex">Original visual child index.</param>
    /// <param name="row">Original grid row.</param>
    /// <param name="column">Original grid column.</param>
    public static void ReplaceChildPane(
        TerminalPaneNode parent,
        TerminalPaneNode oldChild,
        TerminalPaneNode newChild,
        int childIndex,
        int row,
        int column)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(oldChild);
        ArgumentNullException.ThrowIfNull(newChild);

        if (parent.SplitGrid is null)
        {
            return;
        }

        if (childIndex >= 0)
        {
            Grid.SetRow(newChild.Visual, row);
            Grid.SetColumn(newChild.Visual, column);
            parent.SplitGrid.Children.Insert(childIndex, newChild.Visual);
        }

        if (ReferenceEquals(parent.First, oldChild))
        {
            parent.First = newChild;
            newChild.Parent = parent;
        }
        else if (ReferenceEquals(parent.Second, oldChild))
        {
            parent.Second = newChild;
            newChild.Parent = parent;
        }
    }

    /// <summary>
    /// Finds a pane in the requested visual direction from the active pane.
    /// </summary>
    /// <param name="leafControls">Leaf terminal controls in display order.</param>
    /// <param name="nodes">Mapping from terminal controls to pane nodes.</param>
    /// <param name="visualRoot">Visual root used to compare pane positions.</param>
    /// <param name="activeControl">Active terminal control.</param>
    /// <param name="direction">Focus direction.</param>
    /// <returns>The best matching pane, or <c>null</c> when none exists.</returns>
    public static TerminalControl? FindDirectionalPane(
        IReadOnlyList<TerminalControl> leafControls,
        IReadOnlyDictionary<TerminalControl, TerminalPaneNode> nodes,
        Visual visualRoot,
        TerminalControl activeControl,
        TerminalPaneDirection direction)
    {
        ArgumentNullException.ThrowIfNull(leafControls);
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(visualRoot);
        ArgumentNullException.ThrowIfNull(activeControl);

        if (!nodes.TryGetValue(activeControl, out TerminalPaneNode? activeNode) ||
            activeNode.LeafContainer is null)
        {
            return null;
        }

        Point? activeOrigin = activeNode.LeafContainer.TranslatePoint(new Point(0, 0), visualRoot);
        if (activeOrigin is null)
        {
            return null;
        }

        Point activeCenter = new(
            activeOrigin.Value.X + activeNode.LeafContainer.Bounds.Width / 2.0,
            activeOrigin.Value.Y + activeNode.LeafContainer.Bounds.Height / 2.0);
        TerminalControl? best = null;
        double bestScore = double.MaxValue;

        for (int i = 0; i < leafControls.Count; i++)
        {
            TerminalControl candidate = leafControls[i];
            if (ReferenceEquals(candidate, activeControl) ||
                !nodes.TryGetValue(candidate, out TerminalPaneNode? candidateNode) ||
                candidateNode.LeafContainer is null)
            {
                continue;
            }

            Point? candidateOrigin = candidateNode.LeafContainer.TranslatePoint(new Point(0, 0), visualRoot);
            if (candidateOrigin is null)
            {
                continue;
            }

            Point candidateCenter = new(
                candidateOrigin.Value.X + candidateNode.LeafContainer.Bounds.Width / 2.0,
                candidateOrigin.Value.Y + candidateNode.LeafContainer.Bounds.Height / 2.0);
            double dx = candidateCenter.X - activeCenter.X;
            double dy = candidateCenter.Y - activeCenter.Y;
            if (!IsCandidateInDirection(direction, dx, dy))
            {
                continue;
            }

            double primaryDistance = direction is TerminalPaneDirection.Left or TerminalPaneDirection.Right
                ? Math.Abs(dx)
                : Math.Abs(dy);
            double secondaryDistance = direction is TerminalPaneDirection.Left or TerminalPaneDirection.Right
                ? Math.Abs(dy)
                : Math.Abs(dx);
            double score = primaryDistance * 1000.0 + secondaryDistance;
            if (score < bestScore)
            {
                best = candidate;
                bestScore = score;
            }
        }

        return best;
    }

    /// <summary>
    /// Finds the next or previous pane in display order.
    /// </summary>
    /// <param name="leafControls">Leaf terminal controls in display order.</param>
    /// <param name="activeControl">Active terminal control.</param>
    /// <param name="forward">Whether to move forward.</param>
    /// <returns>The next pane, or <c>null</c> when no pane can be selected.</returns>
    public static TerminalControl? FindSequentialPane(
        IReadOnlyList<TerminalControl> leafControls,
        TerminalControl activeControl,
        bool forward)
    {
        ArgumentNullException.ThrowIfNull(leafControls);
        ArgumentNullException.ThrowIfNull(activeControl);

        int index = IndexOfControl(leafControls, activeControl);
        if (index < 0 || leafControls.Count == 0)
        {
            return null;
        }

        int targetIndex = forward
            ? (index + 1) % leafControls.Count
            : (index - 1 + leafControls.Count) % leafControls.Count;
        return leafControls[targetIndex];
    }

    /// <summary>
    /// Determines whether a terminal control is present in the supplied control list.
    /// </summary>
    /// <param name="controls">Controls to inspect.</param>
    /// <param name="control">Control to find.</param>
    /// <returns><c>true</c> when the control is present; otherwise, <c>false</c>.</returns>
    public static bool ContainsControl(IReadOnlyList<TerminalControl> controls, TerminalControl control)
        => IndexOfControl(controls, control) >= 0;

    /// <summary>
    /// Finds a terminal control by reference in the supplied control list.
    /// </summary>
    /// <param name="controls">Controls to inspect.</param>
    /// <param name="control">Control to find.</param>
    /// <returns>The zero-based index, or <c>-1</c> when not found.</returns>
    public static int IndexOfControl(IReadOnlyList<TerminalControl> controls, TerminalControl control)
    {
        ArgumentNullException.ThrowIfNull(controls);
        ArgumentNullException.ThrowIfNull(control);

        for (int i = 0; i < controls.Count; i++)
        {
            if (ReferenceEquals(controls[i], control))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Gets a one-based pane ordinal for the supplied control.
    /// </summary>
    /// <param name="leafControls">Leaf terminal controls in display order.</param>
    /// <param name="control">Terminal control to locate.</param>
    /// <returns>A one-based pane ordinal, or <c>0</c> when the control is not found.</returns>
    public static int GetPaneOrdinal(IReadOnlyList<TerminalControl> leafControls, TerminalControl control)
        => IndexOfControl(leafControls, control) + 1;

    private static void CollectLeafControls(TerminalPaneNode node, List<TerminalControl> controls)
    {
        if (node.Control is not null)
        {
            controls.Add(node.Control);
            return;
        }

        if (node.First is not null)
        {
            CollectLeafControls(node.First, controls);
        }

        if (node.Second is not null)
        {
            CollectLeafControls(node.Second, controls);
        }
    }

    private static bool IsCandidateInDirection(TerminalPaneDirection direction, double dx, double dy)
    {
        return direction switch
        {
            TerminalPaneDirection.Left => dx < -DirectionTolerance,
            TerminalPaneDirection.Right => dx > DirectionTolerance,
            TerminalPaneDirection.Up => dy < -DirectionTolerance,
            TerminalPaneDirection.Down => dy > DirectionTolerance,
            _ => false,
        };
    }

    private static double NormalizeRatio(double ratio)
        => Math.Clamp(ratio, MinimumSplitRatio, MaximumSplitRatio);
}
