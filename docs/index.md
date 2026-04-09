---
layout: home

hero:
  name: RoyalTerminal
  text: Professional terminal infrastructure for .NET and Avalonia
  tagline: Multi-transport sessions, managed and native VT processing, modular rendering packages, and first-class sample and validation tooling.
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
      text: GitHub
      link: https://github.com/royalapplications/RoyalTerminal

features:
  - title: Avalonia-first terminal UI
    details: "`RoyalTerminal.Avalonia` provides a backend-neutral `TerminalControl` with theming, virtualization, capture/replay, snapshot export, and rich input handling."
  - title: Multi-transport runtime
    details: "PTY, pipe, SSH, raw TCP, Telnet, and serial transports share a single session model and option surface."
  - title: Managed and native VT engines
    details: "Choose the managed `BasicVtProcessor`, the native Ghostty-backed processor, or the default factory with deterministic fallback behavior."
  - title: Modular rendering stack
    details: "Use the Skia cell renderer alone or add Ghostty renderer interop packages and native assets where they fit your host architecture."
  - title: Production validation surface
    details: "The repository includes an Avalonia demo, a control catalog, headless/UI tests, integration tests, native packaging checks, and benchmarks."
  - title: Repository-scale documentation
    details: "This guide covers the source tree outside the `external/ghostty` submodule and maps features back to concrete packages and workflows."
---

## Documentation

- [Getting Started](/articles/getting-started)
- [Architecture](/articles/architecture)
- [Package Guide](/articles/packages)
- [Avalonia Control](/articles/avalonia-control)
- [Transport Sessions](/articles/transports)
- [VT Processors And Modes](/articles/vt-modes)
- [Rendering And Native Runtime](/articles/rendering-native)
- [Samples And Tooling](/articles/samples-tooling)
- [Build, Test, And Release](/articles/build-test-release)
- [Troubleshooting](/articles/troubleshooting)
