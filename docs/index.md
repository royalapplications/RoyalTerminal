---
layout: home

hero:
  name: RoyalTerminal
  text: Professional terminal infrastructure for .NET and Avalonia
  tagline: Multi-transport sessions, workspace restore, split panes, shell-integrated command history, managed and native VT processing, regex text highlighting, framebuffer shaders, modular rendering packages, and first-class sample and validation tooling.
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
    details: "`RoyalApps.RoyalTerminal.Avalonia` provides a backend-neutral `TerminalControl` with theming, virtualization, regex text highlighting, framebuffer shaders, capture/replay, snapshot export, and rich input handling."
  - title: Multi-transport runtime
    details: "PTY, pipe, SSH, raw TCP, Telnet, and serial transports share a single session model and option surface."
  - title: Product workflow state
    details: "`RoyalApps.RoyalTerminal.Terminal` includes versioned workspace, pane, shell-integration, command-history, and suggestion contracts so hosts can restore sessions without mixing workflow state into terminal emulation."
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

<div class="home-docs-grid">
  <section class="home-docs-group home-docs-group-primary">
    <p class="home-docs-eyebrow">Start here</p>
    <h3>Plan your integration</h3>
    <p>Choose the right package set, understand the architecture, and jump into the generated API surface.</p>
    <ul>
      <li><a href="articles/getting-started">Getting Started</a></li>
      <li><a href="articles/architecture">Architecture</a></li>
      <li><a href="articles/packages">Package Guide</a></li>
      <li><a href="api/">API Reference</a></li>
    </ul>
  </section>
  <section class="home-docs-group">
    <p class="home-docs-eyebrow">Embedding</p>
    <h3>Avalonia host workflows</h3>
    <p>Wire the control, configure sessions, preserve history, and export terminal state.</p>
    <ul>
      <li><a href="articles/avalonia-control">Embedding in Avalonia</a></li>
      <li><a href="articles/sessions-profiles-and-settings">Sessions, Profiles, and Settings</a></li>
      <li><a href="articles/workspace-restore">Workspace Restore</a></li>
      <li><a href="articles/split-panes">Split Panes</a></li>
      <li><a href="articles/command-history-and-suggestions">Command History and Suggestions</a></li>
      <li><a href="articles/session-history">Session History and Scrollback</a></li>
      <li><a href="articles/session-restart-semantics">Session Restart Semantics</a></li>
      <li><a href="articles/session-restart-reference-analysis">Session Restart Reference Analysis</a></li>
      <li><a href="articles/capture-formats">Capture Formats</a></li>
    </ul>
  </section>
  <section class="home-docs-group">
    <p class="home-docs-eyebrow">Runtime</p>
    <h3>Terminal behavior</h3>
    <p>Review transports, screen state, highlighting, Ghostty interop, and native compatibility notes.</p>
    <ul>
      <li><a href="articles/transports">Transports and Remote Access</a></li>
      <li><a href="articles/vt-modes">Terminal Engine and Screen State</a></li>
      <li><a href="articles/shell-integration">Shell Integration</a></li>
      <li><a href="articles/text-highlighting">Regex Text Highlighting</a></li>
      <li><a href="articles/ghostty-integration">Ghostty Integration</a></li>
      <li><a href="articles/windows-x64-native-compatibility">Windows x64 Native Compatibility</a></li>
    </ul>
  </section>
  <section class="home-docs-group">
    <p class="home-docs-eyebrow">Rendering</p>
    <h3>Text, graphics, and shaders</h3>
    <p>Understand the rendering stack and shader compatibility across Skia, Ghostty, and Windows Terminal models.</p>
    <ul>
      <li><a href="articles/rendering-native">Rendering, Text, and Graphics</a></li>
      <li><a href="articles/shaders">Shader Support</a></li>
      <li><a href="articles/shaders-applying">Applying Shaders</a></li>
      <li><a href="articles/shaders-skia-runtime-effect">Skia Runtime Effect Shaders</a></li>
      <li><a href="articles/shaders-ghostty-shadertoy">Ghostty/Shadertoy Shader Compatibility</a></li>
      <li><a href="articles/shaders-windows-terminal-hlsl">Windows Terminal HLSL Shader Compatibility</a></li>
    </ul>
  </section>
  <section class="home-docs-group">
    <p class="home-docs-eyebrow">Operations</p>
    <h3>Ship and troubleshoot</h3>
    <p>Use sample tooling, validate release workflows, and diagnose common integration issues.</p>
    <ul>
      <li><a href="articles/samples-tooling">Samples and Tooling</a></li>
      <li><a href="articles/demo-product-shell">Demo Product Shell</a></li>
      <li><a href="articles/build-test-release">Build, Test, and Release</a></li>
      <li><a href="articles/troubleshooting">Troubleshooting</a></li>
    </ul>
  </section>
</div>
