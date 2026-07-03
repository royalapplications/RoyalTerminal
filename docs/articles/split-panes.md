---
title: Split Panes
---

# Split Panes

RoyalTerminal models panes as durable workspace data and materializes them as
ordinary `TerminalControl` instances in the host UI. The reusable pane layout
helpers live in `RoyalApps.RoyalTerminal.Avalonia`, next to `TerminalControl`,
while session clone policy, workspace startup, and product commands stay in the
host or app shell. This keeps pane layout out of the terminal emulator while
allowing each pane to keep normal terminal features such as search, capture,
themes, shaders, and shell integration.

## Pane Model

`TerminalWorkspacePane` stores a leaf pane or a split node:

- stable `Id`;
- optional `Title`;
- optional `ProfileId`;
- optional `WorkingDirectory`;
- optional `TransportId`;
- optional `TransportProfileId`;
- optional `Split`.

When `Split` is null, the pane is a leaf session pane. When `Split` is set,
`TerminalWorkspacePaneSplit` stores:

- `Orientation`;
- `Ratio`;
- `FirstPane`;
- `SecondPane`.

```csharp
TerminalWorkspacePane root = new()
{
    Id = "root",
    Split = new TerminalWorkspacePaneSplit
    {
        Orientation = TerminalWorkspacePaneSplitOrientations.Horizontal,
        Ratio = 0.5,
        FirstPane = new TerminalWorkspacePane
        {
            Id = "left",
            ProfileId = "default",
        },
        SecondPane = new TerminalWorkspacePane
        {
            Id = "right",
            ProfileId = "ssh-prod",
        },
    },
};
```

`Horizontal` means side-by-side panes. `Vertical` means stacked panes. `Ratio`
is the first pane's share of the available space and is normalized to `0.05`
through `0.95`.

## Reusable Runtime Layout

Use the lower-level `RoyalApps.RoyalTerminal.Avalonia` package when you want to
compose split panes in your own application without referencing the product app
shell. The reusable types are:

- `TerminalPaneNode` for runtime pane trees;
- `TerminalPaneLayout` for scroll viewer creation, split grids, leaf collection,
  directional focus lookup, sequential focus fallback, and split ratio updates;
- `TerminalPaneSplitRequest`, `TerminalPaneDirection`, and
  `TerminalPaneSplitOrientation` for command and layout contracts.

See [Terminal Pane Layout API](/articles/terminal-pane-layout-api) for the
full lower-level API guide, focus and resize helper details, and host-owned
responsibilities.

```csharp
TerminalControl leftControl = new();
TerminalControl rightControl = new();

TerminalPaneNode left = new("left");
TerminalPaneNode right = new("right");
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

Hosts own the session lifecycle for every leaf. A split pane is normally a new
independent `TerminalControl` session. Apps can allow PTY splits, deny SSH
splits, or provide a custom clone strategy for transports such as Rebex SSH with
MFA. The reusable app shell exposes that policy through
`ITerminalPaneSplitPolicy`, but apps that only use `TerminalControl` can apply
the same decision before creating the new leaf.

See [Pane Split Policy](/articles/split-pane-policy) for the reusable app-shell
policy API and transport-specific examples.

## App Shell Runtime Behavior

The reusable app shell builds a runtime pane tree from the workspace pane tree.
Each leaf creates a `TerminalControl` wrapped in a `ScrollViewer`. Each split
uses `TerminalPaneLayout.CreateSplitGrid(...)` to create an Avalonia `Grid` with
a `GridSplitter` between its children.

Interactive pane commands:

| Command | Default gesture |
| --- | --- |
| Split pane right | `Alt+Shift+OemPlus` |
| Split pane down | `Alt+Shift+OemMinus` |
| Focus pane left/right/up/down | `Alt+Arrow` |
| Resize pane left/right/up/down | `Alt+Shift+Arrow` |

Focus uses pane geometry first, then sequential fallback. Resize commands adjust
the nearest split that matches the requested horizontal or vertical direction.

## Persistence

On shutdown, the reusable app shell snapshots the live runtime pane tree back into
`TerminalWorkspacePane` data. Before writing a split, it reads the current
Avalonia grid star sizes so splitter movement is persisted without a separate
event handler.

The saved pane tree includes:

- split orientation;
- live split ratio;
- child panes;
- pane id;
- pane title;
- pane profile id;
- pane transport id;
- pane working directory.

## Active Pane Features

The active pane is the target for:

- capture/replay state;
- search commands;
- copy, paste, and select-all;
- snapshot copy;
- font size changes;
- theme changes;
- shader sample changes;
- command history capture;
- session restart and clear-history commands.

This keeps split panes behaviorally equivalent to standalone tabs instead of
creating a separate, reduced pane surface.

## Tests

Focused coverage:

- `TerminalWorkspaceSerializerTests` for pane tree normalization;
- `TerminalPaneLayoutTests` for reusable pane tree and split layout helpers;
- `MainWindowViewModelFlowTests` for pane command routing;
- `MainWindowControllerModeStartupTests` for split restore, interactive split
  creation, focus command wiring, resize commands, and live ratio persistence.
