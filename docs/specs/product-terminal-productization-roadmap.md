---
title: Product Terminal Productization Roadmap
---

# Product Terminal Productization Roadmap

Date: 2026-06-30

## Goal

Turn the current RoyalTerminal demo into a shippable, resource-conscious terminal
application surface while preserving the library-first architecture. The product
experience should address common terminal-app expectations:

- restore saved workspaces,
- launch saved sessions quickly,
- remember command history through shell integration,
- offer command suggestions and snippets without breaking native shell behavior,
- support modern tabs and future pane layouts,
- keep startup and idle resource use low.

## Current Baseline

RoyalTerminal already has:

- persisted terminal session profiles through `TerminalSessionProfilesDocument`,
  `TerminalSessionProfileSerializer`, and `JsonFileTerminalSessionProfileStore`;
- transport profiles for PTY, pipe, SSH, raw TCP, Telnet, and serial sessions;
- shell discovery for common local shells;
- tab management in the Avalonia demo app;
- scrollback/history preservation inside a live `TerminalControl`;
- plain Tab passthrough to the shell, so shell-native completion continues to work;
- capture/replay for terminal sessions;
- rendering and highlighting benchmark coverage.

The current gap is product-level workflow state, not terminal emulation. Profiles
describe how to start sessions. They do not describe what workspace was open.
Scrollback history describes terminal output. It does not describe shell command
history or predictive completion.

## Implementation Status

Implemented on 2026-06-30:

- versioned workspace document/store contracts and JSON persistence;
- versioned command-history document/store contracts and JSON persistence;
- shell integration parser for OSC 7 and OSC 133 semantic prompt events;
- managed VT shell-integration event source, Ghostty VT OSC 7/133 pre-parser,
  and `TerminalControl` relay;
- command-history capture from completed shell-integrated commands;
- command suggestion provider/service based on persisted command history,
  profile/transport request scope, working-directory affinity, frequency,
  query-quality scoring, profile-scoped snippets, and built-in snippets;
- opt-in shell integration bootstrap script builder for bash, zsh, fish, and
  PowerShell plus SSH bootstrap composition support;
- sample app workspace restore on startup and save on controller shutdown;
- lazy inactive-tab workspace restore with metadata-only placeholders and
  deferred pane/control/session materialization on first activation;
- lean single-tab startup by default with diagnostic multi-renderer startup behind
  `ROYALTERMINAL_DEMO_START_ALL_RENDER_MODES`;
- compact terminal-first sample shell UI with left rail, command bar, tab strip,
  terminal surface, diagnostics, and status bar;
- visible profile launcher flyout backed by persisted session profiles and shell
  discovery;
- command-history overlay with explicit `Ctrl+Space` open and `Ctrl+Enter`
  insertion, preserving native Tab passthrough;
- per-control launch configuration capture so restored tabs and panes do not race
  on mutable global view-model state during session startup;
- split-pane workspace restore for persisted pane trees and split-pane preservation
  on shutdown;
- interactive split-pane creation to the right or down, pane focus movement,
  keyboard-driven pane resizing, and live splitter-ratio persistence on shutdown;
- macOS application menu replacement for About, Preferences, and Quit plus
  extended-titlebar command bar hit-test roles for native drag, double-click, and
  titlebar gestures;
- granular documentation articles for workspace restore, split panes, shell
  integration, command history/suggestions, and the demo product shell;
- focused tests for workspace, command history, shell integration, product startup,
  restore/save, launcher/history command wiring, split-pane preservation,
  interactive pane commands, titlebar/native-menu integration, and settings
  isolation.

Future hardening outside this roadmap:

- expose profile-scoped snippets in a full editor UI instead of JSON/profile
  persistence only;
- replace Ghostty VT managed pre-parse with native shell-event callbacks if
  libghostty-vt exposes them in the future;
- add more shell-specific bootstrap fixtures as shell integrations evolve.

## Product Requirements

### Workspace Restore

Add a durable workspace document that records:

- workspace format version;
- windows;
- selected window;
- tabs within each window;
- selected tab per window;
- tab profile id;
- tab title;
- tab transport id;
- tab working directory;
- tab render mode;
- future pane layout data.

Workspace restore must be separate from session profiles. Profiles remain reusable
connection templates. Workspace state records the user's last open layout.

### Session Launcher

The sample app should expose saved sessions as first-class launch targets:

- searchable profile launcher;
- recent sessions;
- favorite or pinned sessions;
- new tab from selected profile;
- open restored workspace on startup.

### Lean Startup

The shippable app mode should start one terminal tab by default or restore a saved
workspace. The current demo behavior that opens one tab per supported render mode
is useful for diagnostics, but too expensive as a product default.

Implementation rule:

- product startup: one tab or restored workspace;
- diagnostic/demo mode: optional multi-mode startup.

### Command History

Add app-level command history as structured shell-integration data, not raw
keystroke scraping.

History entries should include:

- profile id;
- transport id;
- shell id;
- working directory;
- command text;
- start timestamp;
- completion timestamp;
- exit code when known.

### Shell Integration

Add opt-in shell integration before implementing suggestions. The integration
layer should capture:

- OSC 7 working-directory updates;
- OSC 133 semantic prompt boundaries;
- command start;
- command finish;
- exit status where available.

