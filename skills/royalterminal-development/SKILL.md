---
name: royalterminal-development
description: End-to-end engineering workflow for the RoyalTerminal repository covering architecture guardrails, render and VT mode selection, transport/session settings, native library integration, and build and test validation. Use when implementing, reviewing, or debugging changes in src, samples, native, scripts, or tests, especially for Avalonia MVVM and ReactiveUI patterns, terminal modes and fallbacks, PTY and Pipe and SSH behavior, renderer interop, and cross-platform runtime configuration.
---

# RoyalTerminal Development

## Overview

Use this skill to execute RoyalTerminal work predictably across UI, terminal engine, rendering, transport, native interop, and test layers.

## Load References

Load [`references/architecture-guardrails.md`](references/architecture-guardrails.md) first on every task.
Load [`references/modes-catalog.md`](references/modes-catalog.md) when behavior depends on mode selection, mode fallback, or mode-specific settings.
Load [`references/control-types-catalog.md`](references/control-types-catalog.md) when the request touches any Avalonia control or control composition.
Load [`references/contracts-implementations-catalog.md`](references/contracts-implementations-catalog.md) when changing interfaces, contracts, factories, providers, services, or adapters.
Load [`references/endpoint-contracts-and-input-pipeline.md`](references/endpoint-contracts-and-input-pipeline.md) when changing endpoint capabilities, input normalization, or endpoint-vs-transport routing precedence.
Load [`references/rendering-system-usage.md`](references/rendering-system-usage.md) first for any rendering task, then load only the rendering sub-files it references.
Load [`references/rendering-interop-and-backends.md`](references/rendering-interop-and-backends.md) directly when changing render contracts, interop backend selection, descriptor validation, or GPU/CPU fallback behavior.
Load [`references/transport-types-usage-setup.md`](references/transport-types-usage-setup.md) first for any transport task, then load only needed transport subfiles it references.
Load [`references/vt-implementations-usage.md`](references/vt-implementations-usage.md) first for any VT task, then load only needed VT subfiles it references.
Load [`references/native-libraries-setup.md`](references/native-libraries-setup.md) first for any native-library task, then load only needed native subfiles it references.
Load [`references/modes-and-settings.md`](references/modes-and-settings.md) for operational toggles, transport runtime settings, and quick mode summaries.
Load [`references/build-test-validation.md`](references/build-test-validation.md) before running validation and whenever native build or CI parity matters.

## Reference Index

Core:
- [`references/architecture-guardrails.md`](references/architecture-guardrails.md)
- [`references/modes-catalog.md`](references/modes-catalog.md)
- [`references/control-types-catalog.md`](references/control-types-catalog.md)
- [`references/contracts-implementations-catalog.md`](references/contracts-implementations-catalog.md)
- [`references/endpoint-contracts-and-input-pipeline.md`](references/endpoint-contracts-and-input-pipeline.md)
- [`references/modes-and-settings.md`](references/modes-and-settings.md)

Transport:
- [`references/transport-types-usage-setup.md`](references/transport-types-usage-setup.md)
- [`references/transport-contracts-and-options.md`](references/transport-contracts-and-options.md)
- [`references/transport-implementations-and-providers.md`](references/transport-implementations-and-providers.md)
- [`references/transport-session-orchestration.md`](references/transport-session-orchestration.md)
- [`references/transport-setup-patterns.md`](references/transport-setup-patterns.md)
- [`references/transport-ssh-credentials-and-trust.md`](references/transport-ssh-credentials-and-trust.md)
- [`references/transport-entrypoints-and-validation.md`](references/transport-entrypoints-and-validation.md)

VT:
- [`references/vt-implementations-usage.md`](references/vt-implementations-usage.md)
- [`references/vt-contracts-and-preferences.md`](references/vt-contracts-and-preferences.md)
- [`references/vt-implementations-and-providers.md`](references/vt-implementations-and-providers.md)
- [`references/vt-factory-selection.md`](references/vt-factory-selection.md)
- [`references/vt-control-and-session-integration.md`](references/vt-control-and-session-integration.md)
- [`references/vt-input-mode-propagation.md`](references/vt-input-mode-propagation.md)
- [`references/vt-demo-mapping-and-validation.md`](references/vt-demo-mapping-and-validation.md)

