---
title: Workspace Restore
---

# Workspace Restore

RoyalTerminal stores product layout separately from terminal emulation state.
`TerminalControl` owns the live terminal session. `TerminalWorkspaceDocument`
records which windows, tabs, profiles, transports, working directories, render
modes, and pane layouts should be restored by a host application.

The workspace APIs live in `RoyalApps.RoyalTerminal.Terminal` and do not depend
on Avalonia.

## Document Model

`TerminalWorkspaceDocument` is a versioned root document with:

- `FormatVersion`;
- `SelectedWindowId`;
- `Windows`.

Each `TerminalWorkspaceWindow` stores:

- stable `Id`;
- optional `Title`;
- optional `SelectedTabId`;
- window `WidthPixels` and `HeightPixels`;
- `IsMaximized`;
- tab list.

Each `TerminalWorkspaceTab` stores:

- stable `Id`;
- `ProfileId`;
- optional `Title`;
- optional `WorkingDirectory`;
- `TransportId`;
- optional `TransportProfileId`;
- `RenderMode`;
- `RootPane`.

`RootPane` uses `TerminalWorkspacePane`, documented in
[Split Panes](/articles/split-panes).

## Serialization

Use `TerminalWorkspaceSerializer` to load and save documents:

```csharp
await using FileStream input = File.OpenRead("workspace.json");
TerminalWorkspaceDocument loaded =
    await TerminalWorkspaceSerializer.LoadAsync(input);

string json = TerminalWorkspaceSerializer.ToJson(loaded);
```

The serializer normalizes documents during load and save:

- trims optional text;
- validates required ids;
- normalizes transport identifiers;
- normalizes render mode identifiers;
- keeps selected ids only when they reference existing windows or tabs;
- normalizes pane trees and split ratios.

Supported render mode identifiers are:

| Constant | Value |
| --- | --- |
| `TerminalWorkspaceRenderModes.Default` | `Default` |
| `TerminalWorkspaceRenderModes.Text` | `Text` |
| `TerminalWorkspaceRenderModes.Skia` | `Skia` |
| `TerminalWorkspaceRenderModes.Ghostty` | `Ghostty` |

## Stores

`ITerminalWorkspaceStore` abstracts persistence:

```csharp
ITerminalWorkspaceStore store = TerminalWorkspaceStoreFactory.CreateDefault();
TerminalWorkspaceDocument workspace = await store.LoadAsync();
await store.SaveAsync(workspace);
```

`JsonFileTerminalWorkspaceStore` persists JSON with atomic file writes.
`TerminalWorkspaceStoreFactory.CreateDefault()` stores `workspace.json` next to
the default session profile file. Pass an explicit path when a host needs a
portable workspace, a profile-specific workspace, or a test store.

## Shared Shell Integration

`RoyalApps.RoyalTerminal.Avalonia.App` uses the workspace store as a product
startup and shutdown boundary:

- startup loads the selected window and restores tabs;
- default product startup opens one tab when no workspace exists;
- diagnostic multi-renderer startup is only enabled with
  `ROYALTERMINAL_DEMO_START_ALL_RENDER_MODES=1`;
- restored inactive tabs are metadata-only placeholders until selected, so
  terminal controls, renderers, and session startup are deferred;
- shutdown saves selected tab, window size, profile id, transport id, working
  directory, render mode, and the live pane tree.

The controller stores per-control launch configuration when tabs and panes are
created. That keeps restored sessions from racing on mutable ViewModel state
during delayed or queued session startup.

Deferred restored tabs keep their original serialized `RootPane`, tab id, title,
profile id, transport id, working directory, and render mode. If the app shuts
down before a deferred tab is opened, the saved workspace uses the original pane
tree instead of flattening or dropping it. Once the tab is selected, the
controller materializes the pane tree and starts its sessions through the normal
active-tab path.

## Tests

Focused coverage:

- `TerminalWorkspaceSerializerTests` for normalization and round trips;
- `InMemoryWorkspaceStore` for controller tests;
- `MainWindowControllerModeStartupTests` for startup restore, lazy inactive-tab
  materialization, deferred split-pane preservation, and shutdown save.
