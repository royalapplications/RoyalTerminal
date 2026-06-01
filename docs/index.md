---
layout: home

hero:
  name: RoyalTerminal
  text: Professional terminal infrastructure for .NET and Avalonia
  tagline: Multi-transport sessions, managed and native VT processing, regex text highlighting, framebuffer shaders, modular rendering packages, and first-class sample and validation tooling.
  image:
    src: /assets/royalterminal-mark.svg
    alt: RoyalTerminal
  actions:
    - theme: brand
      text: Get Started
      link: /articles/getting-started
    - theme: alt
      text: Package Guide
      link: /articles/packages
    - theme: alt
      text: API Reference
      link: /api/
    - theme: alt
      text: GitHub
      link: https://github.com/royalapplications/RoyalTerminal

features:
  - title: Avalonia-first terminal UI
    details: "`RoyalTerminal.Avalonia` provides a backend-neutral `TerminalControl` with theming, virtualization, regex text highlighting, framebuffer shaders, capture/replay, snapshot export, and rich input handling."
  - title: Multi-transport runtime
    details: "PTY, pipe, SSH, raw TCP, Telnet, and serial transports share a single session model and option surface."
  - title: Managed and native VT engines
    details: "Choose the managed `BasicVtProcessor`, the native Ghostty-backed processor, or the default factory with deterministic fallback behavior."
  - title: Modular rendering stack
    details: "Use the Skia cell renderer and shader post-process pipeline alone or add Ghostty renderer interop packages and native assets where they fit your host architecture."
  - title: Production validation surface
    details: "The repository includes an Avalonia demo, a control catalog, headless/UI tests, integration tests, native packaging checks, and benchmarks."
  - title: Repository-scale documentation
    details: "This guide covers the source tree outside the `external/ghostty` submodule with feature-led articles plus generated API reference pages for the public managed packages."
---

## Documentation

- [Getting Started](/articles/getting-started)
- [Architecture](/articles/architecture)
- [Package Guide](/articles/packages)
- [API Reference](/api/)
- [Embedding In Avalonia](/articles/avalonia-control)
- [Sessions, Profiles, And Settings](/articles/sessions-profiles-and-settings)
- [Session History And Scrollback](/articles/session-history)
- [Session Restart Semantics](/articles/session-restart-semantics)
- [Capture Formats](/articles/capture-formats)
- [Regex Text Highlighting](/articles/text-highlighting)
- [Transports And Remote Access](/articles/transports)
- [Terminal Engine And Screen State](/articles/vt-modes)
- [Rendering, Text, And Graphics](/articles/rendering-native)
- [Shader Support](/articles/shaders)
- [Applying Shaders](/articles/shaders-applying)
- [Skia Runtime Effect Shaders](/articles/shaders-skia-runtime-effect)
- [Ghostty/Shadertoy Shader Compatibility](/articles/shaders-ghostty-shadertoy)
- [Windows Terminal HLSL Shader Compatibility](/articles/shaders-windows-terminal-hlsl)
- [Ghostty Integration](/articles/ghostty-integration)
- [Samples And Tooling](/articles/samples-tooling)
- [Build, Test, And Release](/articles/build-test-release)
- [Troubleshooting](/articles/troubleshooting)
