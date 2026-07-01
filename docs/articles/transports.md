---
title: Transports And Remote Access
---

# Transports And Remote Access

RoyalTerminal does not treat a terminal session as "always a local PTY". The public transport surface is broader than that on purpose: local shells, child processes, SSH, raw TCP, Telnet, and serial all fit into the same session model.

## One session model, several transport families

Every transport starts from `ITerminalTransportOptions` and a stable `TransportId`. That shared boundary is what lets `TerminalControl`, `TerminalSessionService`, settings UI, and profile persistence all work across very different backends.

| Transport | Options type | Typical use |
| --- | --- | --- |
| Local PTY | `PtyTransportOptions` | Interactive shell with ConPTY or `forkpty` |
| Child process pipe | `PipeTransportOptions` | Output capture, automation, or non-interactive process IO |
| SSH | `SshTransportOptions` | Remote shell sessions and tunneling |
| Raw TCP | `RawTcpTransportOptions` | Plain byte streams |
| Telnet | `TelnetTransportOptions` | Legacy text terminals with negotiation |
| Serial | `SerialTransportOptions` | Devices, consoles, and embedded targets |

## The common runtime boundary

The transport layer is deliberately small and composable:

| Type | Why it matters |
| --- | --- |
| `ITerminalTransportOptions` | Shared startup contract for all transports. |
| `TerminalSessionDimensions` | Shared logical and pixel dimensions. |
| `ITerminalTransport` | Running transport abstraction. |
| `ITerminalPtyTransport` | Optional capability for transports backed by a PTY. |
| `ITerminalTransportProvider` | Factory participant for one transport family. |
| `ITerminalTransportFactory` | Factory used by hosts and session services. |
| `CompositeTerminalTransportFactory` | Multi-provider selector used throughout the repo. |
| `TerminalTransportIds` | Shared string ids for all standard transports. |
| `TerminalCommandSpec` | Executable path plus argument list. |

The pattern is simple: build the options record, pass it to a factory, and let the session service wire the result to the active VT processor.

## Local process and PTY sessions

Local shells use the PTY path because that is what full-screen TUIs and interactive shells expect. Pipe mode exists for simpler process IO scenarios that do not want a pseudo-terminal.

### PTY-facing contracts and implementations

| Type | Purpose |
| --- | --- |
| `IPty` | Minimal pseudo-terminal abstraction. |
| `IPtyFactory` | Factory for creating PTYs. |
| `DefaultPtyFactory` | Platform-selecting PTY factory. |
| `UnixPty` | Unix `forkpty` implementation. |
| `WindowsPty` | Windows ConPTY implementation. |
| `PtyTerminalTransport` | PTY-backed transport runtime. |
| `PtyTerminalTransportProvider` | Provider for PTY sessions. |
| `PtyTransportOptions` | PTY startup options. |

### Pipe process sessions

| Type | Purpose |
| --- | --- |
| `PipeTerminalTransport` | Child-process pipe transport. |
| `PipeTerminalTransportProvider` | Provider for pipe sessions. |
| `PipeTransportOptions` | Pipe process options, including stderr merge behavior. |

## Network and device transports

These transports all stay intentionally small. They are useful because they let the rest of the stack remain transport-agnostic.

| Type | Purpose |
| --- | --- |
| `RawTcpTerminalTransport` | Plain TCP transport runtime. |
| `RawTcpTerminalTransportProvider` | Provider for raw TCP sessions. |
| `RawTcpTransportOptions` | Host and port for raw TCP sessions. |
| `TelnetTerminalTransport` | Telnet transport runtime. |
| `TelnetTerminalTransportProvider` | Provider for Telnet sessions. |
| `TelnetTransportOptions` | Telnet host, port, terminal type, and optional startup command. |
| `SerialTerminalTransport` | Serial line transport runtime. |
| `SerialTerminalTransportProvider` | Provider for serial sessions. |
| `TerminalSerialParity` | Serial parity enum. |
| `TerminalSerialStopBits` | Serial stop-bit enum. |
| `TerminalSerialHandshake` | Serial handshake enum. |
| `SerialTransportOptions` | Serial port name, baud rate, data bits, parity, stop bits, handshake, and newline settings. |

