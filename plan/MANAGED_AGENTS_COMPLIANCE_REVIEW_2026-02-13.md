# Managed Code AGENTS.md Compliance Review (2026-02-13)

## Scope
Reviewed managed code in:
- `src/GhosttySharp`
- `src/GhosttySharp.Avalonia`
- `samples/GhosttySharp.Demo`
- `tests/GhosttySharp.Tests`
- `tests/GhosttySharp.IntegrationTests`

Interpretation note (per project guidance): ReactiveUI requirement is evaluated for the **sample app/presentation layer**, not as a mandatory dependency for reusable library projects.

## Baseline Validation
- Executed: `dotnet test GhosttySharp.sln -c Release`
- Result: **Passed** (193/193 tests; 155 unit/headless + 38 integration)
- Executed: `dotnet test GhosttySharp.sln -c Release --collect:"XPlat Code Coverage"`
- Cobertura summary (from generated reports):
  - `GhosttySharp`: **22.79% line**, **22.87% branch**
  - `GhosttySharp.Avalonia`: **22.22% line**, **14.26% branch**

---

## Executive Summary
Overall compliance with AGENTS.md is **partial to low**.

Strong areas:
- High-performance low-level patterns exist in core paths (`Span<T>`, `ArrayPool<T>`, intrinsics, `LibraryImport`).
- xUnit + Avalonia Headless tests are present and passing.

Major non-compliance areas:
- MVVM and sample-app ReactiveUI requirements are not implemented.
- UI code-behind contains substantial UI logic and imperative event wiring.
- Layer boundaries and Dependency Inversion are weak in the terminal controls.
- “All production code covered by unit tests” requirement is not met (coverage and uncovered classes).

---

## High-Priority Findings (Ordered)

### 1) MVVM strict requirements are not met
**Status:** Non-compliant

Evidence:
- `samples/GhosttySharp.Demo/MainWindow.axaml.cs:56` onward contains extensive UI orchestration and state management in code-behind.
- Event wiring in code-behind: `samples/GhosttySharp.Demo/MainWindow.axaml.cs:69`-`80`.
- Direct control lookup and mutation: `samples/GhosttySharp.Demo/MainWindow.axaml.cs:58`-`67`, `589`, `700`, `706`.
- No ViewModel files in managed projects (`*ViewModel*.cs` absent).

Impact:
- Violates “Views are passive”, “no UI logic in code-behind”, “inputs routed via bindings/commands/behaviors”, and “ViewModels testable/framework-agnostic”.

### 2) ReactiveUI required stack is absent
**Status:** Non-compliant

Evidence:
- No `ReactiveObject`, `ReactiveCommand`, `WhenAnyValue`, `Interaction<,>`, `IScreen`, `IRoutableViewModel` usage in sample app source.
- No `ReactiveUI` / `ReactiveUI.Avalonia` package references in:
  - `samples/GhosttySharp.Demo/GhosttySharp.Demo.csproj`

Impact:
- Direct mismatch with AGENTS.md section “ReactiveUI (required)” for the application/presentation layer.

### 3) Layering and SOLID/DIP violations in control orchestration
**Status:** Partially non-compliant

Evidence:
- `GhosttyTerminalControl` combines input handling, VT processing, rendering coordination, scrolling, selection, clipboard, and PTY lifecycle in one class:
  - `src/GhosttySharp.Avalonia/GhosttyTerminalControl.cs:32`
  - `src/GhosttySharp.Avalonia/GhosttyTerminalControl.cs:271`-`329`
  - `src/GhosttySharp.Avalonia/GhosttyTerminalControl.cs:510` onward
  - `src/GhosttySharp.Avalonia/GhosttyTerminalControl.cs:808` onward
- Concrete type construction inside UI control (`new BasicVtProcessor`, `new GhosttyVtProcessor`, `new WindowsPty`, `new UnixPty`):
  - `src/GhosttySharp.Avalonia/GhosttyTerminalControl.cs:307`, `314`, `328`, `824`, `829`

Impact:
- Multiple reasons to change (SRP), weak extension points (OCP), no abstraction-driven construction (DIP).

### 4) Avalonia binding/styling guidance not followed in app layer
**Status:** Non-compliant

Evidence:
- No binding expressions / no `x:DataType` in XAML binding scopes.
- Inline styles inside view instead of dedicated dictionaries:
  - `samples/GhosttySharp.Demo/MainWindow.axaml:11`-`26`
- UI built/manipulated imperatively in code (`new Button`, `new TextBlock`, etc.):
  - `samples/GhosttySharp.Demo/MainWindow.axaml.cs:303` onward.

