# Ghostling upstream audit — 2026-10-27

## Revision and complete delta

The [October 9 audit](ghostty-update-2026-10-09.md) recorded Ghostling at
`63842bf8e5e481160f81d348da9ff6fd27986798`. On October 27, both remote `HEAD`
and `refs/heads/main` still resolve to that exact revision. GitHub's
`repos/ghostty-org/ghostling/commits/main` endpoint independently reports the
same commit, committed on August 9 at 14:36:20 UTC:
[Merge pull request #17: update libghostty](https://github.com/ghostty-org/ghostling/commit/63842bf8e5e481160f81d348da9ff6fd27986798).

Reproducible checks:

```sh
git ls-remote https://github.com/ghostty-org/ghostling.git HEAD refs/heads/main
gh api repos/ghostty-org/ghostling/commits/main --jq '{sha,date:.commit.committer.date,subject:.commit.message,html_url}'
```

There are **zero intervening commits**. Consequently, the complete list of new
Ghostling terminal, presentation, rendering, input, build, and dependency changes
is empty. There is no new Ghostling behavior missing from either RoyalTerminal
backend, and no production patch or new regression test is required by this
delta. This result does not substitute for the separate Ghostty library and
application behavior audit against its newer upstream revision.

## Existing consumer behavior checked

The current [main.c](https://github.com/ghostty-org/ghostling/blob/63842bf8e5e481160f81d348da9ff6fd27986798/main.c)
was inspected to identify the consumer integration points, rather than treating
the unchanged commit hash as a claim that every terminal feature was retested.
RoyalTerminal already has these corresponding integration paths:

| Ghostling consumer behavior | RoyalTerminal implementation | Existing coverage |
| --- | --- | --- |
| Native key encoder, terminal keyboard options, modifier input | `DefaultTerminalInputAdapter.cs`, `GhosttyVtProcessor.cs`, managed Kitty keyboard encoder | `TerminalInputAdapterTests`, `ManagedKittyKeyboardParityTests`, `ManagedKittyKeyEncoderTests` |
| Native mouse encoder, terminal tracking mode, cell/pixel coordinates | `TerminalControl.cs`, `GhosttyVtProcessor.cs`, `ManagedMouseEncoder.cs` | `TerminalMouseProtocolTests`, `ManagedMouseModeParityTests`, `TerminalMouseShiftCaptureTests` |
| Wheel scrolling and scrollbar viewport movement | `TerminalControl.PixelScrolling.cs`, `VirtualizedTerminalScrollViewer.cs`, native/managed viewport sources | `TerminalControlHeadlessInteractionTests.PixelScrolling`, `TerminalPartialScrollTests` |
| Render row iteration, grapheme/style data, dirty rows and cursor | `GhosttyVtProcessor.cs`, `TerminalRenderViewport.cs`, `SkiaTerminalRenderer.cs` | `TerminalRenderViewportTests`, `GhosttyVtRenderOverscanTests`, managed grapheme lifecycle tests |
| Kitty image data, placement geometry and viewport clipping | Native/managed Kitty protocol implementations and `SkiaTerminalRenderer` image paths | `KittyGraphicsRuntimeParityTests`, `KittyGraphicsProtocolParityTests`, `GhosttyKittyPlacementMetadataTests` |
| Focus reports gated by terminal mode | `TerminalControl.cs` and both processor focus-state sources | Existing input/headless interaction and terminal mode coverage |

These are source mappings to existing implementation and tests; no runtime test
pass is claimed for this documentation-only audit. The current task's native,
managed, ABI and headless verification remains responsible for validating the
combined updated release.

## Integration decisions

Ghostling's unchanged [CMakeLists.txt](https://github.com/ghostty-org/ghostling/blob/63842bf8e5e481160f81d348da9ff6fd27986798/CMakeLists.txt)
still pins its Ghostty dependency to `f64f4aca2c29b554d111b36c3d946a9bddd159ff`.
That older example dependency is not RoyalTerminal's update target:
RoyalTerminal advances its own Ghostty pin and C ABI bindings independently.
The example's Raylib-specific drawing and input plumbing does not replace the
existing Avalonia/Skia services, richer keyboard handling, or managed engine.

No terminal semantics change is proposed from Ghostling, so there is no new
Windows Terminal/xterm.js divergence to select. The October 9 audit records the
existing comparison with their mouse dispatch and terminal state behavior;
those decisions continue to apply. PowerShell startup/output behavior is
unaffected by this zero-change Ghostling delta.
