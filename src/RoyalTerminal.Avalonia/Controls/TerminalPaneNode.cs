// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia - Reusable terminal pane tree node.

using System;
using Avalonia.Controls;

namespace RoyalTerminal.Avalonia.Controls;

/// <summary>
/// Runtime node for a reusable terminal pane tree.
/// </summary>
public sealed class TerminalPaneNode
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TerminalPaneNode"/> class.
    /// </summary>
    /// <param name="id">Stable pane identifier.</param>
    /// <param name="title">Optional pane title.</param>
    /// <param name="profileId">Optional session profile identifier associated with the pane.</param>
    /// <param name="workingDirectory">Optional pane working directory.</param>
    /// <param name="transportId">Optional transport identifier associated with the pane.</param>
    /// <param name="transportProfileId">Optional transport profile identifier associated with the pane.</param>
    public TerminalPaneNode(
        string id,
        string? title = null,
        string? profileId = null,
        string? workingDirectory = null,
        string? transportId = null,
        string? transportProfileId = null)
    {
        Id = string.IsNullOrWhiteSpace(id) ? throw new ArgumentException("Pane id is required.", nameof(id)) : id;
        Title = title;
        ProfileId = profileId;
        WorkingDirectory = workingDirectory;
        TransportId = transportId;
        TransportProfileId = transportProfileId;
    }

    /// <summary>
    /// Gets the stable pane identifier.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// Gets the optional pane title.
    /// </summary>
    public string? Title { get; }

    /// <summary>
    /// Gets the optional session profile identifier associated with the pane.
    /// </summary>
    public string? ProfileId { get; }

    /// <summary>
    /// Gets the optional pane working directory.
    /// </summary>
    public string? WorkingDirectory { get; private set; }

    /// <summary>
    /// Gets the optional transport identifier associated with the pane.
    /// </summary>
    public string? TransportId { get; }

    /// <summary>
    /// Gets the optional transport profile identifier associated with the pane.
    /// </summary>
    public string? TransportProfileId { get; }

    /// <summary>
    /// Gets or sets the parent split node.
    /// </summary>
    public TerminalPaneNode? Parent { get; set; }

    /// <summary>
    /// Gets or sets the first child node for split panes.
    /// </summary>
    public TerminalPaneNode? First { get; set; }

    /// <summary>
    /// Gets or sets the second child node for split panes.
    /// </summary>
    public TerminalPaneNode? Second { get; set; }

    /// <summary>
    /// Gets the split orientation when this node is a split node.
    /// </summary>
    public TerminalPaneSplitOrientation? Orientation { get; private set; }

    /// <summary>
    /// Gets or sets the first child size ratio for split panes.
    /// </summary>
    public double Ratio { get; set; } = 0.5;

    /// <summary>
    /// Gets the terminal control when this node is a leaf pane.
    /// </summary>
    public TerminalControl? Control { get; private set; }

    /// <summary>
    /// Gets the leaf pane container when this node is a leaf pane.
    /// </summary>
    public Control? LeafContainer { get; private set; }

    /// <summary>
    /// Gets the split grid when this node is a split pane.
    /// </summary>
    public Grid? SplitGrid { get; private set; }

    /// <summary>
    /// Gets the visual root for this pane node.
    /// </summary>
    public Control Visual { get; private set; } = new Grid();

    /// <summary>
    /// Configures this node as a leaf pane.
    /// </summary>
    /// <param name="control">Terminal control hosted by the pane.</param>
    /// <param name="container">Visual container for the terminal control.</param>
    public void SetLeaf(TerminalControl control, Control container)
    {
        Control = control ?? throw new ArgumentNullException(nameof(control));
        LeafContainer = container ?? throw new ArgumentNullException(nameof(container));
        First = null;
        Second = null;
        Orientation = null;
        SplitGrid = null;
        Visual = container;
    }

    /// <summary>
    /// Updates the pane working directory metadata.
    /// </summary>
    /// <param name="workingDirectory">Working directory metadata to associate with the pane.</param>
    public void SetWorkingDirectory(string? workingDirectory)
    {
        WorkingDirectory = workingDirectory;
    }

    /// <summary>
    /// Configures this node as a split pane.
    /// </summary>
    /// <param name="orientation">Split orientation.</param>
    /// <param name="ratio">First child size ratio.</param>
    /// <param name="first">First child pane.</param>
    /// <param name="second">Second child pane.</param>
    /// <param name="grid">Split grid visual.</param>
    public void SetSplit(
        TerminalPaneSplitOrientation orientation,
        double ratio,
        TerminalPaneNode first,
        TerminalPaneNode second,
        Grid grid)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        ArgumentNullException.ThrowIfNull(grid);

        Control = null;
        LeafContainer = null;
        Orientation = orientation;
        Ratio = Math.Clamp(ratio, TerminalPaneLayout.MinimumSplitRatio, TerminalPaneLayout.MaximumSplitRatio);
        First = first;
        Second = second;
        first.Parent = this;
        second.Parent = this;
        SplitGrid = grid;
        Visual = grid;
    }
}
