# libghostty update — November 7, 2026

RoyalTerminal updates its Ghostty submodule and both terminal engines for the
libghostty 1.0 ABI and the latest inspected upstream source. This report records
the exact comparison boundary; it does not equate a terminal library update with
implementing Ghostty's separate desktop application.

## Sources and dependency decisions

| Dependency/source | Previous | Inspected target | Decision |
| --- | --- | --- | --- |
| RoyalTerminal main | `4f5d6689` local main | `c5e5365c08733ea7df42eec98fd505f1cfcef70b` | Fast-forward main before branching; PR #128 opened as a draft at the start. |
| Ghostty submodule | `4a4fc017940bec38c93b278c10c6a18c591f3e63` | `5813c9461d5af78b6307a1254d00b1bc190eaccd` | Pin inspected main, with recursive submodules; no edits inside the submodule. |
| libghostty version | Pre-1.0 API | `1.1.0-dev` | Includes `vt-1.0.0` (`0fa3d577a5614150ff8e2c152de5fb9d5c6d2dec`). Library version is independent of the desktop version. |
| Ghostling | `63842bf8e5e481160f81d348da9ff6fd27986798` | Same HEAD/main | No intervening commits or dependency update to import. Rechecked consumer source and the [existing audit](ghostling-update-2026-10-27.md). Its older demonstration pin is not RoyalTerminal's dependency target. |
| Zig | 0.17.0 | 0.17.0 | Keep the required toolchain; local archive checksum verified before building. |
| Native overlays | Previous source hashes | Seven updated source hashes | All replacement fragments matched. Frozen grid references no longer contain `size`; extension checks follow the new layout. |
| Linux packaging | `share/pkgconfig` assumption | `lib/pkgconfig` | Follow upstream's new install directory and retain the static archive filename correction. Verify versioned ELF exports and both consumer link modes. |

