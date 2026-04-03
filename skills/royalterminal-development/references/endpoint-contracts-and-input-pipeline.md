# Endpoint Contracts And Input Pipeline

## Table Of Contents

- [Scope And Primary Files](#scope-and-primary-files)
- [Contract Surface](#contract-surface)
- [Capability Composition Model](#capability-composition-model)
- [Session-Service Routing Rules](#session-service-routing-rules)
- [Input Adapter Pipeline](#input-adapter-pipeline)
- [Integration-Specific Endpoint Mapping](#integration-specific-endpoint-mapping)
- [Control Integration Points](#control-integration-points)
- [Validation And Regression Tests](#validation-and-regression-tests)
- [Code Examples](#code-examples)

## Scope And Primary Files

Primary code files:
- `src/RoyalTerminal.Terminal/Terminal/TerminalEndpointContracts.cs`
- `src/RoyalTerminal.Terminal.Services.Contracts/Contracts/ITerminalSessionService.cs`
- `src/RoyalTerminal.Terminal.Services/Services/TerminalSessionService.cs`
- `src/RoyalTerminal.Avalonia/Services/DefaultTerminalInputAdapter.cs`
- `src/RoyalTerminal.Avalonia/Controls/TerminalControl.cs`

Use this reference when changing endpoint capability contracts, input routing precedence, or any endpoint-backed control behavior.

## Contract Surface

Core contracts in `TerminalEndpointContracts.cs`:

| Contract | Purpose |
|---|---|
| `ITerminalEndpoint` | minimal endpoint plumbing (`SendText`, `SetFocus`, `SetSize`) |
| `ITerminalInputSink` | normalized key/text/pointer input sink |
| `ITerminalSelectionSource` | read selection state/text from endpoint |
| `ITerminalModeSource` | publish mode state + mode-changed events |
| `ITerminalScaleSink` | optional DPI/content scale propagation |

Normalized event payloads:
- `TerminalKeyEvent`
- `TerminalPointerEvent`
- `TerminalInputAction`
- `TerminalPointerEventKind`
- `TerminalMouseButton`
- `TerminalModifiers`

## Capability Composition Model

`ITerminalSessionService.AttachEndpoint(...)` does capability discovery by type-cast:
- endpoint is always stored as `ITerminalEndpoint`
- optional capabilities are cached if implemented by the same object:
  - `ITerminalInputSink`
  - `ITerminalSelectionSource`
  - `ITerminalModeSource`

Important precedence:
- `ModeSource` uses endpoint mode source first, VT-derived mode bridge second.
- `SendInput(...)` prefers endpoint path first, then transport/PTY paths.

No built-in endpoint-backed Ghostty surface implementation ships in the managed
/.NET stack anymore. The current product path is transport-backed
`TerminalControl` using managed VT or official `libghostty-vt`.

Endpoint contracts remain intentionally generic for custom/native integrations.
When an endpoint implementation omits `ITerminalModeSource`,
`TerminalSessionService` falls back to the VT processor bridge when VT is active.

## Session-Service Routing Rules

`TerminalSessionService` behavior (`src/RoyalTerminal.Terminal.Services/Services/TerminalSessionService.cs`):

`SendInput(string)` routing:
1. endpoint path: `Endpoint.SendText(Encoding.UTF8.GetBytes(text))`
2. active transport path:
- PTY transport: `ptyTransport.Pty.Write(text)`
- non-PTY transport: `Transport.SendInput(Encoding.UTF8.GetBytes(text))`
3. legacy PTY fallback: `Pty.Write(text)`

`SendInput(ReadOnlySpan<byte>)` routing:
1. endpoint path: `Endpoint.SendText(data)`
2. active transport path: `Transport.SendInput(data)`
3. legacy PTY fallback: copy span -> `Pty.Write(byte[], offset, length)`

Mode source refresh logic:
- `AttachEndpoint`/`DetachEndpoint` call `RefreshModeSource()`
- `StartSessionAsync` installs VT mode bridge (`VtProcessorModeSource`) when VT exists
- endpoint mode source always overrides VT mode source when both exist

## Input Adapter Pipeline

`DefaultTerminalInputAdapter` (`src/RoyalTerminal.Avalonia/Services/DefaultTerminalInputAdapter.cs`) applies the same strategy for keyboard/text:

Key down:
1. if `sessionService.InputSink` exists, send normalized `TerminalKeyEvent` (`Press`)
2. else, if transport/PTY path exists, encode VT sequence through `TerminalKeySequenceEncoder` using mode state:
- `sessionService.ModeSource.ModeState` when available
- fallback to `vtProcessor.ModeState`
- final fallback: default mode state

Key up:
- only endpoint-input path (`TerminalKeyEvent` with `Release`)

Text input:
1. endpoint-input path: `InputSink.SendText(text)`
2. fallback path: `sessionService.SendInput(text)`

Design impact:
- endpoint-backed controls can fully own key/pointer semantics while still using shared session lifecycle APIs.

## Integration-Specific Endpoint Mapping

If a custom host wants endpoint-backed routing, it should map the normalized
contracts to its own native or managed surface APIs:

| Normalized API | Typical host responsibility |
|---|---|
| `SendText(ReadOnlySpan<byte>)` | forward UTF-8 input bytes to the endpoint |
| `SetFocus(bool)` | update native focus state |
| `SetSize(int,int)` | update endpoint pixel size |
| `SendKey(TerminalKeyEvent)` | map normalized key data to host-native key input |
| `SendText(string)` | send composed text directly when the host supports it |
| `SendPointer(...)` | map move/button/scroll events to host-native pointer APIs |
| `ReadSelection()` | expose host-managed selection text when available |
| `SetContentScale(double,double)` | propagate DPI/content scale if the endpoint needs it |

Design rule:
- keep endpoint implementations integration-specific
- keep `ITerminalEndpoint` contracts backend-neutral
- do not reintroduce a product-wide managed wrapper around embedded `libghostty`

## Control Integration Points

`TerminalControl` endpoint integration (`src/RoyalTerminal.Avalonia/Controls/TerminalControl.cs`):
- `AttachEndpoint(ITerminalEndpoint)`
- `DetachEndpoint()`
- `SendInput(string)` and `SendInput(ReadOnlySpan<byte>)`
- `SetContentScale(double,double)` forwards only when endpoint implements `ITerminalScaleSink`

Pointer flow in `TerminalControl`:
- if endpoint `InputSink.SendPointer(...)` accepts event, endpoint wins
- otherwise pointer encoding falls back to VT mouse protocol bytes when mouse-reporting mode is enabled

This keeps custom endpoint-backed integrations possible without changing the
default transport-backed `TerminalControl` behavior used by the product.

## Validation And Regression Tests

Test files that should be rerun after endpoint/input pipeline changes:
- `tests/RoyalTerminal.Tests/TerminalInputAdapterTests.cs`
- `tests/RoyalTerminal.Tests/TerminalSessionServiceTransportTests.cs`
- `tests/RoyalTerminal.Tests/TerminalAbstractionsTests.cs`
- `tests/RoyalTerminal.Tests/TerminalControlTests.cs`
- `tests/RoyalTerminal.Tests/TerminalControlHeadlessInteractionTests.cs`

## Code Examples

### Custom endpoint with full capability surface

```csharp
using RoyalTerminal.Terminal;

public sealed class CustomEndpoint :
    ITerminalEndpoint,
    ITerminalInputSink,
    ITerminalSelectionSource,
    ITerminalModeSource,
    ITerminalScaleSink
{
    private TerminalModeState _modeState = new(
        CursorVisible: true,
        ApplicationCursorKeys: false,
        ApplicationKeypad: false,
        AlternateScreen: false,
        BracketedPaste: false);

    public bool HasSelection { get; private set; }
    public TerminalModeState ModeState => _modeState;
    public event EventHandler<TerminalModeState>? ModeChanged;

    public void SendText(ReadOnlySpan<byte> utf8) { /* transport to endpoint */ }
    public void SetFocus(bool focused) { }
    public void SetSize(int widthPx, int heightPx) { }
    public void SetContentScale(double scaleX, double scaleY) { }

    public bool SendKey(TerminalKeyEvent keyEvent) => true;
    public bool SendText(string text) => !string.IsNullOrEmpty(text);
    public bool SendPointer(TerminalPointerEvent pointerEvent) => true;

    public string? ReadSelection() => HasSelection ? "selected text" : null;

    public void SetBracketedPaste(bool enabled)
    {
        _modeState = _modeState with { BracketedPaste = enabled };
        ModeChanged?.Invoke(this, _modeState);
    }
}
```

### Session service endpoint-first routing

```csharp
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Services;

ITerminalSessionService session = new TerminalSessionService();
CustomEndpoint endpoint = new();

session.AttachEndpoint(endpoint);
session.SendInput("echo endpoint-first\\r\\n");

TerminalKeyEvent key = new(
    Action: TerminalInputAction.Press,
    KeyCode: 13,
    Text: null,
    Modifiers: TerminalModifiers.None);

_ = session.InputSink?.SendKey(key);
```

### Endpoint mode source driving input behavior

```csharp
ITerminalModeSource? modeSource = session.ModeSource;
if (modeSource is not null)
{
    modeSource.ModeChanged += (_, state) =>
    {
        Console.WriteLine($"BracketedPaste={state.BracketedPaste}");
    };
}
```

### Attach endpoint to `TerminalControl` and propagate scale

```csharp
using RoyalTerminal.Avalonia.Controls;

TerminalControl control = new();
CustomEndpoint endpoint = new();

control.AttachEndpoint(endpoint);
control.SetContentScale(2.0, 2.0); // forwarded only if endpoint is ITerminalScaleSink
control.SendInput("pwd\\r\\n");
```
