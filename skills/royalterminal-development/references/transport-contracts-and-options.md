# Transport Contracts And Options

## Table Of Contents

- [Core Contracts](#core-contracts)
- [Transport IDs](#transport-ids)
- [Option Models](#option-models)
- [Option Construction Patterns](#option-construction-patterns)
- [SSH Auth And Secret Contracts](#ssh-auth-and-secret-contracts)
- [Contract Invariants](#contract-invariants)
- [Extension Rules](#extension-rules)

## Core Contracts

Primary file:
- `src/RoyalTerminal.Terminal/Terminal/TransportContracts.cs`

Contract responsibilities:

| Contract | Responsibility | Notes |
|---|---|---|
| `ITerminalTransportOptions` | immutable start options payload | must expose `TransportId` and `Dimensions` |
| `ITerminalTransport` | runtime transport lifecycle | event-driven output + explicit `StartAsync/StopAsync` |
| `ITerminalPtyTransport` | transport with underlying PTY | exposes `IPty Pty` for PTY-specific paths |
| `ITerminalTransportProvider` | provider-level factory participant | type/ID gate via `CanHandle` |
| `ITerminalTransportFactory` | resolves provider and creates runtime transport | default implementation: `CompositeTerminalTransportFactory` |

Session service contract:
- `ITerminalSessionService` in `src/RoyalTerminal.Terminal.Services.Contracts/Contracts/ITerminalSessionService.cs`

## Transport IDs

Source:
- `src/RoyalTerminal.Terminal/Terminal/TransportOptions.cs`

IDs are constants in `TerminalTransportIds`:
- `pty`
- `pipe`
- `ssh`

Usage requirements:
- option type must emit the correct `TransportId`
- provider `TransportId` must match option `TransportId` (case-insensitive factory check)
- UI selections should map directly to these IDs

## Option Models

Source:
- `src/RoyalTerminal.Terminal/Terminal/TransportOptions.cs`

### Shared

`TerminalSessionDimensions` (in `TransportContracts.cs`):
- `Columns`
- `Rows`
- `WidthPixels`
- `HeightPixels`

Use logical and pixel dimensions together whenever a transport supports resize semantics.

### PTY

`PtyTransportOptions`:
- `TerminalCommandSpec? Command`
- `string? WorkingDirectory`
- `IReadOnlyDictionary<string,string>? Environment`
- `TerminalSessionDimensions Dimensions`

Behavioral note:
- `Command = null` means "use default shell profile"
- `Command` with empty `FileName` and args means "default shell + explicit args"

### Pipe

`PipeTransportOptions`:
- `TerminalCommandSpec Command`
- `string? WorkingDirectory`
- `IReadOnlyDictionary<string,string>? Environment`
- `bool MergeStdErrIntoStdOut`
- `TerminalSessionDimensions Dimensions`

Behavioral note:
- `Dimensions` are accepted for API symmetry; actual resize is a no-op in pipe transport

### SSH

`SshEndpointOptions`:
- `Host`
- `Port`
- `Username`

`SshTransportOptions`:
- `SshEndpointOptions Endpoint`
- `bool RequestPty`
- `string TerminalType`
- `string? InitialCommand`
- `SshAuthenticationOptions Authentication`
- `TerminalSessionDimensions Dimensions`
- `string? ExpectedHostKeyFingerprintSha256` (init-only, optional)

Fingerprint normalization rules:
- accepts with or without `SHA256:` prefix
- trailing `=` padding is normalized away by transport comparison logic

## Option Construction Patterns

Typical PTY options:
```csharp
PtyTransportOptions options = new(
    Command: null,
    WorkingDirectory: workingDirectory,
    Environment: null,
    Dimensions: dimensions);
```

Typical Pipe options:
```csharp
PipeTransportOptions options = new(
    Command: new TerminalCommandSpec("/bin/sh", new[] { "-lc", commandText }),
    WorkingDirectory: workingDirectory,
    Environment: null,
    MergeStdErrIntoStdOut: true,
    Dimensions: dimensions);
```

Typical SSH options:
```csharp
SshTransportOptions options = new(
    Endpoint: new SshEndpointOptions(host, port, username),
    RequestPty: true,
    TerminalType: "xterm-256color",
    InitialCommand: null,
    Authentication: auth,
    Dimensions: dimensions)
{
    ExpectedHostKeyFingerprintSha256 = expectedFingerprint,
};
```

## SSH Auth And Secret Contracts

Source:
- `src/RoyalTerminal.Terminal/Terminal/SshAuthContracts.cs`
- `src/RoyalTerminal.Terminal/Terminal/SshSecretProtectionContracts.cs`

Core models:
- `SshAuthenticationOptions`
- `SshCredentialRequest`
- `SshResolvedCredentials`

Core abstractions:
- `ISshCredentialProvider`
- `ISshSecretStore`
- `ISshSecretProtector`

Contract responsibilities:
- `ISshCredentialProvider` resolves runtime secrets into materialized credential payloads
- `ISshSecretStore` persists/loads by secret ID
- `ISshSecretProtector` encrypts/decrypts store payload bytes (for protected stores)

## Contract Invariants

- transport implementations must reject wrong option runtime type with `ArgumentException`
- all `StartAsync` paths must be cancellation aware
- `SendInput` should be tolerant of empty payloads and stopped state
- `StopAsync` should be idempotent and safe if not running
- event callbacks must not be left subscribed after stop/dispose

## Extension Rules

When adding a new transport type:

1. Add new ID constant in `TerminalTransportIds`.
2. Add immutable options record implementing `ITerminalTransportOptions`.
3. Implement `ITerminalTransport` and provider.
4. Register provider in the composition root (`TerminalControl` default factory and demo/custom wiring).
5. Add tests for factory matching, lifecycle, input, and stop semantics.
6. Update `references/transport-implementations-and-providers.md` and validation docs.

## Code Examples

### Build options for all transport types

```csharp
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Transport.Pipe;

TerminalSessionDimensions dims = new(120, 40, 1200, 800);

PtyTransportOptions pty = new(
    Command: null,
    WorkingDirectory: Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
    Environment: null,
    Dimensions: dims);

PipeTransportOptions pipe = new(
    Command: new TerminalCommandSpec("/bin/sh", new[] { "-lc", "echo hello" }),
    WorkingDirectory: null,
    Environment: null,
    MergeStdErrIntoStdOut: true,
    Dimensions: dims);

SshAuthenticationOptions auth = new(
    UsePassword: true,
    PasswordSecretId: "ssh-password",
    PrivateKeySecretIds: Array.Empty<string>(),
    UseAgent: false);

SshTransportOptions ssh = new(
    Endpoint: new SshEndpointOptions("example.com", 22, "alice"),
    RequestPty: true,
    TerminalType: "xterm-256color",
    InitialCommand: "top",
    Authentication: auth,
    Dimensions: dims)
{
    ExpectedHostKeyFingerprintSha256 = "SHA256:base64fingerprint"
};
```

### Add a new transport contract implementation skeleton

```csharp
public sealed record SerialTransportOptions(
    string DevicePath,
    int BaudRate,
    TerminalSessionDimensions Dimensions) : ITerminalTransportOptions
{
    public string TransportId => "serial";
}

public sealed class SerialTerminalTransportProvider : ITerminalTransportProvider
{
    public string TransportId => "serial";

    public bool CanHandle(ITerminalTransportOptions options)
        => options is SerialTransportOptions;

    public ITerminalTransport Create()
        => new SerialTerminalTransport();
}
```
