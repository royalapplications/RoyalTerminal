---
title: Sessions, Profiles, And Settings
---

# Sessions, Profiles, And Settings

RoyalTerminal keeps runtime session control, durable session documents, settings UI, themes, and capture files in separate but cooperating layers. That split is one of the main reasons the project scales beyond a single demo app.

## The runtime session boundary

The runtime boundary is the session service. It is responsible for attaching an endpoint or transport, routing input, exposing selection and mode sources, and applying resize changes.

| Type | Purpose |
| --- | --- |
| `ITerminalSessionService` | Public session-service contract used by hosts and the control. |
| `TerminalSessionInputEventArgs` | Event payload raised when input bytes are sent. |
| `TerminalSessionService` | Default session-service implementation. |

The important design point is that this service does not own the UI. It sits between the UI and the transport/endpoint/VT layers.

## Session profiles are the durable configuration format

The profile model is the durable description of a session, not the running session itself. That makes it usable by the settings UI, the demo, file-based persistence, and custom applications.

### Profile document types

| Type | Purpose |
| --- | --- |
| `TerminalSessionProfilesDocument` | Versioned top-level profile document. |
| `TerminalSessionProfile` | One named profile. |
| `TerminalSessionLayoutSettings` | Grid, viewport, and scrollback settings. |
| `TerminalSessionAppearanceSettings` | Font, size, auto-scroll, and opacity settings. |
| `TerminalSessionBehaviorSettings` | Copy, bell, backspace, shaping, ligature, and paste behavior settings. |
| `TerminalSessionTransportProfile` | All transport-specific settings for a profile. |
| `TerminalSessionPtySettings` | Local PTY profile settings. |
| `TerminalSessionPipeSettings` | Pipe process profile settings. |
| `TerminalSessionSshSettings` | SSH profile settings. |
| `TerminalSessionRawTcpSettings` | Raw TCP profile settings. |
| `TerminalSessionTelnetSettings` | Telnet profile settings. |
| `TerminalSessionSerialSettings` | Serial profile settings. |
| `TerminalSessionSshAuthenticationSettings` | Stored SSH auth settings. |
| `TerminalSessionLoggingSettings` | Session logging settings. |
| `TerminalSessionProxySettings` | Proxy settings stored with the profile. |
| `TerminalSessionLogFormat` | Stored log format enum. |
| `TerminalSessionProxyType` | Stored proxy type enum. |

The model is intentionally broad. It is meant to cover what a terminal application needs to remember between runs, not just what is required to start a shell on the current machine.

## Editing profiles in the UI

The Avalonia settings package is built around those profile records. The controls and state slices are public because real applications often want to host or extend that editor.

| Type | Purpose |
| --- | --- |
| `TerminalSettingsPanel` | Root settings host control. |
| `TerminalSettingsSessionPanel` | Session editor surface. |
| `TerminalSettingsConnectionPanel` | Connection editor surface. |
| `TerminalSettingsTerminalPanel` | Terminal behavior editor surface. |
| `TerminalSettingsAppearancePanel` | Appearance editor surface. |
| `TerminalSettingsSshPanel` | SSH editor surface. |
| `TerminalSettingsLoggingPanel` | Logging editor surface. |
| `TerminalSettingsCategoryStateBase` | Base state slice. |
| `TerminalSettingsPanelState` | Central settings state owner. |
| `TerminalSettingsProfileItem` | Profile picker item. |
| `TerminalSettingsTransportModeOption` | Transport picker option. |
| `TerminalSettingsSshAuthModeOption` | SSH auth mode picker option. |
| `TerminalSettingsSessionState` | Session slice. |
| `TerminalSettingsConnectionState` | Connection slice. |
| `TerminalSettingsTerminalBehaviorState` | Terminal behavior slice. |
| `TerminalSettingsAppearanceState` | Appearance slice. |
| `TerminalSettingsSshState` | SSH slice. |
| `TerminalSettingsLoggingState` | Logging slice. |

The point of the package is not just "draw a settings dialog". It is to give hosts a reusable editor for the same document model the runtime understands.

## Serializing and mapping profiles

The public serializer and store abstractions are how profile documents move between disk, UI, and runtime options.

| Type | Purpose |
| --- | --- |
| `TerminalSessionProfileSerializer` | Static serializer, normalizer, and validator for profile documents. |
| `ITerminalSessionProfileStore` | Async persistence contract for profile documents. |
| `JsonFileTerminalSessionProfileStore` | JSON file-backed profile store. |
| `TerminalSessionProfileStoreFactory` | Factory for default profile stores and file paths. |
| `TerminalSessionProfileMapper` | Converts between durable profiles and runnable transport options. |

That mapping layer is critical. It prevents runtime startup code from having to understand every persisted profile detail directly.

## Themes are documents too

RoyalTerminal treats theme data as a real cross-layer document, not as UI-only colors.

| Type | Purpose |
| --- | --- |
| `TerminalPalette` | Immutable 256-color palette. |
| `TerminalPaletteGenerationMode` | Palette generation strategy enum. |
| `TerminalPaletteGenerator` | Static generator for 256-color palettes. |
| `TerminalOscColorReportFormat` | OSC report format enum. |
| `TerminalTheme` | Immutable terminal theme snapshot. |
| `TerminalThemeParser` | Text theme parser. |
| `TerminalThemeSerializer` | Theme serializer and deserializer. |

This is why the same theme model works in managed VT, native VT, snapshot export, and rendering.

## Capture files and replay data

RoyalTerminal also treats captures as durable documents. That makes them useful for debugging, support, and regression reproduction rather than only for live UI playback.

| Type | Purpose |
| --- | --- |
| `TerminalCaptureEventKind` | Output, input, or resize capture event kind. |
| `TerminalCaptureEvent` | One captured event with a relative timestamp. |
| `TerminalCaptureSession` | Serializable capture session document. |
| `TerminalCaptureRecorder` | Runtime recorder for capture events. |
| `TerminalCaptureSessionSerializer` | Capture session serializer. |
| `TerminalCaptureRuntime` | Avalonia-facing capture and replay runtime bound to a control. |

## Shell profiles and defaults

RoyalTerminal also ships shell profile discovery as a reusable surface. That matters for hosts that want a sane default shell without scattering platform checks everywhere.

| Type | Purpose |
| --- | --- |
| `ShellProfile` | Named shell profile. |
| `IShellProfileCatalog` | Shell profile discovery contract. |
| `DefaultShellProfileCatalog` | Platform-aware default shell catalog. |

## Snapshot export lives next to session documents

Snapshot export is where a live terminal becomes a durable copy. The export contracts are shared across VT processors and stay outside the UI layer for that reason.

| Type | Purpose |
| --- | --- |
| `TerminalSnapshotExportFormat` | Snapshot format enum. |
| `TerminalSnapshotExportExtras` | Extra export detail options. |
| `TerminalSnapshotExportOptions` | Snapshot export request. |
| `ITerminalSnapshotExportSource` | Snapshot export capability contract. |

If the transport article is about running sessions, this article is about remembering them, editing them, exporting them, and replaying them later.