## SSH as a first-class remote session

SSH is the richest transport in the library because it has to cover trust, credentials, PTY requests, terminal type, environment bootstrap, proxies, forwarding, and operational policy.

### Runtime SSH option model

| Type | Purpose |
| --- | --- |
| `SshEndpointOptions` | Host, port, and username. |
| `SshAuthenticationOptions` | Runtime auth choices: password, keys, and agent usage. |
| `SshProxyType` | Proxy type enum. |
| `SshProxyOptions` | Proxy connection settings. |
| `SshPortForwardMode` | Local, remote, or dynamic forwarding mode. |
| `SshPortForwardOptions` | One forwarding rule. |
| `SshX11Options` | X11 forwarding settings. |
| `SshPolicyOptions` | Keepalive and connect timeout settings. |
| `SshTransportOptions` | Full SSH startup options including endpoint, auth, environment, proxy, forwarding, X11, and host-key pinning. |

### Host-key trust policy

RoyalTerminal keeps host-key validation pluggable instead of burying it inside the SSH transport:

| Type | Purpose |
| --- | --- |
| `SshHostKeyInfo` | Host-key metadata passed into validators. |
| `ISshHostKeyValidator` | Trust-policy contract. |
| `KnownHostsSshHostKeyValidator` | OpenSSH `known_hosts` validator. |
| `ExpectedFingerprintSshHostKeyValidator` | Strict fingerprint pinning validator. |
| `RejectAllSshHostKeyValidator` | Explicit deny-all validator. |

### SSH.NET integration

The default SSH runtime is built on SSH.NET, but the auth method list is still extensible:

| Type | Purpose |
| --- | --- |
| `ISshNetAuthenticationMethodContributor` | Adds auth methods to the SSH.NET runtime. |
| `NullSshCredentialProvider` | No-op credential provider for externally managed auth. |
| `SshNetTerminalTransport` | SSH.NET transport runtime. |
| `SshNetTerminalTransportProvider` | Provider for `SshTransportOptions`. |
| `SshNetAgentAuthenticationMethodContributor` | Agent-backed auth contributor package. |

## Secret storage and credential resolution

RoyalTerminal separates credential lookup, secret persistence, and secret protection. That makes the SSH transport easier to host in real applications with policy constraints.

### Secret and credential contracts

| Type | Purpose |
| --- | --- |
| `SshCredentialRequest` | Request for credentials. |
| `SshResolvedCredentials` | Resolved credential payload. |
| `ISshCredentialProvider` | Async credential source contract. |
| `ISshSecretStore` | Secret persistence contract. |
| `ISshSecretProtector` | Secret protector contract. |

### Built-in secret stores and protectors

| Type | Purpose |
| --- | --- |
| `InMemorySshSecretStore` | In-memory store for tests or short-lived sessions. |
| `EnvironmentVariableSshSecretStore` | Environment-variable backed store. |
| `JsonFileSshSecretStore` | Plain JSON file store. |
| `ProtectedJsonFileSshSecretStore` | JSON file store protected by an `ISshSecretProtector`. |
| `CompositeSshSecretStore` | Multi-store lookup chain. |
| `SecretStoreSshCredentialProvider` | Credential provider backed by a secret store. |
| `NoOpSshSecretProtector` | Pass-through protector. |
| `DpapiSshSecretScope` | Windows DPAPI scope enum. |
| `DpapiSshSecretProtector` | Windows DPAPI protector. |
| `AesGcmSshSecretProtector` | Cross-platform AES-GCM file-key protector. |
| `SshSecretProtectionFactory` | Factory for default protectors and stores. |
| `SshShellBootstrapCommandBuilder` | Safe bootstrap command builder for environment variables, optional shell-integration bootstrap scripts, and startup commands. |

## How the articles map to the code

The runtime transport implementations live in the transport packages, but the durable document model for configuring them lives in [Sessions, Profiles, And Settings](/articles/sessions-profiles-and-settings). That separation is deliberate:

- this article is about starting and running sessions
- the session/profile article is about saving and editing them

That is the same split Ghostty uses between conceptual feature pages and lower-level reference material.
