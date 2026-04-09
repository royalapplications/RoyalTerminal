# Architecture Guardrails

## Table Of Contents

- [Non-Negotiable Principles](#non-negotiable-principles)
- [Layer Boundaries](#layer-boundaries)
- [UI And MVVM Rules](#ui-and-mvvm-rules)
- [Terminal Session Architecture Rules](#terminal-session-architecture-rules)
- [ReactiveUI Rules](#reactiveui-rules)
- [Performance Rules](#performance-rules)
- [Native Interop Rules](#native-interop-rules)
- [Testing Rules](#testing-rules)
- [Review Checklist](#review-checklist)

## Non-Negotiable Principles

Apply strictly:
- SOLID
- MVVM
- explicit abstractions for runtime boundaries

Practical interpretation in this repository:
- controls compose services/contracts, they do not own transport policy logic.
- transport and VT logic live behind interfaces and factories.
- demo-specific orchestration stays in sample layer, not core packages.

## Layer Boundaries

Required dependency direction:
- UI (`RoyalTerminal.Avalonia`, XAML/views) -> Presentation (`ViewModel`/controller orchestration)
- Presentation -> Domain/service abstractions (`RoyalTerminal.Terminal.*` contracts/services)
- Infrastructure/native providers -> consumed via interfaces/factories

Do not:
- reference Avalonia types from domain contracts/packages
- leak sample-specific behavior into shared `src/` contracts

## UI And MVVM Rules

- keep view code-behind minimal (framework plumbing only).
- route user actions through commands, interactions, and services.
- keep `TerminalControl` reusable and backend-neutral.
- keep low-level Ghostty interop isolated to dedicated native/interop packages; do not reintroduce a product macOS-only Ghostty control package.

For sample app:
- `MainWindowViewModel` exposes state/command surface.
- `MainWindowController` performs composition, mode fallback, and transport startup orchestration.

## Terminal Session Architecture Rules

- all transport startup must route through `TerminalControl.StartSessionAsync(...)` or typed wrappers.
- `TerminalSessionService` owns active transport lifecycle and callback wiring.
- input routing precedence must remain deterministic:
  - endpoint input sink
  - active transport
  - legacy direct PTY fallback
- mode source precedence must remain deterministic:
  - endpoint mode source
  - VT processor mode bridge

## ReactiveUI Rules

- ViewModels derive from `ReactiveObject`.
- commands use `ReactiveCommand`.
- UI side-effects and cross-thread updates use interactions/services, not random event handler logic.
- sample command/interaction surface is in `MainWindowViewModel`; controller registers handlers.

## Performance Rules

Hot paths in this repo:
- VT processing (`BasicVtProcessor`, `GhosttyVtProcessor`)
- rendering loops (Skia + interop paths)
- transport read loops

Rules:
- avoid allocation-heavy patterns in per-frame/per-byte paths.
- prefer pooled buffers (`ArrayPool<byte>`) in read loops.
- avoid LINQ in frequently executed loops.
- preserve lock granularity on shared terminal screen state.

## Native Interop Rules

- keep native loading deterministic and centralized (`NativeLibraryLoader`, renderer loader).
- do not add ad-hoc `DllImport` loading heuristics outside loader classes.
- preserve RID-aware runtime packaging layout.
- treat architecture mismatches and symbol-version mismatches as first-class failure modes.

## Testing Rules

Always add or update tests with behavior changes.

Minimum targets by subsystem:
- transport/session: `TerminalTransportFactoryTests`, `TerminalSessionServiceTransportTests`, provider tests
- VT/input: `TerminalInputAdapterTests`, VT parser tests, mode resolver tests
- native/interp: rendering interop tests, package boundary tests, native integration tests

Run full solution tests for shared contract changes.

## Review Checklist

For every PR touching core terminal behavior, confirm:

1. boundaries stayed clean (no UI leakage into domain).
2. session lifecycle remains start/stop symmetric.
3. callback subscriptions/unsubscriptions are balanced.
4. mode and transport selection paths remain deterministic.
5. docs and tests were updated with the behavior change.
6. native runtime assumptions (RID/arch/layout) still hold.

Primary anchor files:
- `src/RoyalTerminal.Avalonia/Controls/TerminalControl.cs`
- `src/RoyalTerminal.Terminal.Services/Services/TerminalSessionService.cs`
- `src/RoyalTerminal.Terminal/Terminal/TransportOptions.cs`
- `src/RoyalTerminal.Terminal/Terminal/VtProcessorPreference.cs`
- `samples/RoyalTerminal.Demo/Services/MainWindowController.cs`
- `samples/RoyalTerminal.Demo/Services/TerminalModeResolver.cs`

## Code Examples

### ViewModel command and interaction pattern (ReactiveUI)

```csharp
public sealed class SessionViewModel : ReactiveObject
{
    public Interaction<Unit, Unit> StartSessionInteraction { get; } = new();
    public ReactiveCommand<Unit, Unit> StartSessionCommand { get; }

    public SessionViewModel()
    {
        StartSessionCommand = ReactiveCommand.CreateFromObservable(
            () => StartSessionInteraction.Handle(Unit.Default));
    }
}
```

### Controller orchestration without code-behind logic

```csharp
disposables.Add(viewModel.StartSessionInteraction.RegisterHandler(async ctx =>
{
    await terminalControl.StartSessionAsync(options);
    ctx.SetOutput(Unit.Default);
}));
```

### Respect layer boundaries with interfaces

```csharp
public sealed class TerminalFacade
{
    private readonly ITerminalSessionService _session;
    private readonly ITerminalTransportFactory _transportFactory;

    public TerminalFacade(ITerminalSessionService session, ITerminalTransportFactory transportFactory)
    {
        _session = session;
        _transportFactory = transportFactory;
    }
}
```