This should follow reference-terminal behavior: terminals can observe structured
prompt and command events, but shells remain the source of truth for command
execution and native completion.

### Suggestions And Snippets

After command-history capture exists, add:

- searchable command history overlay;
- profile-scoped command snippets;
- command suggestion providers;
- explicit acceptance key or configurable Tab interception;
- native Tab passthrough as the default behavior.

### Split Panes

Add pane modeling after workspace restore is stable:

- persisted split orientation;
- persisted split ratio;
- persisted child panes;
- pane profile id;
- pane title;
- pane working directory;
- pane restore policy.

Runtime restore now builds the persisted split tree and saves it back without
flattening. Runtime creation now supports splitting the active pane right or
down, directional pane focus, keyboard-driven split resizing, and capturing live
splitter ratio changes from the Avalonia grid before workspace save.

### Product UI

Redesign the sample app shell around common terminal-app patterns:

- terminal area is the primary surface;
- compact command bar or left rail for high-frequency actions;
- tab strip remains visible and scannable;
- saved sessions and profile launcher are prominent;
- search, capture, settings, and diagnostics are discoverable but not dominant;
- avoid nested cards and oversized marketing-style layout;
- use resource dictionaries for reusable tokens and component styling;
- keep bindings compiled and viewmodel-driven.

### Resource-Conscious Mode

Expose a resource-sensitive product profile:

- single-tab startup;
- lazy inactive-tab initialization for restored workspace tabs;
- optional native/render backend initialization only when selected;
- suspend expensive inactive-tab work;
- keep text highlighting static/cached by default;
- expose simple diagnostics for active sessions, render mode, scrollback size,
  and capture state.

## Architecture Slices

### Core Terminal Package

Add durable, UI-independent contracts:

- `TerminalWorkspaceDocument`;
- `TerminalWorkspaceWindow`;
- `TerminalWorkspaceTab`;
- `TerminalWorkspacePane`;
- `TerminalWorkspaceSerializer`;
- `ITerminalWorkspaceStore`;
- `JsonFileTerminalWorkspaceStore`;
- `TerminalWorkspaceStoreFactory`;
- `TerminalCommandHistoryEntry`;
- `TerminalCommandHistoryDocument`;
- `TerminalCommandHistorySerializer`;
- `ITerminalCommandHistoryStore`;
- `JsonFileTerminalCommandHistoryStore`;
- `TerminalCommandHistoryStoreFactory`;
- `TerminalCommandSuggestionService`;
- `TerminalCommandSuggestionRequest`;
- `ITerminalCommandSuggestionProvider`;
- `TerminalCommandSnippet`;
- `TerminalCommandSnippets`;
- `TerminalShellIntegrationBootstrapBuilder`;
- `TerminalShellIntegrationBootstrapOptions`;
- `TerminalShellIntegrationParser`;
- `ITerminalShellIntegrationEventSource`;
- `TerminalShellIntegrationEventArgs` and event records.

### Avalonia Demo App

Integrate product workflows without moving business logic into views:

- load workspace on startup;
- save workspace on shutdown/dispose;
- expose launcher commands in `MainWindowViewModel`;
- keep terminal operation routing in `MainWindowController`;
- preserve existing capture/search/settings functionality;
- add tests around startup mode and command wiring.

### Tests

Add focused xUnit coverage for:

- workspace document normalization and validation;
- workspace store save/load round trip;
- command history document normalization and retention limits;
- command history store save/load round trip;
- shell integration event contracts;
- sample app single-tab startup mode;
- UI binding smoke tests for newly added launcher/command surfaces.

## Reference Decisions

### Ghostty

Ghostty injects shell integration when configured and uses semantic prompt data,
including OSC 133, for prompt-aware features. RoyalTerminal should follow this
pattern: capture structured shell events, then build command-history and suggestion
features on top of those events.

### Windows Terminal

Windows Terminal treats profiles, startup actions, tabs, and panes as product
workspace concepts. RoyalTerminal should similarly keep workspace restore separate
from session profile definitions.

### PowerShell

PowerShell and PSReadLine own command completion and prediction. RoyalTerminal
should not guess PowerShell completion semantics. It should interoperate through
shell-produced events or a dedicated PowerShell integration path.

### xterm.js

xterm.js keeps terminal core extensible through add-ons such as search and
serialization. RoyalTerminal should keep workspace restore, command history, and
suggestions as app-layer services around `TerminalControl`, not hidden VT-parser
behavior.

## First Milestone

1. Add workspace document/store contracts.
2. Add command history and shell integration contracts.
3. Switch the sample app startup path to a lean single-tab product default.
4. Add tracked UI state for restored sessions and future profile launcher flows.
5. Redesign the sample shell to a compact terminal-product layout.
6. Add focused serializer/store and sample startup tests.

This milestone makes "save sessions" true at the product level and creates the
safe foundation for command history and command suggestions.

Status: delivered for the core contracts and sample-app integration listed above.

## Documentation Milestone

Documentation is split by topic so each public feature can be maintained without
overloading a single productization article:

1. [Workspace Restore](/articles/workspace-restore)
2. [Split Panes](/articles/split-panes)
3. [Shell Integration](/articles/shell-integration)
4. [Command History And Suggestions](/articles/command-history-and-suggestions)
5. [Demo Product Shell](/articles/demo-product-shell)
