# Ghostty update and OSC 7501 — 2026-10-09

## Target and acceptance

Work on `codex/ghostty-latest-osc-7501`, committing tested increments. The clean
starting point is `ad857e6f`. Advance the native dependency from
`b40acce58dcf77df52231c3798ea58e924647c89` to upstream main observed on October 9,
`246f702876b924a1cb7cade1e99274d1470302fc`. Audit every intervening first-parent
change, port relevant behavior to the managed engine and shared host, update the
public ABI, and validate native/managed behavior with focused xUnit tests.

Ghostling HEAD is still `63842bf8e5e481160f81d348da9ff6fd27986798` (August 9),
matching the existing integration reference. GitHub's commits/main response was
checked on October 9; there are no subsequent changes to port.

## Reference decisions before implementation

- Ghostty's new public `GHOSTTY_TERMINAL_OPT_PROGRAM_STATUS` callback validates
  OSC 7501 reports and answers detection queries. Its public semantic-prompt and
  reset callbacks provide the lifecycle hooks. Use these APIs directly; no
  source overlay or separate native byte-stream parser for this protocol.
- Implement shared per-terminal bounded records, ancestry/app inheritance,
  full replacement, subtree clearing, least-recently-updated eviction, prompt
  and process-exit cleanup, and RIS reset. DECSTR and alternate-screen switching
  retain records. The managed parser follows the upstream parser and the
  [protocol specification](https://www.superlogical.com/rex/docs/build/program-status),
  revision 0.3. Reports never trigger commands or echo their text back.
- Windows Terminal `src/terminal/adapter/adaptDispatch.cpp` and xterm.js
  `src/common/InputHandler.ts` were inspected from upstream. Neither implements
  OSC 7501 in those dispatchers. Their soft-reset implementations preserve screen
  content, reset rendition/margins/charset and active saved cursor state. Prefer
  Ghostty's precise DECSTR contract for cross-engine parity and test differences.
- Windows Terminal's rectangular checksum is feature-gated; the Ghostty checksum
  extension is opt-in too. Preserve default privacy behavior and match Ghostty's
  algorithm/flags when enabled, with native differential cases.
- Native parser/library fixes arrive through the pin. Review existing guarded
  extensions against changed source before refreshing hashes. Keep exact fragment
  checks and existing regressions. Do not edit the upstream submodule checkout.
- Standalone platform UI/build changes (Swift/GTK, translations, packaging and
  Ghostty CLI) require a recorded applicability decision; they are not evidence
  of a new VT feature. Shared-host behavior changes must be evaluated separately.
- PowerShell `4c462e3d39478277bb8e89a7e01783efa375f25d`,
  `ConsoleHostUserInterfaceProgress.cs`, writes OSC 9;4 progress only with VT,
  nonredirected stdout and `UseOSCIndicator`; completion writes OSC 9;4;0.
  Preserve that independent progress channel rather than replacing OSC 7501
  records. No heuristic matching of PowerShell prompts or screen text is added.

## Delivery checklist

- [x] Refresh native pin, review overlays, build both native libraries (macOS arm64).
- [x] Bind new public C types/options/callbacks/functions; regenerate ABI audit.
- [x] OSC 7501 parser, bounded record store, native callbacks, lifecycle and host UI.
- [x] Unknown OSC capture, terminators, cancellation and numeric validation.
- [x] DECSTR and RIS palette behavior; charset single shift and Unicode handling.
- [x] DECRQCRA/XTCHECKSUM and explicit host checksum policy.
- [x] Mouse shape, UTF-8 mouse buttons, modified backspace and prompt-click fixes.
- [x] Resize/reflow, saved cursor, line-selection and Kitty placeholder regressions.
- [x] Snapshot decode compression and memory queries, with managed equivalents.
- [x] Evaluate renderer/shader, font, image/zlib, tmux and other upstream deltas.
- [x] Run ABI, unit, native integration, history and headless/build checks.
- [x] Document every delivered behavior and any concrete remaining limitation.

## Complete upstream delta audit

All 74 first-parent changes between the recorded pins are accounted for below.
“Native pin” means the clean upstream implementation is compiled into libghostty-vt;
managed or shared-host equivalents are stated separately. Standalone-app changes
are recorded explicitly and are not represented as new RoyalTerminal features.

The input comparison also inspected [Windows Terminal mouseInput.cpp](https://github.com/microsoft/terminal/blob/main/src/terminal/input/mouseInput.cpp)
and [xterm.js MouseStateService.ts](https://github.com/xtermjs/xterm.js/blob/master/src/common/services/MouseStateService.ts).
xterm.js uses the same extended-button bits but removed UTF-8/urxvt encodings;
Ghostty is the compatibility target for those encodings and X10's three-button limit.

| Upstream commits | Change | Native / managed / host disposition |
| --- | --- | --- |
| `b115e4567` | DECSTR | Native pin; managed reset contract and focused state-retention tests. |
| `7ec9e26a2` | Formatter origin coordinates | Both exporters restore cursor relative to margins only when margins and modes are exported; ordinary, horizontal-margin and pending-wrap replay tests. |
| `b699ea79f`, `4611e04ff` | Single shift and Unicode charsets | Managed scalar dispatch consumes single shift once and bypasses legacy mapping above FF; native differential and grapheme-transfer tests. |
| `a4aacd918` | OSC 7501 | Official native callbacks, full managed protocol parser, shared bounded store, lifecycle and status UI. |
| `ca2356fa0` | Prompt-bounded line selection | Shared selector already limits trimming to the semantic range; added native/managed blank-cell regression. |
| `b094a6ba3` | UTF-8 mouse button bytes | Shared encoder and native pin; extended back/forward buttons wired through adapters and Avalonia, with encoding and headless tests. |
| `5dc28bb8e` | Wide cell cut during resize | Managed resize clears split heads/tails and allocation metadata; both engines covered. |
| `0e0ff282f` | Initialize PNG callback output | Native pin; managed decoder initializes its output and uses the shared Skia decoder. PNG regression coverage. |
| `04d685c6a` | DECRQCRA and XTCHECKSUM | Native bindings and managed algorithm/flags; explicit shared opt-in policy and all-flag native differentials. |
| `eee709f5d` | Ctrl+Alt+Shift+Backspace | Both input paths emit the upstream legacy sequence; modifyOtherKeys coverage retained. |
| `3425025e5` | Allocator alignment documentation | C# vtable documentation identifies log2 alignment; allocator-failure paste test exercises callbacks. |
| `33da6848d` | OSC 105 color reset routing | Recognized by both parsers; unsupported special-color reset is a no-op, matching native. |
| `8628db1a6`, `54bada35d` | Compressed snapshot history and memory queries | Native APIs and managed lossless row compression, lazy restoration, COW ownership and documented backend-specific memory estimates. |
| `0081d4530`, `364f8472a` | Requested pointer shape and empty OSC 22 | Public native getter and shared pointer path; empty OSC 22 restores text shape in both engines. |
| `a806905ea` | Reject paste after a refused writer call | Native pin plus failing-allocator/contract-violating-reader regression. Managed paste owns its complete encoded bytes before dispatch; it has no native MIME sink trampoline. |
| `76895d97b` | Same-size resize documentation | Native already updates cell pixels while preserving margins; managed NotifyResize corrected and both pixel/margin contracts tested. |
| `daff2d8e1` | Null OSC command access test | Official accessor returns false; integration regression included. |
| `acf1209ee` | RIS palette reset | Native pin and managed override reset; theme/default-color tests. |
| `dc3f73a69`, `26e64dfb4` | Saved cursor and live pending wrap after resize | Managed pin mapping follows deferred hard breaks and wide/pending-wrap boundaries before clamping; repeated resize and anchor tests. |
| `36ec90a07` | Semantic prompt and reset effects | Public native callbacks and managed equivalents; owned command/error bytes, ordered reset and status lifecycle tests. |
| `cc2f7ee4d` | Prompt-relative click coordinates | Shared click capability reads public native grid/prompt APIs or managed row markers; history/page/viewport and passive-control interaction tests. |
| `75e7a180d` | Shader selection color layout | Shared Skia shader uniforms are bound by name; added missing cursor-text and selection foreground/background uniforms and distinct-color pixel tests. |
| `f9e827093` | Windows DLL global constructors | Arrives through native pin/build; no managed static initialization analogue. Windows CI builds the updated DLL. |
| `0538f7535` | CAN/SUB cancel OSC | Both protocol parsers and the existing native shell observer cancel partial commands; all-split regressions also cover C0 and ESC termination. |
| `3a3047f6b` | Unknown OSC callback | Native API and managed bounded capture through the optional host callback, owned bytes, terminators, frozen limits and cancellation tests. |
| `02a3a049c` | Clear Kitty placeholder row flag | Native pin; managed projections already derive from current cells. Full EL/ED clear and image-retention regressions in both engines. |
| `f04a00c07` | Strict OSC integers | Native pin; managed unsigned color/progress parsers hardened. Shared notification expiry and OSC 133 signed exit codes already reject separators. OSC 66/3008 are recognized no-ops in both terminal streams; their raw native parser improvements arrive through the pin. |
| `d48c0372f` | Wuffs zlib decoder | Native uses upstream Wuffs; managed already uses bounded .NET ZLibStream. New upstream compressed-PNG fixture decodes to identical RGBA in both engines. |
| `c065cd3cd` | Search tick after terminal free | Native pin and wrapper lifetime documentation; integration test checks InvalidValue after free. Managed search owns its captured rows and does not dereference freed native state. |
| `35a81a980` | Release GPU shaders | Standalone renderer fix is in the pin. Shared TerminalShaderPostProcessor already disposes every realized effect independently and idempotently; no equivalent delayed GPU release path. |
| `ed350cbb4` | Share GPU render-device state | Standalone Metal/OpenGL renderer internals are not linked by the renderer bridge. Shared Skia owns device/context resources; reproducing Ghostty device objects would not affect either VT backend. |
| `b1d2b7ef1` | Three-level Nerd Font constraints table | Same constraints in a smaller upstream lookup representation, not new glyph behavior. Shared Skia/HarfBuzz does not consume this Zig table; existing glyph constraint/rendering tests remain applicable. |
| `4406075a0` | CoreText BMP UTF-16 width | Standalone Ghostty shaper fix is in the pin. RoyalTerminal CoreText fallback already uses Rune.EncodeToUtf16 (one BMP unit, two supplementary units); no private shaper port required. |
| `2fb0c9cac`, `a85cd2e16` | tmux control parsing and pane mouse flags | Upstream control-mode fixes are in the pin. RoyalTerminal has no tmux control-mode viewer/session manager; ordinary tmux VT/mouse modes use the shared terminal path and its mouse-mode tests. |
| `246f70287`, `c770410db` | Theme browser paging/keypad key | Ghostty CLI-only key bindings. RoyalTerminal does not invoke this TUI; no terminal encoder change beyond the separately ported input fixes. |
| `befcdfd2c` | ssh-terminfo cache includes port | Ghostty CLI cache; RoyalTerminal transports do not run that command or share its cache. |
| `ac2ae69e0` | iTerm2 theme package refresh | Standalone Ghostty resource dependency; RoyalTerminal has its own theme catalog and does not import that package. No new palette protocol or renderer behavior. |
| `f523504ea`, `bcfae289c`, `6467b1dab`, `068e15c2d` | macOS restoration, Shortcuts, tab appearance/compiler check | Swift standalone app changes; RoyalTerminal uses Avalonia window/docking composition, not these NSWindow controllers or App Intents. |
| `62fa23bfa` | Kitty clipboard prompt program name | Standalone macOS prompt presentation. RoyalTerminal clipboard prompts do not display producer-supplied program names as trusted application identity. |
| `4ddf1f79d`, `59c2dc032` | GTK DPI and EGL failure diagnostics | Standalone platform diagnostics; neither GTK monitor detection nor Ghostty EGL surfaceless display creation is used by the shared Avalonia/Skia host. |
| `ce63fcba6`, `8f0dd3709` | Danish translations | Ghostty standalone localization assets; no shared terminal behavior or corresponding RoyalTerminal resource files. |
| `da1afb561`, `d1eb46289` | esctest reporting and zon2nix | Upstream CI/package tooling. RoyalTerminal keeps its xUnit/native differential suites and existing cross-platform Zig build pipeline. |
| `9d479dcb1`, `bad5854f3`, `0f171f650`, `7551c5bad`, `a60e9e2a5`, `34f39002c`, `2febd0116`, `c3203ea4b`, `f96c9711b`, `822e84272`, `f5a7f706f`, `83edd491e`, `d5427745d`, `1dc485f10`, `4da7523fa`, `b0174d03f`, `5b435d548`, `d67ab3213`, `6f51f41d0`, `12752b2ac` | VOUCHED list updates | Upstream contributor metadata only. |

## Validation log

Initial environment: .NET SDK 10.0.201; Zig 0.16.0. Native runtime host: macOS.
Both native libraries rebuilt successfully. The six changed overlay inputs retain
all exact fragment matches; none of the upstream changes replaces the separate
dirty-row, hyperlink ownership, clone rollback or notification/resize hooks.
Reviewed changes add checksum/reset/parser effects, correct charset printing,
reflow pins/wide-cell cuts and add memory/compression APIs. The three other
overlay input files are byte-for-byte unchanged. All nine full-file hash guards
and fragment-count checks remain enabled.

The native ABI now contains 172 types and 30 callbacks. Source audit and generated
ABI checks pass. Release integration build: zero warnings/errors. Focused native
API and ABI suite: 152 passed, zero skipped. Cases cover OSC 7501 opt-in queries,
BEL/ST replies, validated report fields, prompt/reset callbacks, unknown OSC
capture/cancellation, null command access, memory queries, incremental compressed
snapshot restoration, pointer reset and opt-in rectangular checksums.

Full required-native integration suite: 274 passed, zero skipped. Initial OSC
7501 cross-engine suite: 12 passed, including every input split, hierarchy,
replacement, eviction, prompt/exit/reset lifetimes and malformed-report limits.
The adjacent regression suite found two cursor reflow differences from the
new upstream pending-wrap correction; these belong to the resize parity work.

OSC 7501 plus presentation/headless suite: 18 passed, zero skipped. Both engines
keep 256 records, inherit application names dynamically, preserve done/error/idle
on prompt and process exit, retain all records on DECSTR/screen switches, and
clear on RIS/new sessions. Native semantic prompt-start events (A/N/P) and their
managed equivalents drive transient cleanup. OSC 9;4 remains independent.
TerminalControl coalesces immutable snapshots onto the UI thread, handles real
transport exit/stop, and discards old processor state on replacement. The app's
compiled-XAML status bar displays the most urgent record and all records in a
tooltip, labels the originating terminal, and escapes invisible direction text.

Reset/charset regression batch: 440 passed. Follow-up reset, pointer and input
tracker batch: 87 passed. DECSTR now resets only Ghostty's defined mode subset,
active saved cursor, pen, protection, charset, modifyOtherKeys and palette;
it preserves dynamic colors, text/cursor, pending wrap, tabs, links, keyboard
stacks and other modes. RIS also restores palette overrides. Single shift is
consumed by exactly one input scalar, including combining characters; internal
spacers are raw writes and non-8-bit Unicode bypasses legacy charset mapping.
Live pending-wrap cursors advance after resize when no longer at the right edge.
Empty OSC 22 restores the text pointer in both engines.

Protocol/input/lifecycle batch: 539 passed, zero skipped. Unknown OSC selectors
have bounded, owned byte payloads, captured BEL/ST terminators and fixed limits
per command; supported-but-malformed payloads and CAN/SUB never reach that
callback. OSC 105 is recognized without changing unsupported special colors.
Color/progress integer fields reject signs on unsigned fields, separators and
whitespace. Ctrl+Alt+Shift+Backspace matches the new native legacy encoding.
DECRQCRA/XTCHECKSUM uses explicit opt-in and preserves host defaults across
RIS/DECSTR; every flag combination was compared against native using Unicode,
combining marks, attributes, margins, empty rectangles and held output.
Public lifecycle callbacks expose owned OSC 133 command/error bytes, prompt
roles and exit codes, plus ordered protocol-only RIS effects. Native uses the
new official callbacks. Managed decoding matches first-option/quoting/percent
decoding rules and the 2048-byte capture, including malformed and split input.
The shared line selector already bounds whitespace trimming to the semantic
range; the new upstream unwritten-gap regression passes in both engines.

October input/resize batch: 241 passed, zero skipped, including six new headless
cases. Saved pins account for deferred hard breaks and pending wrap before
clamping; shrinking without reflow clears both halves of a split wide glyph
and their snapshot metadata. Same-size resize retains margins and updates pixel
reports. OSC 133 click_events=1/2 now reach both engines through a focused host
capability; prompt-relative rows use absolute coordinates across page boundaries
and scrolled viewports. Native reads public cursor/grid APIs. The control excludes
modified clicks, drags, selections and application mouse reporting; hosts can
disable cursor-click-to-move. Extended back/forward buttons flow through Avalonia,
both adapters and every mouse encoding, including UTF-8 button bytes; X10 remains
limited to the three standard buttons. Native exposes button IDs 10/11 but its
encoder does not emit them, so the shared host does not claim support for them.

Compression/storage regression suite: 2,097 cases, with one anchor expectation
updated for the upstream saved-pin correction. Focused completion suite: 78
passed, including compression, COW, incremental admission, reflow/export,
same-screen memory queries, shader pixels, origin-relative formatter replay,
compressed PNGs, Kitty placeholder erasure and shell-observer cancellation.
A warmed local 1,201-row fixture used 4,616,640 bytes of plain row payload versus
304,091 resident bytes with compression (211,839 compressed bytes), about 93%
less. Restore took 26.86 ms plain versus 62.21 ms compressed in that sample.
These are diagnostic payload/timing measurements, not heap-size or general
performance guarantees. Compression is opt-in and does not change history quotas.

Release solution build: zero warnings/errors. Final native integration suite:
276 passed, zero skipped, including search-after-free rejection and a paste
reader that incorrectly reports success after allocation refuses its write.
History contract suite: 45 passed, zero skipped. Native `test-history` passed;
all 172 ABI types, 30 callbacks and history exports validated. CI also built
both native libraries for macOS/Linux/Windows, each x64 and arm64, at `29d4f383`
(the native source/pin is unchanged by subsequent managed completion commits).

Full unit/headless suite completed in 16 isolated batches: 7,889 passed,
16 pre-existing skips, zero failures (7,905 distinct executed/discovered runtime
test IDs; some theories expand beyond list-tests output). The existing exclusions
cover ncurses/mouse PTY harness cleanup, macOS telnet execution and JSON atomic
file-store roundtrips. None was added by this update. Total passing .NET cases
across unit/headless, native integration and history suites: 8,210. Generated
X11 colors and whitespace checks also pass. Final cross-platform CI remains a
separate PR check; local runtime validation is macOS arm64.
