# VT Control And Session Integration

## Table Of Contents

- [TerminalControl VT Lifecycle](#terminalcontrol-vt-lifecycle)
- [Session Service Callback Lifecycle](#session-service-callback-lifecycle)
- [Mode Source Bridge](#mode-source-bridge)
- [Start And Stop Integration](#start-and-stop-integration)
- [Resize Integration](#resize-integration)
- [Integration Invariants](#integration-invariants)

## TerminalControl VT Lifecycle

Primary file:
- `src/RoyalTerminal.Avalonia/Controls/TerminalControl.cs`

Key control behavior:
- creates VT processor in `InitializeTerminal()` using current `VtProcessorPreference`
- tracks last-applied preference in `_appliedVtProcessorPreference`
- applies preference changes through:
  - `ApplyVtProcessorPreference()`
  - `EnsureVtProcessorPreferenceApplied()`
- does not swap processor while a transport session is active

Preference application rule:
- if session is active, postpone processor replacement until no active transport

## Session Service Callback Lifecycle

Service file:
- `src/RoyalTerminal.Terminal.Services/Services/TerminalSessionService.cs`

Start path:
- VT callbacks are wired before transport starts:
  - `ResponseCallback` -> writes query responses back to transport
  - `BellCallback` -> bell event propagation
  - `TitleCallback` -> title propagation

Stop/failure path:
- callbacks are always cleared when session stops or startup fails
- active VT reference and mode bridge are reset

This symmetry prevents stale callback invocations against stopped transports.

## Mode Source Bridge

Bridge type:
- nested `VtProcessorModeSource` inside `TerminalSessionService`

Role:
- exposes `IVtProcessor.ModeState` as `ITerminalModeSource`
- relays `ModeChanged` events to session consumers

Selection priority:
- endpoint-provided mode source takes precedence
- VT bridge is fallback when no endpoint mode source exists

## Start And Stop Integration

`TerminalControl.StartSessionAsync(...)` integration sequence:
1. `EnsureVtProcessorPreferenceApplied()`
2. reset pointer/mouse tracking state
3. call `TerminalSessionService.StartSessionAsync(...)`
4. set `_activeTransportId` to option transport ID

`TerminalControl.StopPty()` sequence:
1. delegate to session stop
2. clear `_activeTransportId`
3. reset pointer/mouse tracking state

## Resize Integration

`TerminalControl.ApplyTerminalSize(...)` does both:
- local VT resize notification: `_vtProcessor?.NotifyResize(...)`
- transport resize: `TerminalSessionService.ResizeSession(...)`

This keeps local parser state and remote process/session dimensions synchronized.

## Integration Invariants

- preference changes must not silently replace processor during active session.
- callback wiring/clearing must remain symmetric across start, stop, and failure.
- mode source bridge must not outlive current VT processor instance.
- `ResponseCallback` path must continue routing through session service input send.
- active transport ID must be accurate and cleared on exit paths.

## Code Examples

### `TerminalControl` preference + session lifecycle

```csharp
TerminalControl control = new();
control.VtProcessorPreference = VtProcessorPreference.Managed;

await control.StartSessionAsync(new PtyTransportOptions(
    Command: null,
    WorkingDirectory: Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
    Environment: null,
    Dimensions: new TerminalSessionDimensions(100, 30, 1000, 700)));

// Preference changes during active session are deferred.
control.VtProcessorPreference = VtProcessorPreference.Native;

control.StopPty();
```

### VT callback bridge example

```csharp
await sessionService.StartSessionAsync(
    transportFactory,
    options,
    vtProcessor,
    onTransportDataReceived: (data, length) => vtProcessor.Process(data.AsSpan(0, length)),
    onTransportProcessExited: exit => Console.WriteLine($"Exit={exit}"),
    onVtResponse: bytes => sessionService.SendInput(bytes),
    onVtBell: () => Console.WriteLine("BEL"),
    onVtTitleChanged: title => Console.WriteLine($"Title={title}"));
```
