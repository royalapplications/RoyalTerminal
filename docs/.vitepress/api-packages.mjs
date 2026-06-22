export const apiPackageGroups = [
  {
    text: "UI And Host",
    packages: [
      {
        packageId: "RoyalApps.RoyalTerminal.Avalonia",
        slug: "royalterminal-avalonia",
        project: "src/RoyalTerminal.Avalonia/RoyalTerminal.Avalonia.csproj",
        guideTitle: "Embedding In Avalonia",
        guideLink: "/articles/avalonia-control"
      },
      {
        packageId: "RoyalApps.RoyalTerminal.Avalonia.Settings",
        slug: "royalterminal-avalonia-settings",
        project: "src/RoyalTerminal.Avalonia.Settings/RoyalTerminal.Avalonia.Settings.csproj",
        guideTitle: "Sessions, Profiles, And Settings",
        guideLink: "/articles/sessions-profiles-and-settings"
      },
      {
        packageId: "RoyalApps.RoyalTerminal.Avalonia.Rendering.GhosttyInterop",
        slug: "royalterminal-avalonia-rendering-ghosttyinterop",
        project: "src/RoyalTerminal.Avalonia.Rendering.GhosttyInterop/RoyalTerminal.Avalonia.Rendering.GhosttyInterop.csproj",
        guideTitle: "Rendering, Text, And Graphics",
        guideLink: "/articles/rendering-native"
      }
    ]
  },
  {
    text: "Core And Services",
    packages: [
      {
        packageId: "RoyalApps.RoyalTerminal.Terminal",
        slug: "royalterminal-terminal",
        project: "src/RoyalTerminal.Terminal/RoyalTerminal.Terminal.csproj",
        guideTitle: "Terminal Engine And Screen State",
        guideLink: "/articles/vt-modes"
      },
      {
        packageId: "RoyalApps.RoyalTerminal.Terminal.Services.Contracts",
        slug: "royalterminal-terminal-services-contracts",
        project: "src/RoyalTerminal.Terminal.Services.Contracts/RoyalTerminal.Terminal.Services.Contracts.csproj",
        guideTitle: "Architecture",
        guideLink: "/articles/architecture"
      },
      {
        packageId: "RoyalApps.RoyalTerminal.Terminal.Services",
        slug: "royalterminal-terminal-services",
        project: "src/RoyalTerminal.Terminal.Services/RoyalTerminal.Terminal.Services.csproj",
        guideTitle: "Sessions, Profiles, And Settings",
        guideLink: "/articles/sessions-profiles-and-settings"
      },
      {
        packageId: "RoyalApps.RoyalTerminal.Unicode",
        slug: "royalterminal-unicode",
        project: "src/RoyalTerminal.Unicode/RoyalTerminal.Unicode.csproj",
        guideTitle: "Terminal Engine And Screen State",
        guideLink: "/articles/vt-modes"
      },
      {
        packageId: "RoyalApps.RoyalTerminal.Sixel",
        slug: "royalterminal-sixel",
        project: "src/RoyalTerminal.Sixel/RoyalTerminal.Sixel.csproj",
        guideTitle: "Terminal Engine And Screen State",
        guideLink: "/articles/vt-modes"
      }
    ]
  },
  {
    text: "VT And Terminal Engine",
    packages: [
      {
        packageId: "RoyalApps.RoyalTerminal.Terminal.Vt.Default",
        slug: "royalterminal-terminal-vt-default",
        project: "src/RoyalTerminal.Terminal.Vt.Default/RoyalTerminal.Terminal.Vt.Default.csproj",
        guideTitle: "Terminal Engine And Screen State",
        guideLink: "/articles/vt-modes"
      },
      {
        packageId: "RoyalApps.RoyalTerminal.Terminal.Vt.Managed",
        slug: "royalterminal-terminal-vt-managed",
        project: "src/RoyalTerminal.Terminal.Vt.Managed/RoyalTerminal.Terminal.Vt.Managed.csproj",
        guideTitle: "Terminal Engine And Screen State",
        guideLink: "/articles/vt-modes"
      },
      {
        packageId: "RoyalApps.RoyalTerminal.Terminal.Vt.Ghostty",
        slug: "royalterminal-terminal-vt-ghostty",
        project: "src/RoyalTerminal.Terminal.Vt.Ghostty/RoyalTerminal.Terminal.Vt.Ghostty.csproj",
        guideTitle: "Ghostty Integration",
        guideLink: "/articles/ghostty-integration"
      },
      {
        packageId: "RoyalApps.RoyalTerminal.GhosttySharp",
        slug: "royalterminal-ghosttysharp",
        project: "src/RoyalTerminal.GhosttySharp/RoyalTerminal.GhosttySharp.csproj",
        guideTitle: "Ghostty Integration",
        guideLink: "/articles/ghostty-integration",
        referenceMode: "source-index"
      }
    ]
  },
  {
    text: "PTY And Transports",
    packages: [
      {
        packageId: "RoyalApps.RoyalTerminal.Terminal.Pty.Platform",
        slug: "royalterminal-terminal-pty-platform",
        project: "src/RoyalTerminal.Terminal.Pty.Platform/RoyalTerminal.Terminal.Pty.Platform.csproj",
        guideTitle: "Transports And Remote Access",
        guideLink: "/articles/transports"
      },
      {
        packageId: "RoyalApps.RoyalTerminal.Terminal.Pty.Unix",
        slug: "royalterminal-terminal-pty-unix",
        project: "src/RoyalTerminal.Terminal.Pty.Unix/RoyalTerminal.Terminal.Pty.Unix.csproj",
        guideTitle: "Transports And Remote Access",
        guideLink: "/articles/transports"
      },
      {
        packageId: "RoyalApps.RoyalTerminal.Terminal.Pty.Windows",
        slug: "royalterminal-terminal-pty-windows",
        project: "src/RoyalTerminal.Terminal.Pty.Windows/RoyalTerminal.Terminal.Pty.Windows.csproj",
        guideTitle: "Transports And Remote Access",
        guideLink: "/articles/transports"
      },
      {
        packageId: "RoyalApps.RoyalTerminal.Terminal.Transport.Pty",
        slug: "royalterminal-terminal-transport-pty",
        project: "src/RoyalTerminal.Terminal.Transport.Pty/RoyalTerminal.Terminal.Transport.Pty.csproj",
        guideTitle: "Transports And Remote Access",
        guideLink: "/articles/transports"
      },
      {
        packageId: "RoyalApps.RoyalTerminal.Terminal.Transport.Pipe",
        slug: "royalterminal-terminal-transport-pipe",
        project: "src/RoyalTerminal.Terminal.Transport.Pipe/RoyalTerminal.Terminal.Transport.Pipe.csproj",
        guideTitle: "Transports And Remote Access",
        guideLink: "/articles/transports"
      },
      {
        packageId: "RoyalApps.RoyalTerminal.Terminal.Transport.Raw",
        slug: "royalterminal-terminal-transport-raw",
        project: "src/RoyalTerminal.Terminal.Transport.Raw/RoyalTerminal.Terminal.Transport.Raw.csproj",
        guideTitle: "Transports And Remote Access",
        guideLink: "/articles/transports"
      },
      {
        packageId: "RoyalApps.RoyalTerminal.Terminal.Transport.Telnet",
        slug: "royalterminal-terminal-transport-telnet",
        project: "src/RoyalTerminal.Terminal.Transport.Telnet/RoyalTerminal.Terminal.Transport.Telnet.csproj",
        guideTitle: "Transports And Remote Access",
        guideLink: "/articles/transports"
      },
      {
        packageId: "RoyalApps.RoyalTerminal.Terminal.Transport.Serial",
        slug: "royalterminal-terminal-transport-serial",
        project: "src/RoyalTerminal.Terminal.Transport.Serial/RoyalTerminal.Terminal.Transport.Serial.csproj",
        guideTitle: "Transports And Remote Access",
        guideLink: "/articles/transports"
      },
      {
        packageId: "RoyalApps.RoyalTerminal.Terminal.Transport.Ssh.Abstractions",
        slug: "royalterminal-terminal-transport-ssh-abstractions",
        project: "src/RoyalTerminal.Terminal.Transport.Ssh.Abstractions/RoyalTerminal.Terminal.Transport.Ssh.Abstractions.csproj",
        guideTitle: "Transports And Remote Access",
        guideLink: "/articles/transports"
      },
      {
        packageId: "RoyalApps.RoyalTerminal.Terminal.Transport.Ssh.SshNet",
        slug: "royalterminal-terminal-transport-ssh-sshnet",
        project: "src/RoyalTerminal.Terminal.Transport.Ssh.SshNet/RoyalTerminal.Terminal.Transport.Ssh.SshNet.csproj",
        guideTitle: "Transports And Remote Access",
        guideLink: "/articles/transports"
      },
      {
        packageId: "RoyalApps.RoyalTerminal.Terminal.Transport.Ssh.SshNet.Agent",
        slug: "royalterminal-terminal-transport-ssh-sshnet-agent",
        project: "src/RoyalTerminal.Terminal.Transport.Ssh.SshNet.Agent/RoyalTerminal.Terminal.Transport.Ssh.SshNet.Agent.csproj",
        guideTitle: "Transports And Remote Access",
        guideLink: "/articles/transports"
      }
    ]
  },
  {
    text: "Rendering And Native Interop",
    packages: [
      {
        packageId: "RoyalApps.RoyalTerminal.Rendering.Contracts",
        slug: "royalterminal-rendering-contracts",
        project: "src/RoyalTerminal.Rendering.Contracts/RoyalTerminal.Rendering.Contracts.csproj",
        guideTitle: "Rendering, Text, And Graphics",
        guideLink: "/articles/rendering-native"
      },
      {
        packageId: "RoyalApps.RoyalTerminal.Rendering.Text",
        slug: "royalterminal-rendering-text",
        project: "src/RoyalTerminal.Rendering.Text/RoyalTerminal.Rendering.Text.csproj",
        guideTitle: "Rendering, Text, And Graphics",
        guideLink: "/articles/rendering-native"
      },
      {
        packageId: "RoyalApps.RoyalTerminal.Shaders",
        slug: "royalterminal-shaders",
        project: "src/RoyalTerminal.Shaders/RoyalTerminal.Shaders.csproj",
        guideTitle: "Shader Support",
        guideLink: "/articles/shaders"
      },
      {
        packageId: "RoyalApps.RoyalTerminal.Rendering.Skia",
        slug: "royalterminal-rendering-skia",
        project: "src/RoyalTerminal.Rendering.Skia/RoyalTerminal.Rendering.Skia.csproj",
        guideTitle: "Rendering, Text, And Graphics",
        guideLink: "/articles/rendering-native"
      },
      {
        packageId: "RoyalApps.RoyalTerminal.Rendering.Interop.Ghostty",
        slug: "royalterminal-rendering-interop-ghostty",
        project: "src/RoyalTerminal.Rendering.Interop.Ghostty/RoyalTerminal.Rendering.Interop.Ghostty.csproj",
        guideTitle: "Ghostty Integration",
        guideLink: "/articles/ghostty-integration"
      },
      {
        packageId: "RoyalApps.RoyalTerminal.Rendering.Interop.Ghostty.Skia",
        slug: "royalterminal-rendering-interop-ghostty-skia",
        project: "src/RoyalTerminal.Rendering.Interop.Ghostty.Skia/RoyalTerminal.Rendering.Interop.Ghostty.Skia.csproj",
        guideTitle: "Ghostty Integration",
        guideLink: "/articles/ghostty-integration"
      }
    ]
  }
];

export const apiPackages = apiPackageGroups.flatMap((group) =>
  group.packages.map((pkg) => ({
    ...pkg,
    group: group.text
  }))
);

export const nativeAssetPackages = [
  "RoyalTerminal.GhosttySharp.Native.OSX",
  "RoyalTerminal.GhosttySharp.Native.Win64",
  "RoyalTerminal.GhosttySharp.Native.Linux64"
];