Rendering:
- [`references/rendering-system-usage.md`](references/rendering-system-usage.md)
- [`references/rendering-managed-pipeline.md`](references/rendering-managed-pipeline.md)
- [`references/rendering-text-shaping-font-fallback-and-diagnostics.md`](references/rendering-text-shaping-font-fallback-and-diagnostics.md)
- [`references/rendering-interop-and-backends.md`](references/rendering-interop-and-backends.md)

Native:
- [`references/native-libraries-setup.md`](references/native-libraries-setup.md)
- [`references/native-library-inventory.md`](references/native-library-inventory.md)
- [`references/native-library-usage-mapping.md`](references/native-library-usage-mapping.md)
- [`references/native-loader-resolution.md`](references/native-loader-resolution.md)
- [`references/native-package-layout.md`](references/native-package-layout.md)
- [`references/native-build-scripts-and-outputs.md`](references/native-build-scripts-and-outputs.md)
- [`references/native-runtime-checklist-and-troubleshooting.md`](references/native-runtime-checklist-and-troubleshooting.md)

Validation:
- [`references/build-test-validation.md`](references/build-test-validation.md)

## Workflow

1. Classify the request.
- UI and behavior wiring in Avalonia or ViewModel code.
- Terminal session and transport behavior in `TerminalControl` and services.
- VT and rendering behavior in managed/native VT and Skia/interop paths.
- Native build and runtime resolution in `native/` and `scripts/`.

2. Choose affected modes and settings up front.
- Decide whether the change touches `GhosttyRendered`, `GhosttyNative`, `NativeVt`, `ManagedVt`, or `RenderedAuto`.
- Decide whether transport settings are `pty`, `pipe`, or `ssh`.
- Decide whether renderer settings must preserve `CpuCellRenderer` and `TextureInterop` behavior.

3. Implement with project guardrails.
- Keep Views passive and move logic to ViewModels, services, and domain layers.
- Use compiled bindings and explicit `x:DataType` for XAML binding scopes.
- Use ReactiveUI primitives (`ReactiveObject`, `ReactiveCommand`, `Interaction`) instead of code-behind events.
- Prefer composition over inheritance except framework base types.
- Avoid reflection; if unavoidable, ask explicitly first.

4. Validate by change type.
- Always run a focused build and targeted tests first.
- Run full solution tests when touching shared contracts, transports, VT processing, or renderer plumbing.
- For native/runtime changes, run native build scripts and mode-relevant integration tests.

5. Report outcomes concretely.
- Name exact mode and setting combinations validated.
- State fallback behavior impact when mode resolver or capability checks changed.
- Include commands run and what was not run.

## Change Playbooks

### Add or Change a Mode

Update mode enums and resolver logic, then update UI mode state and labels, then update documentation and tests.
Verify fallback order remains deterministic and runnable on unsupported platforms.

### Add or Change Transport Options

Update option records in `src/RoyalTerminal.Terminal/Terminal/TransportOptions.cs` and transport provider/service wiring.
Propagate settings through demo ViewModel and controller only via bindings and commands.
Validate `pty`, `pipe`, and `ssh` behavior independently.

### Add or Change Renderer Settings

Keep `GhosttyRenderedTerminalRenderingMode` behavior explicit for CPU and interop paths.
Preserve CPU fallback behavior for interop when target validation fails.
Validate shaping and diagnostics toggles, and do not introduce reflection-based backend probing.

### Add or Change Native Interop

Keep runtime probing deterministic and cross-platform.
Preserve environment override support for explicit library path and directory.
Build native artifacts and verify runtime placement under the corresponding `runtimes/<rid>/native` paths.

## Definition Of Done

The implementation follows AGENTS constraints and does not place UI logic in code-behind.
The affected mode and setting combinations are identified and validated.
Targeted tests pass, and broader test coverage is run for shared-path changes.
Documentation and references are updated when mode, setting, transport, or runtime behavior changes.
