---
title: Split Panes
---

# Split Panes

RoyalTerminal models panes as durable workspace data and materializes them as
ordinary `TerminalControl` instances in the host UI. This keeps pane layout out
of the terminal emulator while allowing each pane to keep normal terminal
features such as search, capture, themes, shaders, and shell integration.

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

## Runtime Behavior

The demo app builds a runtime pane tree from the workspace pane tree. Each leaf
creates a `TerminalControl` wrapped in a `ScrollViewer`. Each split creates an
Avalonia `Grid` with a `GridSplitter` between its children.

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

On shutdown, the demo snapshots the live runtime pane tree back into
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
- `MainWindowViewModelFlowTests` for pane command routing;
- `MainWindowControllerModeStartupTests` for split restore, interactive split
  creation, focus command wiring, resize commands, and live ratio persistence.
