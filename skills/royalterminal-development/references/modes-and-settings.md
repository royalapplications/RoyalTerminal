# Modes And Settings

## Table Of Contents

- [Integration Render Modes](#integration-render-modes)
- [Mode Availability And Fallback](#mode-availability-and-fallback)
- [VT Preference Settings](#vt-preference-settings)
- [Rendered Backend Settings](#rendered-backend-settings)
- [Transport Settings](#transport-settings)
- [Demo Default Settings](#demo-default-settings)
- [Runtime Environment Toggles](#runtime-environment-toggles)
- [SSH Integration Test Environment](#ssh-integration-test-environment)
- [Operational Guidelines](#operational-guidelines)
- [File Anchors](#file-anchors)

## Integration Render Modes

Demo enum:
- `TerminalRenderMode` in `samples/RoyalTerminal.Demo/Services/TerminalModeResolver.cs`

Values and primary runtime mapping:
- `GhosttyRendered` -> `GhosttyRenderedTerminalControl`
- `GhosttyNative` -> `GhosttyNativeTerminalControl`
- `NativeVt` -> `TerminalControl` + `VtProcessorPreference.Native`
- `ManagedVt` -> `TerminalControl` + `VtProcessorPreference.Managed`
- `RenderedAuto` -> `TerminalControl` + `VtProcessorPreference.Auto`

Cycle order:
- `GhosttyRendered -> GhosttyNative -> NativeVt -> ManagedVt -> RenderedAuto`

## Mode Availability And Fallback

Capability gates:
- embedded Ghostty modes depend on embedded Ghostty initialization (demo currently macOS-oriented)
- native VT depends on `GhosttyVtProcessor.IsAvailable()`
- managed VT is always available
- `RenderedAuto` is always supported fallback

Resolver behavior:
- keep requested mode if supported
- otherwise advance in cycle order to next supported mode
- fallback never returns unsupported mode

## VT Preference Settings

Enum:
- `VtProcessorPreference` in `src/RoyalTerminal.Terminal/Terminal/VtProcessorPreference.cs`

Settings:
- `Auto`: prefer native provider, fallback to managed
- `Managed`: force managed `BasicVtProcessor`
- `Native`: require native provider and fail when unavailable

`TerminalControl` state fields:
- `HasActiveSession`
- `ActiveTransportId`
- `IsUsingNativeVtProcessor`

## Rendered Backend Settings

Enum:
- `GhosttyRenderedTerminalRenderingMode`
- `src/RoyalTerminal.Avalonia.Ghostty/Controls/Common/GhosttyRenderedTerminalRenderingMode.cs`

Values:
- `CpuCellRenderer`
- `TextureInterop`

Demo toggle mapping:
- `UseTextureInterop=false` -> `CpuCellRenderer`
- `UseTextureInterop=true` -> `TextureInterop`

Runtime label mapping in demo:
- "Backend: CPU"
- "Backend: Interop (Preview)"

## Transport Settings

Transport IDs:
- `pty`
- `pipe`
- `ssh`

Option models:
- `PtyTransportOptions`
- `PipeTransportOptions`
- `SshTransportOptions`

SSH settings details:
- `RequestPty` controls shell stream creation mode
- `TerminalType` defaults to `xterm-256color` when empty
- `ExpectedHostKeyFingerprintSha256` accepts with/without `SHA256:` prefix
- authentication mode driven by demo selector IDs:
  - `password`
  - `private-key`
  - `agent`
  - `password-key`

## Demo Default Settings

From `MainWindowViewModel`:
- working directory: user profile
- pipe command text: `echo RoyalTerminal pipe transport`
- pipe stderr merge: `true`
- SSH host: `localhost`
- SSH port: `22`
- SSH username: current user
- SSH terminal type: `xterm-256color`
- SSH request PTY: `true`

UI transport mode defaults:
- selected transport: `PTY`
- selected SSH auth mode: `password`

## Runtime Environment Toggles

Demo renderer toggles:
- `ROYALTERMINAL_DISABLE_TEXT_SHAPING`
- `ROYALTERMINAL_ENABLE_RENDER_DIAGNOSTICS`

Truthy parser values:
- `1`
- `true`
- `yes`
- `on`

Renderer C API loader overrides:
- `GHOSTTY_RENDERER_CAPI_LIBRARY_PATH`
- `GHOSTTY_RENDERER_CAPI_LIBRARY_DIR`

## SSH Integration Test Environment

Required for SSH integration tests:
- `ROYALTERMINAL_IT_SSH_HOST`
- `ROYALTERMINAL_IT_SSH_PORT`
- `ROYALTERMINAL_IT_SSH_USERNAME`
- `ROYALTERMINAL_IT_SSH_PASSWORD`
- `ROYALTERMINAL_IT_SSH_HOST_KEY_SHA256`

Optional:
- `ROYALTERMINAL_IT_SSH_PRIVATE_KEY` (PEM content or file path)

## Operational Guidelines

- set mode first, then create/start terminal session.
- keep transport and VT settings orthogonal (demo does this by design).
- test fallback mode flow any time capabilities or resolver logic changes.
- validate both feature toggles and environment overrides in release-like build outputs.

## File Anchors

Mode and settings anchors:
- `samples/RoyalTerminal.Demo/Services/TerminalModeResolver.cs`
- `samples/RoyalTerminal.Demo/Services/MainWindowController.cs`
- `samples/RoyalTerminal.Demo/ViewModels/MainWindowViewModel.cs`
- `src/RoyalTerminal.Terminal/Terminal/VtProcessorPreference.cs`
- `src/RoyalTerminal.Avalonia.Ghostty/Controls/Common/GhosttyRenderedTerminalRenderingMode.cs`
- `src/RoyalTerminal.Terminal/Terminal/TransportOptions.cs`
- `src/RoyalTerminal.Rendering.Interop.Ghostty/Native/GhosttyRendererNativeLibraryLoader.cs`

## Code Examples

### Set render mode and VT preference for new sessions

```csharp
_viewModel.SetRenderMode(TerminalRenderMode.NativeVt);

TerminalControl terminal = CreateStandaloneTerminalControl(TerminalRenderMode.NativeVt);
terminal.VtProcessorPreference = VtProcessorPreference.Native;
```

### Toggle rendered backend mode from UI

```csharp
_viewModel.ToggleRenderedBackendCommand.Execute().Subscribe();
// switches between CpuCellRenderer and TextureInterop for new rendered tabs
```

### Environment toggle usage

```bash
export ROYALTERMINAL_DISABLE_TEXT_SHAPING=1
export ROYALTERMINAL_ENABLE_RENDER_DIAGNOSTICS=true

dotnet run --project samples/RoyalTerminal.Demo
```

### Build SSH options from UI fields

```csharp
SshTransportOptions options = BuildSshOptions(new TerminalSessionDimensions(120, 40, 1200, 800));
await terminal.StartSshAsync(options);
```