Impact:
- Misses compiled-binding MVVM path and maintainable styling architecture expected by AGENTS.md.

### 5) “All production code covered by unit tests” is not satisfied
**Status:** Non-compliant

Evidence:
- Overall managed coverage near ~22% line.
- 0% coverage classes include:
  - `src/GhosttySharp/Ghostty.cs`
  - `src/GhosttySharp/GhosttyApp.cs`
  - `src/GhosttySharp/GhosttyConfig.cs`
  - `src/GhosttySharp/GhosttySurface.cs`
  - `src/GhosttySharp/GhosttyInspector.cs`
  - `src/GhosttySharp.Avalonia/GhosttyNativeTerminalControl.cs`
  - `src/GhosttySharp.Avalonia/GhosttyRenderedTerminalControl.cs`
  - `src/GhosttySharp.Avalonia/Terminal/UnixPty.cs`
  - `src/GhosttySharp.Avalonia/Terminal/WindowsPty.cs`

Impact:
- Current tests validate important pieces, but requirement explicitly expects comprehensive production coverage.

### 6) Custom control property strategy (Styled vs Direct) is overly broad
**Status:** Partially non-compliant

Evidence:
- Heavy use of `StyledProperty` for operational state/config in controls:
  - `src/GhosttySharp.Avalonia/GhosttyTerminalControl.cs:43`-`79`
  - `src/GhosttySharp.Avalonia/GhosttyRenderedTerminalControl.cs:85`-`95`
  - `src/GhosttySharp.Avalonia/GhosttyNativeTerminalControl.cs:51`-`58`

Impact:
- AGENTS recommends `StyledProperty` only when styling participation is needed; `DirectProperty` is preferred for non-styled operational properties.

### 7) Additional correctness/perf risks discovered during review
**Status:** Risk (not AGENTS-only)

Evidence:
- Potential leak/contract bug in selection span API:
  - `src/GhosttySharp/GhosttySurface.cs:359`-`377`
  - Returns `ReadOnlySpan<byte>` from native pointer without corresponding free in success path.
- Hot-path allocation in data event flow:
  - `src/GhosttySharp.Avalonia/GhosttyTerminalControl.cs:481` (`data.ToArray()`).
- Frequent stream allocation in Windows PTY writes:
  - `src/GhosttySharp.Avalonia/Terminal/WindowsPty.cs:116`-`119`.
- Library emits `Console.WriteLine`/`Console.Error.WriteLine` in control runtime paths:
  - `src/GhosttySharp.Avalonia/GhosttyRenderedTerminalControl.cs:228`-`230`, `314`-`315`, `518`-`521`, `648`, `655`.

Impact:
- Memory/resource correctness and runtime overhead risk; noisy logs in reusable library surface.

---

## AGENTS Requirement Matrix

| AGENTS area | Status | Notes |
|---|---|---|
| SOLID (strict) | Partial | Core wrappers are focused; terminal controls are too broad and tightly coupled. |
| MVVM (strict) | Non-compliant | Demo app is code-behind centric; no ViewModels/command binding architecture. |
| Layering & boundaries | Partial | `GhosttySharp` is mostly UI-free, but `GhosttySharp.Avalonia` mixes UI/presentation/infrastructure concerns. |
| Avalonia best practices | Partial/Low | Missing binding-first architecture, inline styles in views, imperative control creation. |
| ReactiveUI (required) | Non-compliant (sample app scope) | Sample app has no ReactiveUI model, base types, commands, or routing usage. |
| DI (if requested) | Not applied | No composition root/DI setup present; requirement conditional in AGENTS. |
| Performance (required) | Partial | Strong low-level APIs present; also avoidable allocations and some expensive paths remain. |
| Reflection/source gen (required) | Partial/Good | Uses `LibraryImport` source-gen; reflection usage minimal and bounded (`NativeLibraryLoader`). |
| Testing & validation | Partial/Low | Good test foundation and passing runs, but broad production coverage gaps remain. |
| Code conventions | Partial | Public API docs exist broadly, but strict no-code-behind-event-handlers rule is violated in demo app. |

---

## Granular Refactoring Plan

## Phase 1 (ReactiveUI + MVVM foundation)
1. **Introduce `Presentation` layer in demo app**
   - Add `MainWindowViewModel : ReactiveObject`.
   - Move fields/state from `MainWindow.axaml.cs` (tabs, theme, mode, font, status).
