---
title: Settings Panel API
---

# Settings Panel API

`RoyalApps.RoyalTerminal.Avalonia.Settings` exposes the reusable settings
surface used by the app shell. The package is for hosts that want to edit
RoyalTerminal session profiles without adopting the complete product window.

The settings panel edits state and durable profile data. It does not start
transports directly and it does not require the app-shell package.

## Public Controls

| Type | Purpose |
| --- | --- |
| `TerminalSettingsPanel` | Root settings host control. |
| `TerminalSettingsSessionPanel` | Session name and transport mode editor. |
| `TerminalSettingsConnectionPanel` | Working directory, PTY, pipe, raw TCP, Telnet, serial, and base SSH endpoint editor. |
| `TerminalSettingsTerminalPanel` | Terminal behavior editor. |
| `TerminalSettingsAppearancePanel` | Font, opacity, scroll, and regex text highlighting editor. |
| `TerminalSettingsSshPanel` | SSH auth, host-key trust, proxy, forwarding, X11, keep-alive, and timeout editor. |
| `TerminalSettingsLoggingPanel` | Session logging and event logging editor. |

Include the resource dictionary in your executable or library host:

```xml
<Application.Styles>
  <FluentTheme />
  <StyleInclude Source="avares://RoyalTerminal.Avalonia.Settings/Settings/TerminalSettingsPanel.axaml" />
</Application.Styles>
```

Then host the root panel:

```xml
<settings:TerminalSettingsPanel
    State="{Binding SettingsPanelState}" />
```

## Public State Model

| Type | Purpose |
| --- | --- |
| `TerminalSettingsPanelState` | Central state owner for profile selection, profile CRUD, category selection, dirty state, and status text. |
| `TerminalSettingsCategoryStateBase` | Base type for category-specific state. |
| `TerminalSettingsSessionState` | Session name and transport mode state. |
| `TerminalSettingsConnectionState` | Local and remote connection state. |
| `TerminalSettingsTerminalBehaviorState` | Copy, bell, paste, shaping, ligature, and terminal behavior state. |
| `TerminalSettingsAppearanceState` | Font, opacity, scrollback, and text highlighting state. |
| `TerminalSettingsSshState` | SSH authentication, trust, proxy, port forwarding, X11, and timeout state. |
| `TerminalSettingsLoggingState` | Session logging and event-log state. |
| `TerminalSettingsHighlightRuleState` | Editable regex highlighting rule state. |

Option records exposed by the package:

| Type | Purpose |
| --- | --- |
| `TerminalSettingsProfileItem` | Profile picker entry. |
| `TerminalSettingsTransportModeOption` | Transport picker entry. |
| `TerminalSettingsFontSourceOption` | Font source picker entry. |
| `TerminalSettingsFontEdgingOption` | Font edging picker entry. |
| `TerminalSettingsFontHintingOption` | Font hinting picker entry. |
| `TerminalSettingsTextHighlightingModeOption` | Regex highlighting mode picker entry. |
| `TerminalSettingsSshAuthModeOption` | SSH authentication mode picker entry. |

## Data Flow

The settings state is intentionally UI-facing. Hosts should map between the
state object and durable profile documents:

1. Load `TerminalSessionProfilesDocument` from a store.
2. Populate `TerminalSettingsPanelState`.
3. Let the user edit settings state.
4. Validate and normalize through `TerminalSessionProfileSerializer`.
5. Save the updated profile document.
6. Start or restart sessions using `TerminalSessionProfileMapper`.

The reusable app shell already does this mapping for its own settings overlay.
Custom hosts can use the same state objects without adopting `MainWindow`.

## Relationship To Runtime Profiles

Durable profile records live in `RoyalApps.RoyalTerminal.Terminal`:

- `TerminalSessionProfile`;
- `TerminalSessionTransportProfile`;
- `TerminalSessionAppearanceSettings`;
- `TerminalSessionBehaviorSettings`;
- `TerminalSessionLoggingSettings`;
- `TerminalSessionTextHighlightRule`.

The settings panel state mirrors those profile fields for editing, but it stays
separate from runtime session services. This keeps the UI reusable and avoids
transport-specific logic in the control templates.

## Related Articles

- [Sessions, Profiles, And Settings](/articles/sessions-profiles-and-settings)
- [Regex Text Highlighting](/articles/text-highlighting)
- [Transports And Remote Access](/articles/transports)
- [App Shell Hosting](/articles/app-shell-hosting)
