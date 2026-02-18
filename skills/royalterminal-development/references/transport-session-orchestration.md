# Transport Session Orchestration

## Table Of Contents

- [Service Overview](#service-overview)
- [Endpoint Attachment Lifecycle](#endpoint-attachment-lifecycle)
- [Session Start Flow](#session-start-flow)
- [Session Stop Flow](#session-stop-flow)
- [Input Routing Rules](#input-routing-rules)
- [Resize Routing Rules](#resize-routing-rules)
- [Mode Source Bridging](#mode-source-bridging)
- [Stale Transport Cleanup](#stale-transport-cleanup)
- [Integration Constraints](#integration-constraints)

## Service Overview

Implementation:
- `TerminalSessionService`
- `src/RoyalTerminal.Terminal.Services/Services/TerminalSessionService.cs`

Main responsibilities:
- own active transport lifecycle (`Transport`, `Pty`, active callbacks)
- bridge endpoint capabilities (`ITerminalEndpoint`, `ITerminalInputSink`, `ITerminalSelectionSource`, `ITerminalModeSource`)
- route input and resize to correct backend
- expose mode source to input and UI layers

## Endpoint Attachment Lifecycle

`AttachEndpoint(ITerminalEndpoint endpoint)`:
- detaches existing endpoint first
- sets capability interfaces from endpoint type casts
- resolves `ModeSource` preference to endpoint mode source when available

`DetachEndpoint()`:
- clears endpoint + all derived sink/source references
- recalculates mode source fallback

Important:
- endpoint mode source always has priority over VT-derived mode source

## Session Start Flow

Method:
- `StartSessionAsync(...)`

Flow:
1. validate arguments and cancellation
2. release stale inactive transport if needed
3. reject start when an active transport already exists
4. create transport from factory
5. if VT is provided, wire callbacks before start:
   - `ResponseCallback`
   - `BellCallback`
   - `TitleCallback`
6. create/replace VT-based mode source bridge (`VtProcessorModeSource`)
7. subscribe transport events:
   - output bytes
   - process exit
8. call `transport.StartAsync(...)`
9. set `Transport` and `Pty` (when transport implements `ITerminalPtyTransport`)

Failure path:
- unsubscribes transport events
- clears VT callbacks
- clears active VT and mode source bridge
- disposes failed transport instance

## Session Stop Flow

Method:
- `StopSessionAsync(...)`

Flow:
1. clear VT callbacks on explicit or active processor
2. capture active transport snapshot
3. if no transport: clear mode source bridge and return
4. unsubscribe transport events
5. `await transport.StopAsync()`
6. always dispose transport in finally
7. clear `Transport`, `Pty`, and mode bridge

Compatibility wrapper methods:
- `StartPty(...)` and `StopPty(...)` call async methods synchronously for legacy usage

## Input Routing Rules

`SendInput(string)` precedence:
1. endpoint path: `Endpoint.SendText(utf8)`
2. active transport path:
   - PTY transport: write string through `ptyTransport.Pty.Write(...)`
   - non-PTY transport: UTF-8 bytes via `Transport.SendInput(...)`
3. legacy direct PTY fallback: `Pty.Write(...)`

`SendInput(ReadOnlySpan<byte>)` precedence:
1. endpoint `SendText(data)`
2. active transport `SendInput(data)`
3. legacy PTY byte write fallback

Design intent:
- endpoint-attached modes can completely own key/pointer/text processing while still using shared control/session APIs

## Resize Routing Rules

`ResizeSession(columns, rows, widthPixels, heightPixels)`:
- releases stale transport first
- no active transport: no-op
- active transport: forwards `TerminalSessionDimensions` to `Transport.Resize(...)`

`ResizePty(...)` delegates to `ResizeSession(...)` for compatibility.

## Mode Source Bridging

Bridge type:
- nested private `VtProcessorModeSource : ITerminalModeSource, IDisposable`

Behavior:
- subscribes to `IVtProcessor.ModeChanged`
- exposes current `ModeState`
- unsubscribes on dispose

Selection rule (`RefreshModeSource`):
- use endpoint mode source when endpoint implements `ITerminalModeSource`
- otherwise use VT bridge if a VT processor is active

## Stale Transport Cleanup

Method:
- `ReleaseInactiveTransportIfNeeded()`

Purpose:
- recover when `Transport` exists but is no longer running
- clear VT callbacks and mode bridge
- best-effort dispose stale transport
- reset `Transport` and `Pty`

This keeps repeated start/stop flows resilient even if exits happen outside normal stop paths.

## Integration Constraints

- do not bypass `StartSessionAsync`/`StopSessionAsync` from `TerminalControl`.
- VT callbacks must remain wired before transport start and cleared on all stop/failure paths.
- use the same transport event delegate instances for start/stop subscribe-unsubscribe pairs.
- when changing input routing precedence, update `DefaultTerminalInputAdapter` docs/tests too.
- when changing mode bridge behavior, validate mouse/paste/key-encoding paths that depend on `ModeSource`.

## Code Examples

### Direct `ITerminalSessionService` lifecycle usage

```csharp
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Services;

ITerminalSessionService session = new TerminalSessionService();
IVtProcessor vt = vtFactory.Create(screen, VtProcessorPreference.Auto);

Action<byte[], int> onData = (data, length) =>
{
    ReadOnlySpan<byte> payload = data.AsSpan(0, length);
    vt.Process(payload);
};

Action<int> onExit = exitCode => Console.WriteLine($"Exited: {exitCode}");
Action<byte[]> onResponse = response => session.SendInput(response);
Action onBell = () => Console.Beep();
Action<string> onTitleChanged = title => Console.WriteLine($"Title: {title}");

await session.StartSessionAsync(
    transportFactory: factory,
    transportOptions: options,
    vtProcessor: vt,
    onTransportDataReceived: onData,
    onTransportProcessExited: onExit,
    onVtResponse: onResponse,
    onVtBell: onBell,
    onVtTitleChanged: onTitleChanged);

session.SendInput("ls -la\r\n");
session.ResizeSession(140, 45, 1400, 900);

await session.StopSessionAsync(
    vt,
    onTransportDataReceived: onData,
    onTransportProcessExited: onExit);
```

### Endpoint-first input routing scenario

```csharp
session.AttachEndpoint(endpoint); // endpoint implements ITerminalInputSink
session.SendInput("echo endpoint-path\n");
// SendInput goes to endpoint first, not directly to transport.
```
