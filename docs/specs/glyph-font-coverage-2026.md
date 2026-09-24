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

## Delivered status

| Feature | Status and boundary |
| --- | --- |
| Host coverage contract | Implemented as optional caller-owned `ITerminalGlyphCoverageSource` / `ITerminalGlyphCoverageSink`; no Avalonia or Skia types in the contract |
| Managed query replies | Implemented: empty, `system`, `glossary`, `system,glossary`; source failure preserves the core reply |
| Native adapter replies | Implemented by augmenting complete canonical query replies only; ordinary PTY/paste/other replies are unchanged; no C ABI/binary change |
| Shared Skia host | Configured regular system/file font plus normal fallback; verifies glyph presence and rejects LastResort placeholders |
| Ownership/performance | Lazy independent resources, serialized query/dispose, 4,096 scalar-result cap and bounded fallback caches; cached queries allocate zero managed bytes in the focused test |
| Host lifecycle | Font/renderer and engine changes rebind the source; invalid scalars are rejected before font discovery; disposed sources return false |
| Explicit exclusions | COLR/downloaded-font support, arbitrary multi-scalar/style coverage guarantees, automatic OS-font-install detection and Ghostty CoreText/GPU raster parity |

## Validation

At code commit `e6aaca3`, full local Release validation with CI flags and
`ROYALTERMINAL_REQUIRE_NATIVE_TESTS=1` passed **3,978 unit/headless + 240 native
integration = 4,218 tests**, with **16 conditional unit skips and zero failures**.
Both projects wrote `glyph-font-coverage-release.trx`.
The final focused glyph suite passed **151 tests with zero skips/failures**
(`glyph-font-coverage-focused.trx`); the full solution Release build passed with
**zero warnings and errors**.

The focused suite covers both adapters' four coverage states, caller ownership,
clear/reset, bytewise split APCs, synchronized-output working/published glossary
separation, malformed/unrelated native reply pass-through, invalid scalars,
provider exceptions, configured-file fallback, LastResort exclusion, bounded
caches, allocation-free warmed lookups, concurrent disposal and headless host
font/engine replacement. Cross-platform CI status is tracked separately in the PR.
No throughput or renderer-speed improvement is claimed for this capability.

Subsequent CI `36007943014` at `f0acddb` failed one Linux file-font fallback
assertion; macOS/Windows managed jobs and all six native builds passed. The
[plain-formatter follow-up](managed-plain-formatter-2026.md) reproduced the cause
on Ubuntu ARM64 and adds verified global discovery after a family-specific match
fails. Fresh follow-up validation is recorded there and in the PR; the earlier
full local pass is not presented as cross-platform sign-off.
