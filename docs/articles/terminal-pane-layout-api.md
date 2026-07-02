---
title: Terminal Pane Layout API
---

# Terminal Pane Layout API

`RoyalApps.RoyalTerminal.Avalonia` includes reusable split-pane composition
helpers next to `TerminalControl`. These helpers are for applications that want
to build their own terminal shell without depending on the complete
`RoyalApps.RoyalTerminal.Avalonia.App` package.

The API manages pane tree visuals and geometry. It does not own transport
startup, SSH authentication, profile cloning, persistence stores, or menu
commands.

## Public API

| Type | Purpose |
| --- | --- |
| `TerminalPaneNode` | Runtime pane tree node. It can be a leaf that hosts one `TerminalControl` or a split node with two child panes. |
| `TerminalPaneLayout` | Static helper for pane containers, split grids, leaf collection, focus lookup, and ratio updates. |
| `TerminalPaneSplitRequest` | Command-level request to split right or down. |
| `TerminalPaneDirection` | Direction used by focus and resize commands. |
| `TerminalPaneSplitOrientation` | Runtime split orientation: horizontal or vertical. |

## Creating A Leaf

```csharp
TerminalControl terminal = new();

TerminalPaneNode leaf = new(
    id: "pane-1",
    title: "Local shell",
    profileId: "default",
    workingDirectory: Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
    transportId: TerminalTransportIds.Pty);

leaf.SetLeaf(terminal, TerminalPaneLayout.CreatePaneScrollViewer(terminal));
```

`CreatePaneScrollViewer` returns the default scroll viewer used by the reusable
shell: vertical scrollbars are automatic and horizontal scrollbars are disabled.

## Creating A Split

```csharp
TerminalPaneNode left = new("left");
TerminalPaneNode right = new("right");

TerminalControl leftControl = new();
TerminalControl rightControl = new();

left.SetLeaf(leftControl, TerminalPaneLayout.CreatePaneScrollViewer(leftControl));
right.SetLeaf(rightControl, TerminalPaneLayout.CreatePaneScrollViewer(rightControl));

Grid grid = TerminalPaneLayout.CreateSplitGrid(
    TerminalPaneSplitOrientation.Horizontal,
    ratio: 0.5,
    left.Visual,
    right.Visual);

TerminalPaneNode root = new("root");
root.SetSplit(TerminalPaneSplitOrientation.Horizontal, 0.5, left, right, grid);
```

`Horizontal` creates side-by-side panes. `Vertical` creates stacked panes.
Ratios are clamped to `TerminalPaneLayout.MinimumSplitRatio` and
`TerminalPaneLayout.MaximumSplitRatio`.

## Navigating Panes

The layout helper can collect leaves and resolve focus movement:

```csharp
IReadOnlyList<TerminalControl> leaves =
    TerminalPaneLayout.CollectLeafControls(root);

Dictionary<TerminalControl, TerminalPaneNode> nodes = new()
{
    [leftControl] = left,
    [rightControl] = right,
};

TerminalControl? next = TerminalPaneLayout.FindDirectionalPane(
    leaves,
    nodes,
    root.Visual,
    leftControl,
    TerminalPaneDirection.Right);
```

`FindDirectionalPane` uses arranged visual positions. If no geometric candidate
is available, hosts can use `FindSequentialPane` as a fallback.

## Resizing Panes

For keyboard resize commands, locate the nearest ancestor split matching the
requested axis, calculate the focused-pane delta, then apply the new ratio:

```csharp
TerminalPaneNode? split =
    TerminalPaneLayout.FindResizeSplit(activeNode, wantsHorizontal: true);

if (split is not null)
{
    double delta = TerminalPaneLayout.GetFocusedPaneResizeDelta(
        split,
        activeNode,
        TerminalPaneDirection.Right);

    TerminalPaneLayout.ApplySplitRatio(split, split.Ratio + delta);
}
```

`ApplySplitRatio` updates both the node metadata and the Avalonia grid star
definitions.

## Replacing A Child Pane

When an existing leaf is split, hosts can replace the old visual in its parent
grid:

```csharp
TerminalPaneLayout.ReplaceChildPane(
    parent,
    oldChild: activeNode,
    newChild: splitNode,
    childIndex,
    row,
    column);
```

The helper updates the visual tree and the node parent/child relationship.

## What Hosts Still Own

Applications still own:

- creating and starting each `TerminalControl` session;
- choosing whether a split is allowed;
- cloning or replacing session profiles;
- SSH MFA and credential prompts;
- durable workspace persistence;
- menu, toolbar, and shortcut wiring.

Use [Pane Split Policy](/articles/split-pane-policy) if you want the reusable
app-shell policy layer. Use this article if you only need lower-level pane
composition.
