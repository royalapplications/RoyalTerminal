# VT Demo Mapping And Validation

## Table Of Contents

- [Demo VT Mapping](#demo-vt-mapping)
- [Mode Resolution Integration](#mode-resolution-integration)
- [Standalone Session Startup Mapping](#standalone-session-startup-mapping)
- [Validation Matrix](#validation-matrix)
- [Command Checklist](#command-checklist)
- [Source-Of-Truth Guidance](#source-of-truth-guidance)

## Demo VT Mapping

Source:
- `samples/RoyalTerminal.Demo/Services/MainWindowController.cs`

Mapping function:
- `ResolveVtProcessorPreference(TerminalRenderMode mode)`

Mapping table:
- `TerminalRenderMode.NativeVt` -> `VtProcessorPreference.Native`
- `TerminalRenderMode.ManagedVt` -> `VtProcessorPreference.Managed`
- all other modes -> `VtProcessorPreference.Auto`

Practical outcome:
- rendered and native embedded modes still use auto VT preference for standalone `TerminalControl` fallback paths

## Mode Resolution Integration

Mode resolver source:
- `samples/RoyalTerminal.Demo/Services/TerminalModeResolver.cs`

Cycle order:
- `GhosttyRendered -> GhosttyNative -> NativeVt -> ManagedVt -> RenderedAuto`

Capability-gated fallback ensures new tabs select the next supported mode when requested mode is unavailable.

## Standalone Session Startup Mapping

In `StartStandaloneSessionAsync`:
- transport selection is independent from VT preference selection
- VT preference is set at control creation time
- session startup then applies selected transport (`pty`/`pipe`/`ssh`)

This separation means you must validate both axes when changing demo behavior:
- render mode axis (embedded/native/managed choice)
- transport axis (pty/pipe/ssh)

## Validation Matrix

| Change | Minimum Demo Validation |
|---|---|
| VT preference mapping | new-tab startup in each mode, confirm selected processor type |
| mode fallback behavior | unsupported mode request falls forward correctly |
| native availability gating | startup with native unavailable should reach managed/auto path |
| transport + VT combined flow | open sessions for PTY/Pipe/SSH under native and managed VT modes |

## Command Checklist

Targeted unit tests:
```bash
dotnet test tests/RoyalTerminal.Tests/RoyalTerminal.Tests.csproj -c Release --filter "TerminalModeResolverTests|MainWindowControllerModeStartupTests|MainWindowViewModelFlowTests|TerminalControlTests|TerminalInputAdapterTests"
```

Integration/native checks (when native VT changed):
```bash
dotnet test tests/RoyalTerminal.IntegrationTests/RoyalTerminal.IntegrationTests.csproj -c Release --filter "TerminalNativeTests|KeyEncoderTests|PasteTests|OscParserTests|SgrParserTests"
```

Full safety pass:
```bash
dotnet test RoyalTerminal.sln -c Release
```

## Source-Of-Truth Guidance

When comments and behavior diverge:
- prefer implementation files for runtime truth
- verify with tests after updating docs

Runtime-truth anchors:
- `src/RoyalTerminal.Terminal.Vt.Ghostty/Terminal/GhosttyVtProcessor.cs`
- `src/RoyalTerminal.Terminal.Vt.Managed/Terminal/BasicVtProcessor.cs`
- `samples/RoyalTerminal.Demo/Services/MainWindowController.cs`

## Code Examples

### Demo mode -> VT preference mapping assertions

```csharp
Assert.Equal(VtProcessorPreference.Native,
    ResolveVtProcessorPreference(TerminalRenderMode.NativeVt));

Assert.Equal(VtProcessorPreference.Managed,
    ResolveVtProcessorPreference(TerminalRenderMode.ManagedVt));

Assert.Equal(VtProcessorPreference.Auto,
    ResolveVtProcessorPreference(TerminalRenderMode.RenderedAuto));
```

### Fallback verification (resolver test style)

```csharp
TerminalModeCapabilities caps = new(
    EmbeddedGhosttyNativeAvailable: false,
    EmbeddedGhosttyRenderedAvailable: false,
    NativeVtAvailable: false,
    ManagedVtAvailable: true);

TerminalRenderMode resolved = TerminalModeResolver.Default.ResolveSupportedMode(
    TerminalRenderMode.GhosttyRendered,
    caps);

Assert.Equal(TerminalRenderMode.ManagedVt, resolved);
```

### Mode + transport combined smoke workflow

```csharp
_viewModel.SetRenderMode(TerminalRenderMode.NativeVt);
_viewModel.SelectedTransportMode = new TransportModeOption(TerminalTransportIds.Ssh, "SSH");

TerminalControl terminal = CreateStandaloneTerminalControl(TerminalRenderMode.NativeVt);
await StartStandaloneSessionAsync(terminal);
Assert.True(terminal.HasActiveSession);
```
