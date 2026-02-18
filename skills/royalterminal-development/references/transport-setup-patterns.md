# Transport Setup Patterns

## Table Of Contents

- [Default TerminalControl Wiring](#default-terminalcontrol-wiring)
- [Custom TerminalControl Wiring](#custom-terminalcontrol-wiring)
- [Demo Wiring Pattern](#demo-wiring-pattern)
- [Transport Option Build Pattern](#transport-option-build-pattern)
- [Safe Customization Rules](#safe-customization-rules)
- [Common Misconfigurations](#common-misconfigurations)

## Default TerminalControl Wiring

Source:
- `src/RoyalTerminal.Avalonia/Controls/TerminalControl.cs`

`TerminalControl()` default constructor composes:
- `TerminalSessionService`
- `DefaultTerminalInputAdapter`
- `DefaultTerminalSelectionService`
- `DefaultTerminalScrollService`
- `DefaultVtProcessorFactory()`
- `DefaultPtyFactory()`
- `NullSshCredentialProvider()`
- `KnownHostsSshHostKeyValidator()`
- default `CompositeTerminalTransportFactory` with:
  - `PtyTerminalTransportProvider`
  - `PipeTerminalTransportProvider`
  - `SshNetTerminalTransportProvider`

Implication:
- SSH is present by default, but start will fail until a real credential provider is supplied.

## Custom TerminalControl Wiring

Use constructor injection:
- custom `ITerminalSessionService`
- custom `ITerminalInputAdapter`
- custom `ITerminalSelectionService`
- custom `ITerminalScrollService`
- custom `IVtProcessorFactory`
- custom `IPtyFactory`
- custom `ISshCredentialProvider`
- custom `ISshHostKeyValidator`
- optional custom `ITerminalTransportFactory`

Pattern:
```csharp
TerminalControl control = new(
    sessionService,
    inputAdapter,
    selectionService,
    scrollService,
    vtFactory,
    ptyFactory,
    credentialProvider,
    hostKeyValidator,
    transportFactory);
```

## Demo Wiring Pattern

Source:
- `samples/RoyalTerminal.Demo/Services/MainWindowController.cs`

Standalone mode composition (`CreateStandaloneControl`):
- native providers: `new GhosttyVtProcessorProvider()`
- transport factory:
  - `PtyTerminalTransportProvider`
  - `PipeTerminalTransportProvider`
  - `SshNetTerminalTransportProvider` with:
    - `DemoSshCredentialProvider`
    - `KnownHostsSshHostKeyValidator`
    - `SshNetAgentAuthenticationMethodContributor`

Transport start logic (`StartStandaloneSessionAsync`):
- `pty`: use selected shell profile + working directory
- `pipe`: build shell command wrapper around `PipeCommandText`
- `ssh`: build `SshTransportOptions` from view-model fields and selected auth mode

## Transport Option Build Pattern

Demo option builders:
- `BuildSessionDimensions(TerminalControl)`
- `BuildPipeCommand(ShellProfileOption?)`
- `BuildSshOptions(TerminalSessionDimensions)`
- `BuildSshAuthenticationOptions()`

Auth mode mapping (`SshAuthModeOption`):
- `password`
- `private-key`
- `agent`
- `password-key`

Private-key and password values are resolved through demo secret IDs, not passed directly in options.

## Safe Customization Rules

- keep options immutable after handoff to `StartSessionAsync`.
- preserve provider list coverage for every enabled transport ID.
- if you replace `ITerminalTransportFactory`, mirror the same provider capabilities or explicitly remove unsupported modes in UI.
- keep `VtProcessorPreference` assignment before session start (demo does this in `CreateStandaloneTerminalControl`).
- do not call transport objects directly from UI; go through `TerminalControl.Start*` APIs.

## Common Misconfigurations

| Misconfiguration | Effect | Fix |
|---|---|---|
| `NullSshCredentialProvider` used in real SSH flow | runtime invalid-operation on connect | inject `SecretStoreSshCredentialProvider` or equivalent |
| transport ID exposed in UI but provider not registered | factory cannot resolve provider | align transport picker with provider registration |
| custom factory ignores `CanHandle` semantics | wrong provider selected | enforce ID + type checks as in composite factory |
| VT preference set after session already started | preference not applied to active processor | set `VtProcessorPreference` before `StartSessionAsync` |

## Code Examples

### Default setup with typed start methods

```csharp
TerminalControl control = new();

await control.StartPipeAsync(new PipeTransportOptions(
    Command: new TerminalCommandSpec("/bin/sh", new[] { "-lc", "uname -a" }),
    WorkingDirectory: null,
    Environment: null,
    MergeStdErrIntoStdOut: true,
    Dimensions: new TerminalSessionDimensions(120, 40, 1200, 800)));
```

### Custom setup with secure SSH credential provider

```csharp
ISshSecretStore store = SshSecretProtectionFactory.CreateDefaultSecretStore();
ISshCredentialProvider creds = new SecretStoreSshCredentialProvider(store);
ISshHostKeyValidator trust = new KnownHostsSshHostKeyValidator();

TerminalControl control = new(
    terminalSessionService: new TerminalSessionService(),
    terminalInputAdapter: new DefaultTerminalInputAdapter(),
    terminalSelectionService: new DefaultTerminalSelectionService(),
    terminalScrollService: new DefaultTerminalScrollService(),
    vtProcessorFactory: new DefaultVtProcessorFactory(new INativeVtProcessorProvider[] { new GhosttyVtProcessorProvider() }),
    ptyFactory: new DefaultPtyFactory(),
    sshCredentialProvider: creds,
    sshHostKeyValidator: trust,
    transportFactory: null);
```

### Demo-style transport selection logic

```csharp
if (transportId == TerminalTransportIds.Pty)
{
    control.StartPty(shell: shellPath, workingDirectory: workingDirectory);
}
else if (transportId == TerminalTransportIds.Pipe)
{
    await control.StartPipeAsync(pipeOptions);
}
else if (transportId == TerminalTransportIds.Ssh)
{
    await control.StartSshAsync(sshOptions);
}
```
