# Transport Types Usage Setup

This is the transport reference entrypoint. Load this file first, then open only the sub-file needed for the change.

## Table Of Contents

- [What This Set Covers](#what-this-set-covers)
- [Load Order](#load-order)
- [Decision Guide](#decision-guide)
- [Recommended Workflow](#recommended-workflow)
- [Critical Invariants](#critical-invariants)
- [Validation Gate](#validation-gate)

## What This Set Covers

The transport reference set documents:
- contract surfaces (`ITerminalTransport*`, options, providers, session service integration)
- endpoint-vs-transport input routing precedence in session service
- concrete runtime implementations (PTY, Pipe, SSH)
- dependency wiring in `TerminalControl` and demo orchestration
- SSH credential and host-key trust plumbing
- transport-specific validation and regression checks

## Load Order

1. [`transport-contracts-and-options.md`](transport-contracts-and-options.md)
2. [`transport-implementations-and-providers.md`](transport-implementations-and-providers.md)
3. [`transport-session-orchestration.md`](transport-session-orchestration.md)
4. [`transport-setup-patterns.md`](transport-setup-patterns.md)
5. [`transport-ssh-credentials-and-trust.md`](transport-ssh-credentials-and-trust.md) (when SSH is involved)
6. [`transport-entrypoints-and-validation.md`](transport-entrypoints-and-validation.md) before finalizing changes
7. [`endpoint-contracts-and-input-pipeline.md`](endpoint-contracts-and-input-pipeline.md) when endpoint capability/input routing behavior changes

## Decision Guide

Use this quick guide to pick files fast:

| If you are changing... | Read first | Then read |
|---|---|---|
| option records or transport IDs | [`transport-contracts-and-options.md`](transport-contracts-and-options.md) | [`transport-entrypoints-and-validation.md`](transport-entrypoints-and-validation.md) |
| provider/factory selection behavior | [`transport-implementations-and-providers.md`](transport-implementations-and-providers.md) | [`transport-session-orchestration.md`](transport-session-orchestration.md) |
| `TerminalSessionService` behavior | [`transport-session-orchestration.md`](transport-session-orchestration.md) | [`transport-entrypoints-and-validation.md`](transport-entrypoints-and-validation.md) |
| endpoint contract or endpoint-input precedence | [`endpoint-contracts-and-input-pipeline.md`](endpoint-contracts-and-input-pipeline.md) | [`transport-session-orchestration.md`](transport-session-orchestration.md) |
| `TerminalControl` constructor wiring | [`transport-setup-patterns.md`](transport-setup-patterns.md) | [`transport-contracts-and-options.md`](transport-contracts-and-options.md) |
| SSH auth or host key handling | [`transport-ssh-credentials-and-trust.md`](transport-ssh-credentials-and-trust.md) | [`transport-implementations-and-providers.md`](transport-implementations-and-providers.md) |
| demo transport start flow | [`transport-setup-patterns.md`](transport-setup-patterns.md) | [`transport-ssh-credentials-and-trust.md`](transport-ssh-credentials-and-trust.md) |

## Recommended Workflow

1. Confirm the target transport path (`pty`, `pipe`, `ssh`) in `TerminalTransportIds`.
2. Confirm option shape and invariants in `TransportOptions.cs`.
3. Confirm provider selection and runtime behavior in concrete transport file.
4. Confirm `TerminalSessionService` start/stop callback lifecycle is still correct.
5. Confirm `TerminalControl` entrypoint (`StartSessionAsync`, `StartPipeAsync`, `StartSshAsync`) still behaves consistently.
6. Run targeted tests plus the final validation gate.

## Critical Invariants

- `TransportId` must match provider `TransportId` and option type.
- `CanHandle(options)` must remain deterministic and type-safe.
- transport start must never succeed without event subscriptions being active.
- stop/cleanup must always clear callbacks and release resources.
- input routing priority in `TerminalSessionService` must remain: endpoint -> transport -> legacy PTY fallback.
- SSH host trust must remain explicit: pin if configured, otherwise validator.

## Validation Gate

Always run:
- targeted transport tests in `tests/RoyalTerminal.Tests`
- SSH/security tests if SSH code changed
- full `dotnet test RoyalTerminal.sln -c Release` for shared contracts

Command set and scope are documented in:
- [`transport-entrypoints-and-validation.md`](transport-entrypoints-and-validation.md)
- [`build-test-validation.md`](build-test-validation.md)

## Code Examples

### Unified transport startup helper

```csharp
using RoyalTerminal.Avalonia.Controls;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Transport.Pipe;

public static class TerminalSessionStarter
{
    public static async Task StartAsync(
        TerminalControl control,
        string transportId,
        string workingDirectory,
        string commandText,
        string sshHost,
        int sshPort,
        string sshUser,
        SshAuthenticationOptions sshAuth,
        CancellationToken cancellationToken = default)
    {
        TerminalSessionDimensions dims = new(120, 40, 1200, 800);

        ITerminalTransportOptions options = transportId switch
        {
            TerminalTransportIds.Pty => new PtyTransportOptions(
                Command: null,
                WorkingDirectory: workingDirectory,
                Environment: null,
                Dimensions: dims),

            TerminalTransportIds.Pipe => new PipeTransportOptions(
                Command: new TerminalCommandSpec("/bin/sh", new[] { "-lc", commandText }),
                WorkingDirectory: workingDirectory,
                Environment: null,
                MergeStdErrIntoStdOut: true,
                Dimensions: dims),

            TerminalTransportIds.Ssh => new SshTransportOptions(
                Endpoint: new SshEndpointOptions(sshHost, sshPort, sshUser),
                RequestPty: true,
                TerminalType: "xterm-256color",
                InitialCommand: null,
                Authentication: sshAuth,
                Dimensions: dims),

            _ => throw new InvalidOperationException($"Unsupported transport id '{transportId}'.")
        };

        await control.StartSessionAsync(options, cancellationToken);
    }
}
```

### Reference-loading workflow in practice

```bash
# 1) Start from transport index
cat skills/royalterminal-development/references/transport-types-usage-setup.md

# 2) Open contracts + implementations for behavior changes
cat skills/royalterminal-development/references/transport-contracts-and-options.md
cat skills/royalterminal-development/references/transport-implementations-and-providers.md

# 3) Finish with validation commands
cat skills/royalterminal-development/references/transport-entrypoints-and-validation.md
```

### Start by explicit typed wrappers when possible

```csharp
if (options is PipeTransportOptions pipe)
{
    await terminal.StartPipeAsync(pipe, cancellationToken);
}
else if (options is SshTransportOptions ssh)
{
    await terminal.StartSshAsync(ssh, cancellationToken);
}
else
{
    await terminal.StartSessionAsync(options, cancellationToken);
}
```