2. **Add ReactiveUI packages**
   - `ReactiveUI`, `ReactiveUI.Avalonia`, `ReactiveUI.SourceGenerators` to demo project.
3. **Replace code-behind event wiring with commands/interactions**
   - Convert `CreateNewTab`, `CloseTab`, `Copy/Paste`, font/theme/mode toggles into `ReactiveCommand`s.
4. **Add compiled bindings with explicit `x:DataType`**
   - Bind toolbar/context menu/status/tab strip to VM properties/commands.

## Phase 2 (View passivity + XAML structure)
1. **Reduce `MainWindow.axaml.cs` to `InitializeComponent()` plus optional activation hooks only**
   - Move all UI logic to ViewModel/services.
2. **Move inline styles to resource dictionaries**
   - Create `samples/GhosttySharp.Demo/Styles/Tabs.axaml`, merge in `App.axaml`.
3. **Use `StaticResource` / `DynamicResource` for theme colors**
   - Remove repeated hard-coded color literals from view code.

## Phase 3 (Control decomposition for SOLID + DIP)
1. **Extract orchestration services from `GhosttyTerminalControl`**
   - `ITerminalSessionService` (attach/detach surface, lifecycle)
   - `ITerminalInputAdapter` (key/mouse/text mapping)
   - `ITerminalSelectionService` (selection/copy/paste)
   - `ITerminalScrollService` (scroll coordination)
2. **Introduce factories for concrete runtime dependencies**
   - `IVtProcessorFactory`, `IPtyFactory`.
   - Remove direct `new` of `BasicVtProcessor/GhosttyVtProcessor/UnixPty/WindowsPty` from UI control.
3. **Reassess property types (`StyledProperty` vs `DirectProperty`)**
   - Keep style-relevant properties styled.
   - Convert operational properties (where appropriate) to `DirectProperty`.

## Phase 4 (Correctness + performance fixes)
1. **Fix `GhosttySurface.TryReadSelectionUtf8` ownership contract**
   - Replace with safe disposable handle API or copy-on-read API.
   - Add explicit native free semantics.
2. **Eliminate hot-path allocations in data events**
   - Replace `byte[]` event payload with `ReadOnlyMemory<byte>` or pooled buffers.
3. **Refactor `WindowsPty.Write` to reuse stream/writer**
   - Avoid per-write `FileStream` creation.
4. **Replace `Console.*` library logging with injectable logger abstraction**
   - Default no-op logger for library consumers.

## Phase 5 (Testing expansion to AGENTS target)
1. **Add unit tests for currently untested core wrappers**
   - `Ghostty`, `GhosttyApp`, `GhosttyConfig`, `GhosttySurface`, `GhosttyInspector`, `NativeLibraryLoader`.
   - Use adapter abstraction around native API for deterministic tests.
2. **Add tests for `GhosttyRenderedTerminalControl` / `GhosttyNativeTerminalControl`**
   - Headless behavior where possible + platform-conditional integration tests.
3. **Add PTY contract tests**
   - `UnixPty` and `WindowsPty` behavior tests with platform guards.
4. **Add UI flow tests with Avalonia Headless input simulation**
   - Verify tab switching, keyboard shortcuts, copy/paste command flow, mode switching.

## Phase 6 (Documentation/API completeness)
1. **Audit public API docs for field/member-level consistency in native structs/enums**
   - Ensure all exposed public API surfaces have XML docs aligned with AGENTS expectations.
2. **Document performance expectations and profiling evidence**
   - Add `plan/PERF_BASELINES.md` with before/after metrics for key paths.

---

## Suggested Execution Order (Low-risk)
1. Phase 1/2 (MVVM + ReactiveUI in sample app)
2. Phase 3 (control decomposition behind stable public APIs)
3. Phase 4 (correctness/perf fixes)
4. Phase 5/6 (coverage completion + documentation hardening)

---

## Acceptance Criteria for “AGENTS-compliant managed layer”
- No application UI logic in code-behind beyond initialization hooks.
- ReactiveUI command/binding architecture in demo app with compiled bindings and explicit `x:DataType`.
- `GhosttyTerminalControl` responsibilities split behind abstractions/factories.
- No 0%-covered critical production classes, and overall managed coverage is materially improved.
- Resource/style dictionaries centralized and merged through app-level resources.
- Public API docs complete and validated.

---

## Phase 6 Completion (2026-02-14)
- Completed native API documentation audit with field/member-level XML documentation coverage for public native structs/enums in `src/GhosttySharp/Native`.
- Added performance baseline and profiling evidence document:
  - `plan/PERF_BASELINES.md`
