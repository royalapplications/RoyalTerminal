# Downloaded-glyph host font coverage (September 24, 2026)

## Reference decision

Ghostty `622b4eecd` documents APC `25a1;q` coverage as empty, `system`,
`glossary`, or `system,glossary` in `src/terminal/apc/glyph.zig`.
`glyph/execute.zig` explicitly delegates system-font coverage to the host;
the pinned terminal/stream handler only produces glossary coverage.
[Ghostty #13929](https://github.com/ghostty-org/ghostty/pull/13929) is the
downloaded-glyph feature reference, not evidence that upstream already supplies
the host integration implemented here.

Windows Terminal's `StateMachine::_EventSosPmApcString` ignores APC contents.
xterm.js exposes `InputHandler.registerApcHandler`, but the checked reference has
no built-in `25a1` glyph coverage handler. RoyalTerminal therefore follows
Ghostty's documented wire semantics, with its own Skia host implementation.
There is no PowerShell/shell behavior change.

## Scope

- Optional, framework-independent coverage source/sink contracts keep font
  resolution outside VT parsing. Without a source, preserve glossary-only replies.
- Managed queries and native query replies use the same bounded response helper;
  host failure/invalid Unicode must not fabricate system coverage or lose replies.
- The shared Skia host checks its configured regular font (system family or file)
  and the existing fallback resolver, then verifies a real glyph instead of tofu.
  LastResort fonts are excluded using the OpenType `head.flags` bit 14 and Apple's
  family-name fallback. This follows Ghostty's `font/discovery.zig` LastResort
  exclusion and the [OpenType font-header specification](https://learn.microsoft.com/en-us/typography/opentype/otspec190/head).
- Coverage resources are separately owned and synchronized with disposal, not
  borrowed from render-thread caches. Creation is lazy; cache size is bounded.
- Font/renderer changes rebind both engines. Registrations, clear/reset, split
  input and synchronized-output working glossary semantics remain authoritative
  in the processors; host font coverage is not serialized terminal state.

This does not add COLR formats, downloaded fonts, a new native C ABI, or Ghostty's
CoreText/GPU renderer. Coverage is for a scalar in the current regular/fallback
font configuration, not proof of arbitrary multi-scalar shaping or identical
pixels. Dynamic OS font installation requires a renderer/font refresh.
