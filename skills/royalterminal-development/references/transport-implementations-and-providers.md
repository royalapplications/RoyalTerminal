# Transport Implementations And Providers

## Table Of Contents

- [Factory Selection](#factory-selection)
- [PTY Transport](#pty-transport)
- [Pipe Transport](#pipe-transport)
- [SSH Transport](#ssh-transport)
- [Provider Registration Patterns](#provider-registration-patterns)
- [Failure Modes](#failure-modes)
- [Extension Checklist](#extension-checklist)

## Factory Selection

Factory implementation:
- `CompositeTerminalTransportFactory`
- `src/RoyalTerminal.Terminal/Terminal/CompositeTerminalTransportFactory.cs`

Selection algorithm:
1. Iterate providers in registration order.
2. Compare `provider.TransportId` to `options.TransportId` (ordinal-ignore-case).
3. Require `provider.CanHandle(options)`.
4. Return `provider.Create()` on first match.
5. Throw `InvalidOperationException` when no match exists.

Practical implication:
- provider order matters if multiple providers share an ID.
- keep one provider per transport ID unless intentional override behavior is needed.

## PTY Transport

Implementation:
- `PtyTerminalTransport`
- `src/RoyalTerminal.Terminal.Transport.Pty/PtyTerminalTransport.cs`

Provider:
- `PtyTerminalTransportProvider`

Key behaviors:
- starts platform PTY via `IPtyFactory` (`UnixPty` or `WindowsPty` under `DefaultPtyFactory`)
- resolves command in this order:
  1. explicit `options.Command.FileName` when non-empty
  2. default shell profile (`IShellProfileCatalog.GetDefaultProfile()`)
  3. default shell profile plus argument-only override when `Command.FileName` is empty but args are provided
- enforces minimum dimensions of 1 for cols/rows/pixels on start/resize
- mirrors PTY events (`DataReceived`, `ProcessExited`) through transport events
- write path copies `ReadOnlySpan<byte>` before forwarding to PTY

Important constraints:
- throws if `StartAsync` called twice without stop
- `Pty` property only valid while running

## Pipe Transport

Implementation:
- `PipeTerminalTransport`
- `src/RoyalTerminal.Terminal.Transport.Pipe/PipeTerminalTransport.cs`

Provider:
- `PipeTerminalTransportProvider`

Key behaviors:
- launches process with redirected stdin/stdout/stderr and `UseShellExecute=false`
- optional working directory and environment injection
- async readers consume stdout always; stderr only emitted when `MergeStdErrIntoStdOut=true`
- uses `ArrayPool<byte>` buffer in read loops
- raises `ProcessExited` exactly once (`Interlocked.Exchange` guard)
- `Resize(...)` is intentionally a no-op (pipe has no PTY window semantics)

Lifecycle notes:
- `StopAsync` cancels readers, best-effort kills process tree, drains readers, unsubscribes, disposes
- tolerant to read faults during teardown

## SSH Transport

Implementation:
- `SshNetTerminalTransport`
- `src/RoyalTerminal.Terminal.Transport.Ssh.SshNet/SshNetTerminalTransport.cs`

Provider:
- `SshNetTerminalTransportProvider`

Optional auth contributor:
- `SshNetAgentAuthenticationMethodContributor`
- `src/RoyalTerminal.Terminal.Transport.Ssh.SshNet.Agent/SshNetAgentAuthenticationMethodContributor.cs`

Key behaviors:
- validates endpoint options before connection (`host`, `username`, `port` range)
- resolves secrets via `ISshCredentialProvider`
- builds auth method list from password, private keys, and contributors
- rejects start when auth methods list is empty
- host-key trust flow:
  1. if `ExpectedHostKeyFingerprintSha256` set: strict normalized SHA-256 match
  2. else defer to configured `ISshHostKeyValidator`
- creates shell stream with PTY when `RequestPty=true`; otherwise no-terminal shell stream
- `InitialCommand` is written after stream is ready
- resize only applies when `RequestPty=true`

Error behavior:
- preserves host-key validation failure details via `_lastHostKeyValidationError`
- raises `ProcessExited` once on shell close/error/stop

## Provider Registration Patterns

Default `TerminalControl` registration:
- `PtyTerminalTransportProvider`
- `PipeTerminalTransportProvider`
- `SshNetTerminalTransportProvider`

Demo registration (`MainWindowController.CreateStandaloneControl`):
- same providers, but injects:
  - `DemoSshCredentialProvider`
  - `KnownHostsSshHostKeyValidator`
  - `SshNetAgentAuthenticationMethodContributor`

Registration anchor files:
- `src/RoyalTerminal.Avalonia/Controls/TerminalControl.cs`
- `samples/RoyalTerminal.Demo/Services/MainWindowController.cs`

## Failure Modes

Common failure and likely root cause:

| Symptom | Likely Cause | Check |
|---|---|---|
| `No terminal transport provider can handle transport id` | provider not registered or wrong ID/type | `CompositeTerminalTransportFactory` registration + `CanHandle` |
| PTY starts wrong shell | command fallback path used unexpectedly | `PtyTransportOptions.Command` and shell profile catalog |
| pipe output missing stderr | `MergeStdErrIntoStdOut=false` | pipe options in start flow |
| SSH connection fails before auth | invalid host/port/user | `ValidateOptions` path in `SshNetTerminalTransport` |
| SSH trust failure | mismatch pin or validator rejection | `ExpectedHostKeyFingerprintSha256`, known_hosts entries |

## Extension Checklist

When modifying a transport implementation:

1. Keep `StartAsync/StopAsync` idempotency and event symmetry.
2. Keep cleanup in all failure paths.
3. Keep input send tolerant of stopped/empty state.
4. Verify resize semantics are explicit (supported vs no-op).
5. Add or update tests in `tests/RoyalTerminal.Tests`:
   - `PtyTerminalTransportTests.cs`
   - `PipeTerminalTransportTests.cs`
   - `SshNetTerminalTransportSecurityTests.cs`
   - `TerminalTransportFactoryTests.cs`

## Code Examples

### Register providers with the composite factory

```csharp
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Transport.Pipe;
using RoyalTerminal.Terminal.Transport.Pty;
using RoyalTerminal.Terminal.Transport.Ssh;
using RoyalTerminal.Terminal.Transport.Ssh.SshNet;

ITerminalTransportFactory factory = new CompositeTerminalTransportFactory(
    new ITerminalTransportProvider[]
    {
        new PtyTerminalTransportProvider(),
        new PipeTerminalTransportProvider(),
        new SshNetTerminalTransportProvider(
            credentialProvider: new SecretStoreSshCredentialProvider(secretStore),
            hostKeyValidator: new KnownHostsSshHostKeyValidator()),
    });
```

### Custom provider override by registration order

```csharp
// If two providers share TransportId, first matching provider wins.
ITerminalTransportFactory factory = new CompositeTerminalTransportFactory(
    new ITerminalTransportProvider[]
    {
        new CustomSshTransportProvider(),    // selected first for "ssh" when CanHandle returns true
        new SshNetTerminalTransportProvider() // fallback
    });
```

### PTY command resolution pattern

```csharp
PtyTransportOptions options = new(
    Command: new TerminalCommandSpec(string.Empty, new[] { "--login" }),
    WorkingDirectory: null,
    Environment: null,
    Dimensions: new TerminalSessionDimensions(100, 30, 1000, 700));

// Runtime behavior: default shell profile executable + provided argument list.
```