Primary sources: [libghostty 1.0 announcement](https://mitchellh.com/writing/libghostty-1-0),
[Ghostty pinned source](https://github.com/ghostty-org/ghostty/tree/5813c9461d5af78b6307a1254d00b1bc190eaccd),
[Ghostling](https://github.com/ghostty-org/ghostling/tree/63842bf8e5e481160f81d348da9ff6fd27986798).

## Complete native ABI reconciliation

The [generated ABI inventory](ghostty-abi-inventory-2026.md) accounts for all
**216 types, 31 callback signatures and 209 native VT entry points**, compared
with 187 types and 205 entry points previously. Every export is checked against
both the installed library and its managed declaration. The audit now verifies
return/argument shapes, scalar widths, pointer versus value passing, Cdecl, and
one-byte bool marshalling. Mutation tests prove that incorrect declarations are
rejected. CI runs this audit in addition to the generated runtime layout tests.

| API change | RoyalTerminal implementation | Validation |
| --- | --- | --- |
| `ghostty_version`, `ghostty_sized_init` | New direct bindings; frozen versus sized structs follow the new contract. | `GhosttyOneApiTests`, generated layouts. |
| Key/mouse `setopt` and `setopt_from_terminal` become `set` and `set_from_terminal` | Updated entry points and result handling; ergonomic wrapper method names retained. Explicit Cdecl on older imports. | Key/mouse integration suites; signature audit. |
| Setter/getter key enums reserve INVALID zero and return UNSUPPORTED for unknown keys | Regenerated enum values; updated selectors and wrapper checks. | Generated enum checks; invalid-key integration tests. |
| Key UTF-8 and Kitty pixels use `GhosttyString` | Updated pointer/length values; borrowed memory lifetime remains explicit. | Key-event, image extraction and ownership tests. |
| Graphemes use `GhosttyCodepointBuffer` | Pass pointer and capacity together; bounded copy cannot overrun caller storage. | Exact and undersized-buffer integration tests. |
| Frozen grid references, selections and effect callback payloads | Remove obsolete size fields/checks; allocator vtable includes four reserved pointer slots. | All field offsets/sizes and callback signatures; effects integration. |
| Compact style colors and flags | Four-byte tagged colors, 16-byte C style, explicit flags/reserved fields; preserve ergonomic style properties. | Packed-field independence, styles, rendering, generated ABI checks. |
| Unified cursor-style and image-layer enums | Replace duplicate enum declarations and update consumers. | Compile-time named enum references; rendering/overscan tests. |
| Formatter creation takes three arguments; options move to `formatter_set/get` | Wrapper translates its options into keyed settings and extras flags, frees failed initialization, retains selection ownership rules. | Formatter round trips, defaults/reset/unknown keys. |
| Removed terminal selection-format buffer/alloc exports | Existing high-level selection formatting routes through the formatter object. | Selection/formatting integration tests. |
| Search next/previous become dedicated exported functions | Existing search wrapper calls `search_select_next/prev`. | Search selection integration suite. |
| Pointer-based selection/size-report/SGR inputs and result-returning viewport/mouse/OSC calls | Update native signatures and failure handling. | Full function signature audit and runtime integration. |
| Null terminal option resets defaults | Nullable high-level scrollback setters pass the explicit unlimited value, preserving their documented contract; `ClearOption` remains reset-to-default. | New unlimited/reset distinction test; runtime quota differential tests. |
| Glyph Protocol defaults off in the C embedder API | RoyalTerminal's high-level constructor opts in because both engines and the renderer support it. Raw API default remains upstream's. | Glyph extraction/runtime parity tests. |
| Stable ABI release infrastructure, manifest typing, ELF versioning | Consume upstream's release baseline and typed manifest; extension `ghostty_*` exports use the same ELF version node. | Source audit, native target/history tests, ELF inspection and Linux consumer links. |

Ten exported names were added and six removed. Four additions replace the four
encoder `setopt` names; two removals are the retired terminal-selection-format
helpers. Five Wasm-only helpers remain outside native exports, as documented by
upstream. Legacy managed formatter options are configuration values only and are
never passed using their retired native layouts.

## Terminal behavior and managed parity

Windows Terminal's `src/terminal/adapter/adaptDispatch.cpp` and xterm.js's
`src/common/InputHandler.ts` were inspected before implementing column controls.
RoyalTerminal follows Ghostty for its native/managed differential contract.
Windows Terminal also restricts column edits to the scrolling region, but its
horizontal-index behavior outside vertical margins differs; Ghostty's no-op at
the horizontal boundary is intentional and tested. xterm.js column editing uses
its vertical scrolling region without an equivalent complete LR-index path.
No PowerShell startup, invocation, environment or ConPTY behavior is changed.

| Upstream behavior/change | Native disposition | Managed/shared disposition and evidence |
| --- | --- | --- |
| DECIC, DECDC, DECBI, DECFI (`86d97211a`) | Included in pin. | Add CSI apostrophe `}`/`~`, ESC 6/9; margin, cursor, delayed-wrap, metadata and held-publication comparisons across 24 positions per scenario. `ManagedCharacterEditParityTests.Columns`. |
| Pending wrap at right margin (`9781c0afc`) | Included in formatter. | Existing edge-glyph replay already supports margins; new `NovemberFormatterParityTests` covers narrow, wide and physical-edge-outside-margin cases with both export and replay engines. Blank-cell replay remains covered. |
| Input-only semantic prompt tracking and copied cursor state (`9bfa8ee9a`) | Prompt scans may be skipped only when prompt/input has never occurred. | Set Seen for input, cursor copy and snapshot restoration; existing processor-state differential cases cover primary/alternate transitions. |
| Apply all SGR attributes once (`2f967e3a6`) | Included. | Capture the old pen once and commit the final pen once per CSI. Update pressure tests to distinguish one sequence from separate mutations; do not allocate discarded intermediate styles. |
| 128-bit packed style and hash (`687f37acd`) | Included. | Match field packing/hash folding and 16-byte alignment; native reference-counted style items are 32 bytes. Allocation and collision differential tests use the new model. |
| Separate hyperlink set/map capacities (`298f8ecb9`) | Included. | Track unique-link bytes separately from linked-cell capacity; map growth does not enlarge the set. Preserve PAGE v1's conservative combined wire hint, including saturation behavior. |
| Capacity projection for links/strings (`431d003c4`) | Included. | All five dimensions project density with 25% headroom, dimension-specific floors, 32-times bounds and page-size ceiling. Growth/admission/reflow tests cover zero hints and limits. |
| Recent-page capacity carry (`dc8f59943`, `4747d7ff4`, `7b267cd9a`) | Included. | Per-screen three-page ring, per-row utilization scaling, 50% headroom, hyperlink-set floor, two-unused-page decay, and pool fitting at 90% row/byte efficiency. COW copies keep independent history; plain growth allocates zero bytes per call. |
| Byte limit enforcement after carried-page growth | Included. | Retire all eligible historical page components after growth, preserving active-boundary pages. Quota tests cover large pages, interleaving, line limits and subsequent streaming. |
| Dead ID reuse and Robin Hood probe repair (`b5d9c93da`) | Included. | Circular dead-ID reclamation replaces unnecessary rehashing; preserve live thresholds, probe bounds, preferred IDs, deletion ownership and COW independence. |
| Kitty numbered-image lookup and eviction by transmission (`97f422fa2`, `1fa19fa3e`) | Included. | Use transmission order instead of mutable image generation; animation/base-frame/quota tests cover the distinction. |
| Placeholders target virtual placements only (`c412a3199`) | Included. | Shared projection checks explicit targets as well as fallback targets; ordinary placements remain independently visible. Both-engine renderer tests updated. |
| Triple-click drag through blank rows (`53e55147b`) | Included in gesture API. | Shared line selection has an untrimmed fallback; headless tests drag from text onto a full blank row in both engines. |
| Clear cursor-pin garbage after resize (`76329e5ad`) | Included. | Managed row/cursor remapping does not use native pin garbage; repeated-resize, saved-cursor and continuation comparisons exercise the equivalent state. |
| Wide-cell overwrite scan (RoyalTerminal overlay) | Reject a doomed unlinked wide-cell fill before repeatedly scanning the remaining codepoints; pending wrap and linked text keep upstream behavior. Submodule remains unchanged. | Paired profiling found a ~4% upstream microbenchmark regression. The narrow overlay improves this case ~55% versus baseline; complete native snapshots match byte-fed scalar writes in `GhosttyWideOverwriteTests`. |
| Mixed-width/ASCII/hyperlink printing and right-margin fast paths (`e2a3ba004`, `77f85cd67`, `e0acea275`, `91973f2b2`, `091beb992`) | Adopt upstream optimizations directly. | Preserve managed parser and metadata ownership algorithms; Unicode/wide/margin/hyperlink differential tests check behavior. Measure native core, adapter and managed paths independently. |
| Blank-row erase/scroll and style-run scans (`59093e3aa`, `a0c092868`, `18e645ea2`, `7ea52a29c`, `6fba0873e`, `171a4e044`) | Adopt upstream optimizations directly. | Shared output/metadata/erase/scroll tests enforce semantic equivalence. Native SIMD/layout algorithms are not transplanted into CLR storage without a measured benefit. |
| tmux `%output` parser and window events (`ae0dc1b71`, `eec45a52d`) | Present in upstream application sources. | Not exposed through libghostty-vt; RoyalTerminal does not host Ghostty's separate tmux control-mode frontend. Normal tmux VT output remains handled by both engines. |
| Zig container migration and test syntax (`ada6e3d72`, `a74bd243c`) | Build with required Zig 0.17.0. | No managed terminal behavior change. |

The rest of the terminal-path commits in this range are the ABI, release,
manifest, enum and packaging changes enumerated above. The exact first-parent
inventory is reproducible with:

```sh
git -C external/ghostty log --first-parent --format='%h %s' \
  4a4fc017940bec38c93b278c10c6a18c591f3e63..5813c9461d5af78b6307a1254d00b1bc190eaccd \
  -- src/terminal
```

Parity means the covered VT behavior, host integration and public native ABI at
this pin. It does not require identical allocations or copying an upstream leak.
The existing `ManagedSnapshotCursorResizeTests` intentionally release an orphaned
cursor hyperlink that certain native resize layouts retain; content, cursor and
continuation remain compared, and the capacity divergence is explicit in that
test. Its projected native capacity is target-specific: 600 bytes on 4 KiB
layouts and 6000 bytes on macOS ARM64’s 16 KiB layout. Both are asserted exactly. Ghostty application windows, configuration UI, Raylib rendering and tmux
control-mode orchestration are not libghostty-vt features to port into Avalonia.

## Validation ledger

| Check | Result |
| --- | --- |
| Release solution | Pass, zero warnings/errors. |
| Native integration | 305 passed, zero skips, fresh macOS ARM64 library; wide-overwrite bulk/scalar snapshots compared at 360 checkpoints. |
| Bounded history project | 45 passed, zero skips. |
| Focused parity reruns | 249 printing, character/column edits, scroll, hyperlink lifecycle, reflow-refusal and formatter cases pass on the final optimized library. The target-alignment correction also passes all 18 macOS cursor-resize cases. |
| Local Linux guest | 1,364 snapshot, allocation, reflow, printing, character/column-edit, scroll and formatter parity tests pass on Ubuntu 24.04 ARM64, zero skips. Reproduced the two 4 KiB fixture failures before correcting their expected capacities. |
| Native history/metadata/target tests | Pass with Zig 0.17.0. |
| Native local builds | macOS ARM64 VT + renderer; Linux x64 and ARM64 VT cross-builds. |
| Linux consumer packaging | Shared and static C consumers link; ELF exports use `GHOSTTY_VT_1.0`, including RoyalTerminal extensions. |
| ABI | 216 types, 31 callbacks, 209 exports/signatures checked; audit mutation checks pass. |
| Complete local unit/headless suite | 8,354 passed, 23 existing platform/harness skips, zero remaining failures across 8,377 unique discovered cases. Includes the complete 113-batch repository-local run plus focused reruns of 59 corrected legacy-fixture failures. No skips were added. |
| Performance | No material regression in measured CPU workloads. Identical-harness measurements, the resolved upstream wide-overwrite regression, bounded metadata allocation costs and raw data are in the [performance report](ghostty-performance-2026-11-07.md). |
| CI | All six native OS/architecture builds passed. Windows audit encoding failure fixed with explicit UTF-8 and non-ASCII mutation fixtures. Linux/Windows resize-fixture expectations corrected for 4 KiB target alignment and verified locally. Final PR revision must pass before review. |

Reproduction uses `scripts/build-native.sh`, `scripts/audit-ghostty-abi.py`,
`scripts/test-audit-ghostty-abi.py`, `dotnet build -c Release`,
`scripts/run-unit-test-batches.sh`, and the integration/history projects.
Set `ROYALTERMINAL_REQUIRE_NATIVE_TESTS=1` for the native-backed local runs.
