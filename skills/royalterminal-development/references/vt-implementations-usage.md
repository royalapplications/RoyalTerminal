# VT Implementations Usage

This is the VT reference entrypoint. Load this file first, then open only the specific VT sub-file needed.

## Table Of Contents

- [Scope](#scope)
- [Load Order](#load-order)
- [Decision Guide](#decision-guide)
- [Implementation Workflow](#implementation-workflow)
- [Critical Invariants](#critical-invariants)
- [Validation Gate](#validation-gate)

## Scope

The VT reference set covers:
- VT contracts and preference modes
- managed and native VT implementations
- native provider/factory selection rules
- control/session service integration points
- mode propagation into input encoding and pointer/paste behavior
- demo-specific VT mapping and runtime fallback behavior

## Load Order

1. [`vt-contracts-and-preferences.md`](vt-contracts-and-preferences.md)
2. [`vt-implementations-and-providers.md`](vt-implementations-and-providers.md)
3. [`vt-factory-selection.md`](vt-factory-selection.md)
4. [`vt-control-and-session-integration.md`](vt-control-and-session-integration.md)
5. [`vt-input-mode-propagation.md`](vt-input-mode-propagation.md)
6. [`vt-demo-mapping-and-validation.md`](vt-demo-mapping-and-validation.md)

## Decision Guide

| If you are changing... | Read first | Then read |
|---|---|---|
| `IVtProcessor` contract or mode semantics | [`vt-contracts-and-preferences.md`](vt-contracts-and-preferences.md) | [`vt-control-and-session-integration.md`](vt-control-and-session-integration.md) |
| native processor behavior | [`vt-implementations-and-providers.md`](vt-implementations-and-providers.md) | [`vt-factory-selection.md`](vt-factory-selection.md) |
| fallback or preference behavior | [`vt-factory-selection.md`](vt-factory-selection.md) | [`vt-demo-mapping-and-validation.md`](vt-demo-mapping-and-validation.md) |
| `TerminalControl` VT lifecycle | [`vt-control-and-session-integration.md`](vt-control-and-session-integration.md) | [`vt-input-mode-propagation.md`](vt-input-mode-propagation.md) |
| key/pointer mode-sensitive encoding | [`vt-input-mode-propagation.md`](vt-input-mode-propagation.md) | [`vt-control-and-session-integration.md`](vt-control-and-session-integration.md) |
| endpoint mode source overrides VT mode source | [`endpoint-contracts-and-input-pipeline.md`](endpoint-contracts-and-input-pipeline.md) | [`vt-control-and-session-integration.md`](vt-control-and-session-integration.md) |
| demo mode mapping | [`vt-demo-mapping-and-validation.md`](vt-demo-mapping-and-validation.md) | [`modes-and-settings.md`](modes-and-settings.md) |

## Implementation Workflow

1. Confirm the intended preference mode (`Auto`, `Managed`, `Native`).
2. Confirm factory/provider behavior for that preference.
3. Confirm control/session callback wiring remains symmetric on start/stop.
4. Confirm input adapter still reads the correct mode source.
5. Validate fallback behavior in demo mode selection.
6. Run VT-focused tests and then full solution tests when shared behavior changed.

## Critical Invariants

- `Auto` must never fail solely due to native provider unavailability.
- `Native` must fail when no native provider can create an instance.
- VT callbacks (`Response`, `Bell`, `Title`) must be set before transport start and cleared on stop/failure.
- mode source preference must remain endpoint first, VT bridge second.
- input encoding must use current mode state (cursor/keypad/application modes).

## Validation Gate

Minimum checks:
- VT and mode resolver tests in `tests/RoyalTerminal.Tests`
- terminal control/session integration tests
- integration tests for native VT behavior where applicable
- full `dotnet test RoyalTerminal.sln -c Release` for shared contract changes

Detailed commands:
- [`vt-demo-mapping-and-validation.md`](vt-demo-mapping-and-validation.md)
- [`build-test-validation.md`](build-test-validation.md)

## Code Examples

### End-to-end VT setup for `TerminalControl`

```csharp
using RoyalTerminal.Avalonia.Controls;
using RoyalTerminal.Terminal;

TerminalControl control = new();
control.VtProcessorPreference = VtProcessorPreference.Auto;

await control.StartSessionAsync(new PtyTransportOptions(
    Command: null,
    WorkingDirectory: Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
    Environment: null,
    Dimensions: new TerminalSessionDimensions(120, 40, 1200, 800)));

control.SendInput("printf 'VT ready\\n'\r\n");
```

### Force managed VT for deterministic CI runs

```csharp
control.VtProcessorPreference = VtProcessorPreference.Managed;
```

### Force native VT with strict availability requirements

```csharp
control.VtProcessorPreference = VtProcessorPreference.Native;
// Start will throw if no native VT provider can be created.
```

### Validate active transport and VT mode from control state

```csharp
if (control.HasActiveSession)
{
    Console.WriteLine($"Transport={control.ActiveTransportId}, NativeVT={control.IsUsingNativeVtProcessor}");
}
```
