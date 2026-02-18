# Transport Entrypoints And Validation

## Table Of Contents

- [Public Entrypoints](#public-entrypoints)
- [Entrypoint Usage Matrix](#entrypoint-usage-matrix)
- [Validation Matrix](#validation-matrix)
- [Targeted Test Suites](#targeted-test-suites)
- [Regression Hotspots](#regression-hotspots)
- [Command Checklist](#command-checklist)

## Public Entrypoints

Primary API surface in `TerminalControl`:
- `StartSessionAsync(ITerminalTransportOptions, CancellationToken)`
- `StartPipeAsync(PipeTransportOptions, CancellationToken)`
- `StartSshAsync(SshTransportOptions, CancellationToken)`
- compatibility: `StartPty(...)`
- stop: `StopPty()` (session stop wrapper)

State inspection:
- `HasActiveSession`
- `ActiveTransportId`
- `HasPty`

Source:
- `src/RoyalTerminal.Avalonia/Controls/TerminalControl.cs`

## Entrypoint Usage Matrix

| Scenario | Preferred API | Why |
|---|---|---|
| generic transport startup | `StartSessionAsync` | supports any `ITerminalTransportOptions` |
| process command flow | `StartPipeAsync` | explicit compile-time option type |
| SSH flow | `StartSshAsync` | explicit compile-time option type |
| legacy shell startup | `StartPty` | compatibility convenience wrapper |

Guidance:
- new code should prefer `StartSessionAsync` or typed async wrappers.
- avoid using compatibility `StartPty` in asynchronous orchestration code; prefer option records + async APIs.

## Validation Matrix

| Change Type | Minimum Validation |
|---|---|
| option model or transport IDs | transport factory tests + session service tests |
| PTY transport behavior | `PtyTerminalTransportTests` + `PtyContractTests` |
| pipe transport behavior | `PipeTerminalTransportTests` |
| SSH auth/trust behavior | SSH security tests + integration SSH tests |
| session orchestration behavior | `TerminalSessionServiceTransportTests` + input adapter tests |
| shared contract changes | full solution test pass |

## Targeted Test Suites

Unit tests (`tests/RoyalTerminal.Tests`):
- `TerminalTransportFactoryTests.cs`
- `PtyTerminalTransportTests.cs`
- `PipeTerminalTransportTests.cs`
- `TerminalSessionServiceTransportTests.cs`
- `TerminalInputAdapterTests.cs`
- `SshCredentialProvidersTests.cs`
- `SshHostKeyValidationTests.cs`
- `KnownHostsSshHostKeyValidatorTests.cs`
- `SshNetTerminalTransportSecurityTests.cs`
- `SshNetAgentAuthenticationMethodContributorTests.cs`

Integration tests (`tests/RoyalTerminal.IntegrationTests`):
- `SshTransportIntegrationTests.cs`

## Regression Hotspots

Validate these behavior contracts after transport changes:
- callback lifecycle: VT response/bell/title wiring and cleanup
- exit signaling: only one `ProcessExited` event per session
- resize semantics: PTY + SSH PTY should resize, pipe should no-op
- input path precedence: endpoint vs transport vs legacy PTY
- transport switching: repeated start/stop without resource leaks

## Command Checklist

Full baseline:
```bash
dotnet test RoyalTerminal.sln -c Release
```

Transport/security-focused subset:
```bash
dotnet test tests/RoyalTerminal.Tests/RoyalTerminal.Tests.csproj -c Release --filter "TerminalTransportFactoryTests|PtyTerminalTransportTests|PipeTerminalTransportTests|TerminalSessionServiceTransportTests|SshCredentialProvidersTests|SshHostKeyValidationTests|KnownHostsSshHostKeyValidatorTests|SshNetTerminalTransportSecurityTests|SshNetAgentAuthenticationMethodContributorTests"
```

SSH integration subset:
```bash
ROYALTERMINAL_IT_SSH_HOST=127.0.0.1 \
ROYALTERMINAL_IT_SSH_PORT=22 \
ROYALTERMINAL_IT_SSH_USERNAME=test-user \
ROYALTERMINAL_IT_SSH_PASSWORD=secret \
ROYALTERMINAL_IT_SSH_HOST_KEY_SHA256=SHA256:your-host-key-fingerprint \
dotnet test tests/RoyalTerminal.IntegrationTests/RoyalTerminal.IntegrationTests.csproj -c Release --filter "SshTransportIntegrationTests"
```

For broader build and native validation commands, see:
- `references/build-test-validation.md`

## Code Examples

### Start all session types through `TerminalControl`

```csharp
TerminalControl control = new();

// PTY
await control.StartSessionAsync(new PtyTransportOptions(
    Command: null,
    WorkingDirectory: Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
    Environment: null,
    Dimensions: new TerminalSessionDimensions(120, 40, 1200, 800)));

// Pipe
await control.StartPipeAsync(new PipeTransportOptions(
    Command: new TerminalCommandSpec("/bin/sh", new[] { "-lc", "echo pipe mode" }),
    WorkingDirectory: null,
    Environment: null,
    MergeStdErrIntoStdOut: true,
    Dimensions: new TerminalSessionDimensions(120, 40, 1200, 800)));

// SSH
await control.StartSshAsync(sshOptions);
```

### Session state assertions for regression tests

```csharp
Assert.True(control.HasActiveSession);
Assert.Equal(TerminalTransportIds.Ssh, control.ActiveTransportId);

control.StopPty();
Assert.False(control.HasActiveSession);
Assert.Null(control.ActiveTransportId);
```

### Command recipe by subsystem

```bash
# Transport unit + security tests
 dotnet test tests/RoyalTerminal.Tests/RoyalTerminal.Tests.csproj -c Release --filter "TerminalTransportFactoryTests|PtyTerminalTransportTests|PipeTerminalTransportTests|TerminalSessionServiceTransportTests|SshNetTerminalTransportSecurityTests"

# SSH integration
 ROYALTERMINAL_IT_SSH_HOST=127.0.0.1 \
 ROYALTERMINAL_IT_SSH_PORT=22 \
 ROYALTERMINAL_IT_SSH_USERNAME=test-user \
 ROYALTERMINAL_IT_SSH_PASSWORD=secret \
 ROYALTERMINAL_IT_SSH_HOST_KEY_SHA256=SHA256:your-host-key-fingerprint \
 dotnet test tests/RoyalTerminal.IntegrationTests/RoyalTerminal.IntegrationTests.csproj -c Release --filter "SshTransportIntegrationTests"
```
