---
title: Getting Started
---

# Getting Started

RoyalTerminal is a modular terminal stack, not a single UI control package. The smallest successful setup depends on whether you want:

- a managed Avalonia terminal with no native VT dependency
- an Avalonia terminal that can use the official Ghostty VT engine
- lower-level transport, rendering, or profile infrastructure without the demo app

## Choose the package set

| Scenario | Required packages |
| --- | --- |
| Avalonia terminal with managed VT fallback | `RoyalTerminal.Avalonia` |
| Avalonia terminal with native Ghostty VT available | `RoyalTerminal.Avalonia`, `RoyalTerminal.Terminal.Vt.Ghostty`, and the matching native asset package for your OS |
| Avalonia settings surface | add `RoyalTerminal.Avalonia.Settings` |
| SSH agent auth for the SSH.NET transport | add `RoyalTerminal.Terminal.Transport.Ssh.SshNet.Agent` |

Native asset packages:

- macOS: `RoyalTerminal.GhosttySharp.Native.OSX`
- Windows: `RoyalTerminal.GhosttySharp.Native.Win64`
- Linux: `RoyalTerminal.GhosttySharp.Native.Linux64`

## Install the packages

Managed-only Avalonia setup:

```bash
dotnet add package RoyalTerminal.Avalonia
```

Avalonia plus native Ghostty VT:

```bash
dotnet add package RoyalTerminal.Avalonia
dotnet add package RoyalTerminal.Terminal.Vt.Ghostty
dotnet add package RoyalTerminal.GhosttySharp.Native.OSX
```

Replace the last package with the correct Windows or Linux native package for your target runtime.

## Add a `TerminalControl`

In XAML, host `TerminalControl` like any other Avalonia control:

```xml
<Window
    xmlns="https://github.com/avaloniaui"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:rt="clr-namespace:RoyalTerminal.Avalonia.Controls;assembly=RoyalTerminal.Avalonia">
  <rt:TerminalControl
      x:Name="Terminal"
      Columns="120"
      Rows="36"
      ScrollbackLimit="10000"
      TerminalFontSize="14" />
</Window>
```

## Start a local PTY session

`StartSessionAsync(...)` is the preferred entry point. It routes through the transport abstraction and keeps the control aligned with the shared session-service pipeline.

```csharp
using RoyalTerminal.Avalonia.Controls;
using RoyalTerminal.Terminal;

public static async Task StartAsync(TerminalControl terminal)
{
    TerminalSessionDimensions dimensions = new(
        Columns: 120,
        Rows: 36,
        WidthPixels: 1200,
        HeightPixels: 800);

    PtyTransportOptions options = new(
        Command: null,
        WorkingDirectory: Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        Environment: null,
        Dimensions: dimensions);

    await terminal.StartSessionAsync(options);
}
```

`StartPty(...)` still exists for compatibility, but new integrations should prefer `StartSessionAsync(...)` or the typed helpers such as `StartSshAsync(...)`.

## Start an SSH session

The SSH transport uses the same session pipeline and option model:

```csharp
using RoyalTerminal.Avalonia.Controls;
using RoyalTerminal.Terminal;

TerminalControl terminal = new();

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
};

await terminal.StartSshAsync(options);
```

If `ExpectedHostKeyFingerprintSha256` is not set, the default trust path falls back to `KnownHostsSshHostKeyValidator` and OpenSSH `known_hosts`.

## Enable native VT explicitly

If you want strict native-VT behavior instead of automatic fallback, set `VtProcessorPreference` to `Native` and add the Ghostty VT provider package plus native runtime assets:

```csharp
using RoyalTerminal.Avalonia.Controls;
using RoyalTerminal.Avalonia.Services;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Services;

TerminalControl terminal = new(
    new TerminalSessionService(),
    new DefaultTerminalInputAdapter(),
    new DefaultTerminalSelectionService(),
    new DefaultTerminalScrollService(),
    new DefaultVtProcessorFactory([new GhosttyVtProcessorProvider()]),
    new DefaultPtyFactory())
{
    VtProcessorPreference = VtProcessorPreference.Native,
};
```

Use `Auto` when you want the native processor when available and the managed processor otherwise.

## Where to go next

- Read [Architecture](/articles/architecture) for the package boundaries and runtime flow.
- Read [Package Guide](/articles/packages) before composing a custom package set.
- Read [Sessions, Profiles, And Settings](/articles/sessions-profiles-and-settings) if your app needs saved profiles, reusable settings UI, themes, or capture files.
- Read [Transports And Remote Access](/articles/transports) for PTY, SSH, raw TCP, Telnet, serial, and secret handling.
- Read [Terminal Engine And Screen State](/articles/vt-modes) if you need deterministic managed vs native behavior, input encoding, or direct screen-model access.
