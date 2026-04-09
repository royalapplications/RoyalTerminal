---
title: Transport Sessions
---

# Transport Sessions

RoyalTerminal runs sessions through a transport abstraction instead of a PTY-only runtime. Every session starts from an `ITerminalTransportOptions` implementation with a stable `TransportId`.

## Transport matrix

| Transport ID | Options type | Typical use |
| --- | --- | --- |
| `pty` | `PtyTransportOptions` | Local interactive shell with ConPTY or `forkpty` |
| `pipe` | `PipeTransportOptions` | Non-PTY process I/O for command, output, or log style scenarios |
| `ssh` | `SshTransportOptions` | Remote shell sessions with optional PTY, trust policy, proxies, and forwarding |
| `raw-tcp` | `RawTcpTransportOptions` | Unframed TCP byte streams |
| `telnet` | `TelnetTransportOptions` | Telnet sessions with option negotiation |
| `serial` | `SerialTransportOptions` | Direct serial line sessions |

## Preferred startup pattern

`TerminalControl.StartSessionAsync(...)` is the main API:

```csharp
PtyTransportOptions options = new(
    Command: null,
    WorkingDirectory: Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
    Environment: null,
    Dimensions: new TerminalSessionDimensions(120, 40, 1200, 800));

await terminal.StartSessionAsync(options);
```

Typed helpers such as `StartSshAsync(...)` exist where they improve readability, but the transport abstraction is the primary model.

## SSH transport

`SshTransportOptions` is the richest transport surface in the repository. It includes:

- endpoint information
- authentication settings
- optional host-key fingerprint pinning
- optional environment-variable bootstrap
- proxy settings
- port forwarding
- keep-alive and connect timeout policy
- optional X11 settings surface

```csharp
SshTransportOptions options = new(
    Endpoint: new SshEndpointOptions("example.com", 22, "alice"),
    RequestPty: true,
    TerminalType: "xterm-256color",
    InitialCommand: null,
    Authentication: new SshAuthenticationOptions(
        UsePassword: true,
        PasswordSecretId: "demo-password",
        PrivateKeySecretIds: Array.Empty<string>(),
        UseAgent: false),
    Dimensions: new TerminalSessionDimensions(120, 40, 1200, 800))
{
    ExpectedHostKeyFingerprintSha256 = "SHA256:BASE64_FINGERPRINT",
    EnvironmentVariables = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["LANG"] = "en_US.UTF-8",
        ["LC_CTYPE"] = "en_US.UTF-8",
        ["TERM"] = "xterm-256color",
    },
};

await terminal.StartSshAsync(options);
```

### SSH security and trust

The SSH transport supports:

- `KnownHostsSshHostKeyValidator`
- `ExpectedFingerprintSshHostKeyValidator`
- `RejectAllSshHostKeyValidator`

When `ExpectedHostKeyFingerprintSha256` is set, the session can enforce SHA-256 host-key pinning. When omitted, the default path falls back to OpenSSH `known_hosts`.

### SSH agent support

If you want agent-backed auth with SSH.NET, add `RoyalTerminal.Terminal.Transport.Ssh.SshNet.Agent`. It provides `SshNetAgentAuthenticationMethodContributor`.

### SSH bootstrap composition

For custom SSH backends, reuse `SshShellBootstrapCommandBuilder` so environment-variable export behavior stays consistent with the built-in SSH.NET transport.

### Current X11 limitation

`SshX11Options` exists in the shared option model, but the SSH.NET backend currently does not support X11 forwarding and will throw when enabled.

## Telnet, raw TCP, and serial

These transports are intentionally explicit and lightweight:

- `RawTcpTransportOptions` is for raw host/port stream sessions
- `TelnetTransportOptions` adds terminal type and optional startup command
- `SerialTransportOptions` captures baud rate, parity, stop bits, handshake, and newline

They all reuse the same `TerminalSessionDimensions` model so the UI/session contract stays uniform.

## Session profiles

Profile persistence is built into the shared `RoyalTerminal.Terminal` package. The core document model is `TerminalSessionProfilesDocument`.

```csharp
TerminalSessionProfilesDocument profiles = new()
{
    Profiles =
    [
        new TerminalSessionProfile
        {
            Id = "dev-ssh",
            DisplayName = "Dev SSH",
            Transport = new TerminalSessionTransportProfile
            {
                TransportId = TerminalTransportIds.Ssh,
                Ssh = new TerminalSessionSshSettings
                {
                    Host = "example.com",
                    Port = 22,
                    Username = "alice",
                    Authentication = new TerminalSessionSshAuthenticationSettings
                    {
                        UsePassword = true,
                        PasswordSecretId = "ssh/dev/password",
                    },
                },
            },
        },
    ],
};

ITerminalSessionProfileStore store = TerminalSessionProfileStoreFactory.CreateDefault();
await store.SaveAsync(profiles);

TerminalSessionProfilesDocument loaded = await store.LoadAsync();
ITerminalTransportOptions options = TerminalSessionProfileMapper.ToTransportOptions(loaded.Profiles[0]);
await terminal.StartSessionAsync(options);
```

This is the same persistence model used by the demo’s settings and profile editor.

## Secret storage

The repository includes pluggable secret protection and persistence:

- `ISshSecretStore`
- `ISshSecretProtector`
- `SshSecretProtectionFactory`
- `SecretStoreSshCredentialProvider`

The default factory provides a cross-platform secure default so secret handling is not hard-coded into the SSH transport itself.
