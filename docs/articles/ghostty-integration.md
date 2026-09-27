---
title: Ghostty Integration
---

# Ghostty Integration

RoyalTerminal uses Ghostty in two different ways: as a native VT engine with renderer interoperability, and as a public wrapper library for hosts that want direct access to the Ghostty C ABI from .NET. The second role is what `RoyalTerminal.GhosttySharp` exists for.

Both VT engines use RoyalTerminal's shared Avalonia/Skia host. The renderer bridge
does not embed Ghostty's complete Metal/OpenGL terminal renderer, and the managed
VT engine does not claim exhaustive native or standalone-application parity.
The [generated ABI inventory](../specs/ghostty-abi-inventory-2026.md) documents
the pinned native type and callback bindings; regenerate it with
`scripts/audit-ghostty-abi.py` after a dependency update.

## Untrusted hyperlink dispatch

Both engines now route producer-supplied OSC 8 links through a shared safety
boundary before the operating-system launcher, following
[Ghostty #13634](https://github.com/ghostty-org/ghostty/pull/13634) and its pinned
[`UntrustedURL` policy](https://github.com/ghostty-org/ghostty/blob/622b4eecd7d2ce1a10930537c17f0d61abdba817/macos/Sources/Helpers/UntrustedURL.swift).
Well-formed HTTP/HTTPS and nonempty mailto targets retain direct opening.
Malformed, relative, remote-file and unsafe-Unicode targets cannot dispatch.
Unsafe scalars are escaped visibly without normalizing web/custom target
spellings; ordinary previews reuse the original string. The explicit
`EnableOsc8Hyperlinks` switch defaults to true and does not disable plain-text
web-link recognition. Hover lookup reads cells without acquiring mutable row
storage. Raw link identity remains available separately from the safe preview.

The reusable control exposes `ITerminalHyperlinkHost` for non-direct requests;
without a host they fail closed. Requests are bounded to one pending operation
per control and canceled on host replacement, OSC 8 disable, detach or session
stop. Host failure/cancellation never enables an unrestricted fallback.
The application shell wires a passive compiled-XAML dialog and framework-free
ReactiveUI view model. Custom schemes require explicit confirmation showing the
escaped target and the registered application: a Launch Services bundle ID on
macOS, or an application display name from GIO/Windows associations. Association
lookup runs off the UI thread and never invokes a shell. Unknown/unavailable
handlers cannot be confirmed. Cancel is the default action. Blocked targets
remain copyable only by an explicit user action; copying preserves the original
target, not its escaped display spelling. Cancellation is checked again after
association lookup and after confirmation, before launching.

Local-file opening is **not yet complete**: the classifier produces an
`InspectFile` request, and the current application host displays it as blocked.
Canonical/symlink-resolved previews, regular-file/directory inspection,
executable/content-type checks and safe-file dispatch still need their platform
implementation. This is deliberately stricter than Ghostty's safe-file path,
not a claim of complete #13634 parity. The other dialog and native association
bindings are also awaiting the final cross-platform validation pass.

For comparison,
[Windows Terminal](https://github.com/microsoft/terminal/blob/main/src/cascadia/TerminalApp/TerminalPage.cpp)
filters supported/safe URIs before confirmation, and
[xterm.js](https://github.com/xtermjs/xterm.js/blob/master/src/browser/OscLinkProvider.ts)
defaults to web schemes unless an embedder supplies a link handler. RoyalTerminal
follows Ghostty's scheme/Unicode policy with an application-owned decision UI,
rather than treating every absolute URI as safe. Focused classifier, host,
view-model, headless-dialog and both-engine click cases are authored; they have
not yet been executed.

## Font discovery and startup

The shared Skia renderer follows Ghostty's
[known-family emoji lookup](https://github.com/ghostty-org/ghostty/commit/afc79b8ccf4098ba15659578d0fc666c74fb61bd)
and [background font warmup](https://github.com/ghostty-org/ghostty/commit/c454a3bf47cd72945b7f4db3b53f8af332e167c9).
On macOS each resolver lazily matches `Apple Color Emoji` once, in regular style,
through its own Skia font manager. It verifies the returned family and individual
glyph coverage before using the candidate. Missing/substituted families and
unsupported glyphs retain general character discovery; text presentation and
other platforms retain their existing paths. Whole-grapheme coverage and lazy
component candidates still apply. This is a named Skia lookup, not an additional
native CoreText-to-Skia bridge or a copy of Ghostty's complete font collection.
Both VT engines use this renderer behavior.

Font selection also follows the pinned Ghostty
[configured-face presentation rules](https://github.com/ghostty-org/ghostty/blob/622b4eecd7d2ce1a10930537c17f0d61abdba817/src/font/Collection.zig)
and [adjacent-selector and cluster rules](https://github.com/ghostty-org/ghostty/blob/622b4eecd7d2ce1a10930537c17f0d61abdba817/src/font/shaper/run.zig).
Without an adjacent VS15/VS16, a configured primary face keeps its glyph even
when it is a deliberately monochrome emoji. Fallback discovery uses the exact
Unicode 18 `Emoji_Presentation` property from Ghostty's pinned UCD, not a Unicode
block heuristic. The separate generated range table adds 640 bytes without
widening the packed width/grapheme lookup table. Only a selector immediately
after the base changes its requested presentation; later selectors, keycaps,
modifiers and tags cannot override it. Component discovery uses each component's
own default, while whole-cluster validation requires the base's explicit
presentation and permits either presentation for other components.

Color coverage uses the existing pinned HarfBuzz library's public COLR v0/v1
and bitmap glyph APIs. CoreText's `sbix`-face rule and macOS SVG support are
retained; SVG is not enabled for the FreeType path. Glyph results are cached per
resolver, and rejected candidates do not dispose fonts retained by another
cache entry. Where Skia exposes a mapped font stream, a HarfBuzz blob borrows it
with an owned release callback, avoiding a managed copy of large bitmap tables.
This checks available color data, not an interactive guarantee for every font
format/backend.

`TerminalTypefaceCollection` supplies ordered faces for regular, bold, italic
and bold-italic logical styles. The shared renderer and glyph-coverage source
use these collections, including ordinary family/file configuration. Hosts may
set `TerminalControl.TypefaceCollection` or use the renderer/coverage
`CreateWithTypefaces` factories to provide multiple faces per style. Collections
copy entries but borrow typefaces: callers keep every face alive for the lifetime
of all consuming renderers/resolvers, including retained presentation resources.
Passing null to the control restores family/file configuration.

Following Ghostty's
[CodepointResolver](https://github.com/ghostty-org/ghostty/blob/622b4eecd7d2ce1a10930537c17f0d61abdba817/src/font/CodepointResolver.zig),
configured style faces are searched in order, then regular configured and loaded
fallback faces, before regular-style discovery. Discovered faces are reused for
later scalars, but must match explicit or Unicode-default presentation; configured
faces accept any presentation by default. Final ANY lookup only considers loaded
regular faces. Like
[SharedGrid](https://github.com/ghostty-org/ghostty/blob/622b4eecd7d2ce1a10930537c17f0d61abdba817/src/font/SharedGrid.zig),
both scalar hits and misses remain cached even as the collection grows. Cache
keys additionally preserve the managed API's culture argument. New configuration
creates a new collection/resolver; the bounded glyph-coverage cache resets its
resolver on eviction. Cluster selection, cursor, IME and password rendering use
the same collection policy. Resolver-owned discovery faces are released once;
configured faces are never disposed by borrowed-collection consumers.

Persisted `FontFamilies` settings supply four immutable ordered lists (regular,
bold, italic, bold italic). The settings editor accepts one family per line and
keeps comma-containing names intact. Profiles, launch configuration, runtime
settings and split panes preserve the lists. Existing profiles and empty lists
keep their single family/file behavior. A non-empty regular list overrides that
primary selection; unavailable entries are skipped, and an entirely unavailable
list restores the legacy primary. Missing/empty styled lists use variants of
the loaded regular families, then regular-face fallback. Caller-supplied
`TypefaceCollection` still takes precedence over persisted family settings.

This follows Ghostty's
[repeatable per-style family configuration](https://github.com/ghostty-org/ghostty/blob/622b4eecd7d2ce1a10930537c17f0d61abdba817/src/config/Config.zig),
not the implicit DirectWrite/canvas family fallback of Windows Terminal/xterm.js.
Skia family style sets distinguish unavailable families from implicit system
substitution without enumerating or reordering the system fallback registry.
Configured face references are owned by each renderer/coverage source and
disposed after their consumers; partial loading failures dispose all acquired
references. Skia protects shared system faces from public disposal and may keep
their native resources cached; this does not promise immediate OS-font reclamation.
Profile JSON uses generated metadata for the new immutable family-list model.
The existing profile reader is retained so omitted fields in older documents
keep their property-initializer defaults.

`FontFamilies.CodepointMaps` adds repeatable `U+XXXX[-U+YYYY][,...]=family`
overrides, shared by both engines. Mappings preserve order, with the last matching
range winning even if its family is unavailable. The resolver lazily requests
the exact regular family and caches both found and missing descriptors; a
missing glyph falls through to normal selection. Successfully loaded mapping
faces join the regular collection in discovery order with configured-face
presentation rules. No font is loaded simply because a mapping was configured.

This follows Ghostty's
[CodepointResolver](https://github.com/ghostty-org/ghostty/blob/622b4eecd7d2ce1a10930537c17f0d61abdba817/src/font/CodepointResolver.zig)
and [last-range-wins map](https://github.com/ghostty-org/ghostty/blob/622b4eecd7d2ce1a10930537c17f0d61abdba817/src/font/CodepointMap.zig):
an available scalar override precedes style/presentation and built-in symbol
drawing. Whole-grapheme selection still verifies the base's explicit presentation
and every substantive component, as Ghostty's run iterator does. Text, cursor,
IME and coverage share the mapping policy without changing stored terminal text.
Windows Terminal's DirectWrite mapping and xterm.js's canvas/custom-glyph switch
do not define this explicit range-override policy.

Mappings use the same profile, settings and split-pane propagation as family
lists. Invalid editor syntax displays a line-specific error and blocks Apply and
Save; the last valid mappings remain in the profile if the user switches profiles.
Programmatic malformed mappings throw `FormatException` during normalization.
Configuration accepts Ghostty's 21-bit range bounds, while non-Unicode-scalar
values cannot match rendered text. Updating host configuration rebuilds the
renderer and its caches immediately; unlike standalone Ghostty's new-terminal-only
reload behavior, this preserves RoyalTerminal's live settings contract.

`RegularStyle`, `BoldStyle`, `ItalicStyle` and `BoldItalicStyle` select advertised
face names within configured system families. Empty/`default` uses automatic
selection; `false` independently disables bold, italic or bold italic. Disabling
one component does not disable the combined style. Regular cannot be disabled.
Names apply to explicit families, their inherited regular-family variants and
the existing primary-system-family setting, not a primary font file. Missing names continue normal
family fallback and missing-style completion; they are not silently replaced by a nearest weight.
The Skia adapter compares advertised names case-insensitively, retaining the
caller spelling in profiles. The ReactiveUI editor, profile serialization,
live settings and split panes carry these controls. Disabled styles route to
regular before font discovery without changing SGR attributes, colors or stored
text. Borrowed collections expose the same immutable policy and retain mappings
when copied. This follows Ghostty's `Config.finalize` family inheritance,
`SharedGridSet` named descriptors and `CodepointResolver` disabled-style rule;
Windows Terminal and xterm.js instead delegate logical weight/slant selection
to DirectWrite and canvas respectively.

`SyntheticBold`, `SyntheticItalic` and `SyntheticBoldItalic` default to enabled.
They follow Ghostty's
[missing-style completion](https://github.com/ghostty-org/ghostty/blob/622b4eecd7d2ce1a10930537c17f0d61abdba817/src/font/Collection.zig):
real configured styles win, including deliberately named faces whose intrinsic
weight/slant differs from the requested category. Automatic family lookup rejects
a nearest regular face when a real styled face was requested. Missing italic and
bold are synthesized from the first regular face with non-color text; missing
combined style first italicizes an existing real bold face, otherwise emboldens
the completed italic face. Disabling a synthesis toggle aliases regular outlines
for that missing style; it does not disable an available real face. The combined
toggle is independent, but its resulting outlines still depend on the chosen base
face. `false` named-style disables take precedence over these toggles.

Synthesis is carried in the resolved font, not inferred from the requested style
or native face handle. Mapped glyphs and discovered/regular fallback faces do not
inherit synthetic effects. Run boundaries, ASCII/general resolution caches,
batched rows, shaped/Pretext/cell blobs, paths, cursor text and IME/caret drawing
retain this distinction. No font bytes are cloned and no extra discovery is
performed for warm synthetic lookups. Pretext's direct fallback also reuses the
font cache instead of constructing a native font for each draw.

The shared Skia path uses native emboldening and a 15-degree italic shear without
changing HarfBuzz glyph identity or terminal cell metrics. The macOS smoothing
path uses a matching CoreText font transform plus Ghostty's size-dependent bold
stroke; transformed bounds and stroke padding participate in bounded glyph-mask
caches. Global rasterizer emboldening remains separate and is combined without
double-applying the effect. Skia rasterization is not claimed pixel-identical to
Ghostty's FreeType/CoreText backends. A caller-supplied emoji-only collection stays
usable without synthetic text, rather than rejecting the whole configuration when
Ghostty's text-base completion reports `DefaultUnavailable`; a focused test records
this deliberate borrowed-collection compatibility choice. Profiles, live settings,
compiled controls and split panes persist all three toggles. Regression tests are
written; execution is deferred to the final validation stage.

Ordinary printable ASCII uses lazy 95-entry tables per used style for the first
lookup culture. Other cultures, explicit presentation selectors and Unicode
retain the general result dictionary. Both hits and misses remain sticky;
disabled styles share the regular table. This reduces common-path hashing and
dictionary storage without loading fonts eagerly. Symbol override decisions
also retain positive/negative scalar results rather than repeating native glyph
coverage on every draw. Configuration validation no longer constructs parsed
range arrays or family strings when callers only need validity. The
`--font-lookup` benchmark compares warmed direct-table and general-cache queries
with the same ASCII/style/face workload. **Execution and before/after performance
validation are deferred until the implementation batch is complete; no measured
speedup is claimed yet.**

Fallback discovery now continues beyond a rejected first platform match, as
Ghostty's [candidate loop](https://github.com/ghostty-org/ghostty/blob/622b4eecd7d2ce1a10930537c17f0d61abdba817/src/font/CodepointResolver.zig)
does when character coverage has the wrong color/text presentation. The existing
Skia family/global first match and macOS emoji fast path remain first, preserving
successful platform/locale choices (including CJK). Only an unresolved request
opens the additional candidate stream:

- Linux uses Fontconfig's active configuration, charset/language substitution,
  monospace preference and **untrimmed** sorted results. Equal cmap coverage must
  not eliminate alternate presentation. Render-prepared file/collection indexes
  retain native ordering and sysroot handling; fonts load only as consumed.
- macOS queries codepoint-constrained CoreText descriptors, removes the temporary
  charset restriction, and ranks monospace, exact style, slant, weight, fuzzy style
  and glyph count. `head`/`OS/2` traits and variation values refine symbolic traits.
  Variation axes use stable OpenType identifiers rather than localized display
  names. Exact PostScript names map to Skia family entries or their original font
  file/collection face; this avoids silently substituting another family.
- Windows examines system and per-user font directories, including all TTC/OTC
  collection faces, alongside application-registered fonts. Inaccessible, missing
  or invalid files are skipped without suppressing later candidates.
- The complete Skia family/style registry is also available after native candidates,
  covering manager-private registrations and systems without optional native APIs.

This ports exhaustive candidate coverage, not identical font choice to standalone
Ghostty: successful Skia first choices retain the shared renderer's existing host
contract. Windows Terminal obtains its primary fallback from DirectWrite and
xterm.js from browser/canvas. No process-global font registry is mutated here.
Native query resources close before faces are transferred; early iterator disposal
does not dispose a selected face. Even source-internal discarded probes use the
resolver's ownership guard. Configured, mapped and already-retained faces remain
protected. A reference-identity retention set replaces repeated scans of every
cached scalar/descriptor during rejection. Positive and negative scalar caching,
regular-only collection discovery and loaded-face reuse keep the expanded search
off warm rendering paths. More complete cold misses necessarily inspect more
fonts; there is no claimed overall discovery speedup.

The `--font-candidates` workload measures fresh mixed-presentation/unsupported
queries separately from warmed hits/misses. Candidate iteration, ownership,
failure cleanup, ranking/trait parsing, native descriptor smoke and collection
header tests are written. **Execution, native platform sign-off and before/after
performance measurement remain deferred to final validation.** The separate font
discovery benchmark compares single-face discovery with loaded-collection reuse;
neither isolated workload proves end-to-end rendering performance.

Unsupported cells now carry an explicit `TerminalFontResolution.ReplacementCodepoint`:
the entire cluster becomes U+FFFD, falling back to a space if that glyph is
unavailable. Like Ghostty's run iterator, absence of even a space is a font
configuration error. The final configured-face lookup permits any presentation
after presentation-specific discovery fails. Render-only copies preserve the
original width and metadata while stripping suffix VS15/VS16 after font
selection; ZWJ and other shaping controls remain. Shaped text, optional Pretext,
cell-anchored fallback, thickening, IME preview/caret and block-cursor text all
consume this same projection, so text hashes and cluster/grid offsets agree.
Stored cells, copy/search text and snapshots retain the original characters.
Selector-normalization caching is bounded by both entry count and retained UTF-16
characters; warmed projection reuses its buffers. Generic LastResort range
placeholders are rejected during discovery. Glyph-coverage queries do not
advertise replacement glyphs as coverage, and the password indicator retains its
geometric fallback. Windows Terminal's DirectWrite mapping and xterm.js's canvas
delegate fallback to their platform font stacks; this shared Skia layer follows
Ghostty's explicit replacement/selector policy instead.

`TerminalFontWarmup.StartAsync()` is a best-effort, one-shot macOS startup query.
The demo starts it before Avalonia initialization, after rejecting inert toast
activation. Embedders can call it at the same point in their composition root.
The returned task owns its temporary font-manager resources, never touches UI
or renderer objects, and reports unsupported platforms/query failure as false.
Rendering does not await it; ordinary font discovery remains available if it
fails or has not finished. No process-global task or renderer-owned cache is
introduced. GPU warmup is separate and is not claimed here.

[Windows Terminal](https://github.com/microsoft/terminal/blob/main/src/renderer/atlas/AtlasEngine.cpp)
initializes DirectWrite fallback in its renderer, while
[xterm.js](https://github.com/xtermjs/xterm.js/blob/master/addons/addon-webgl/src/TextureAtlas.ts)
warms ASCII atlas entries through idle callbacks. Neither defines macOS font
registry behavior; this port follows Ghostty while retaining Skia's public APIs
and ownership rules. Windows Terminal passes the configured family and full
text to DirectWrite; xterm.js delegates presentation and fallback to its canvas
font stack. RoyalTerminal instead uses the explicit Ghostty policy above, with
Skia for host discovery and HarfBuzz for color-table coverage.
Tests use deterministic font fixtures for family mismatch,
coverage, primary ownership, negative caching and whole-grapheme fallback.
`--font-discovery` measures fresh-resolver emoji discovery; adding
`--warm-font-registry` reports background query time separately from subsequent
foreground font initialization. These are isolated font timings, not a
first-window or end-to-end rendering speedup claim.

## Managed color publication

Ghostty's [shared dynamic-palette defaults](https://github.com/ghostty-org/ghostty/commit/d1cd56a56c4244d9d9fadae028cbffc23a1d3d1a)
avoid duplicating configured defaults per terminal. Managed color state already
shares its immutable configured theme; it now also caches its latest effective
theme and palette independently. Repeated identical OSC overrides and resets of
absent overrides reuse the render view. Foreground/background/cursor changes
retain the existing materialized palette. Actual palette changes, a different
configured palette and snapshot installation invalidate the appropriate caches.
Each owner retains at most one effective theme/palette, not a history of colors.

Equal-to-default application overrides still acquire and retain their explicit
mask, survive configuration changes and reset to the newest configured default.
Snapshot nullable defaults remain protocol state rather than host render fallbacks.
Published themes/palettes and COW row readers remain immutable. Palette batches
copy each backing array once and apply both configured and application explicit
flags directly; `WithColor` also avoids its former redundant defensive recopy.
Public constructors and exported arrays still make defensive copies. Screen
theme revision, invalidation and recoloring behavior is unchanged.

[Windows Terminal RenderSettings](https://github.com/microsoft/terminal/blob/main/src/renderer/base/RenderSettings.cpp)
and [xterm.js ThemeService](https://github.com/xtermjs/xterm.js/blob/master/src/browser/services/ThemeService.ts)
also distinguish current colors from reset defaults. Ghostty defines the managed
override-mask/configuration contract; the immutable caching is a CLR-specific
equivalent, not a transplant of native palette storage. Eighteen new cases cover
sharing, repeat allocations, dynamic changes, equal defaults, configuration,
reset, invalid batches, defensive ownership, snapshot installation and held/COW
publication. These tests and `--managed-colors` are authored but unrun. Its six
scenarios separate repeated OSC colors, cursor changes with an overridden palette,
palette changes/resets and immutable palette copies. Allocation and timing
comparisons with the previous implementation remain deferred to final validation;
no measured speedup is claimed.

## Managed mode-state storage

The managed engine's 28 extended DEC mode values now occupy one 32-bit field
instead of a per-processor `HashSet<int>`. Known print/input/render checks use
constant flags directly; queries and setters use a fixed number-to-flag switch
instead of a linear whitelist scan followed by hashing. Supported mode numbers
are exposed internally as an immutable span. Creation, reset, updates and reads
need no mode-container heap allocation or capacity growth, including the mode
commit after synchronized-output staging and raw snapshot-mode installation.

This follows [Ghostty's fixed mode lookup](https://github.com/ghostty-org/ghostty/pull/14316)
and packed mode state, and applies the fixed-heap reduction principle from
[Ghostty #14138](https://github.com/ghostty-org/ghostty/pull/14138).
[Windows Terminal's dispatch mode enumset](https://github.com/microsoft/terminal/blob/main/src/terminal/adapter/adaptDispatch.hpp)
and [xterm.js's explicit mode fields](https://github.com/xtermjs/xterm.js/blob/master/src/common/services/CoreService.ts)
were also checked. The CLR layout is deliberately private: snapshot-v1's
43-bit current/saved/default order, semantic setters and all reset/hold/mouse
side effects remain unchanged. Unknown and separately stored mode numbers do
not alias an extended-mode bit.

New regression cases cover every mode, independent value copies, set/reset/query
cycles, the full 16-bit lookup domain, unknown/out-of-range numbers, built-in
defaults, zero-allocation container operations and first snapshot installation.
`--managed-mode-state` pairs the former hash-set/scan representation with packed
storage for creation, known flags, numeric queries and toggling. `--managed-print`
also includes mode-churn and grapheme-mode workloads for actual processor costs.
Tests, before/after profiling and CI inspection remain deferred to final validation;
neither a measured throughput gain nor a whole-terminal heap reduction is claimed.

## Managed search capture invalidation

The search-only COW capture now has a lazy, buffer-specific change token. An
unchanged capture checks token identity/revision, columns, viewport height and
screen selection in constant time without allocating or walking history. The
first search capture attaches row observers; terminals that have never searched
do not allocate a token. Writes through row spans/indexers, width/wrap changes,
cell-storage swaps and structural row-buffer changes invalidate it independently
of renderer dirty acknowledgments. Read-only access, unchanged layout setters
and semantic-only row metadata do not invalidate search.

For active-only edits since the latest successful capture, row observation also
proves that the history prefix is unchanged. Capture skips its whole 64-row
reference blocks and compares only the active rows plus at most 63 boundary
rows, retaining the weak storage cache. Changed captures still clone the outer
block-reference array when needed: this is O(history blocks + active rows), not
constant-time capture. Direct history edits, structural changes, lost observers
and stale consumers fall back to exact O(total rows) storage/layout comparison.
Conservative invalidations may publish a new capture with the same row blocks.
A stamp never changes after publication, so one search consumer cannot advance
another consumer's view; the dirty scope is acknowledged only after allocation
of the complete new capture succeeds.

Tokens contain no owner references and are not copied into COW row wrappers.
Snapshots therefore do not retain live screens/history through their observer.
Transferring observation of an aliased row invalidates its previous token; a full
capture reattaches even rows in unchanged blocks. Retired rows can conservatively
invalidate a former owner without retaining it. Revision saturation permanently
disables the shortcut rather than risking stale equality after rollover. The
existing screen-lock/no-retained-writable-cell-reference contract is unchanged.

Active/history membership uses a spare row-metadata bit, not another per-row
object or index. A wrapper present in both regions is always treated as history.
Structural edits or a different active-window boundary replace the token and
rebind membership, so recycled history rows become active without retaining a
permanent full-scan penalty. This rebuild is lazy and happens once at the next
capture, not per added/removed row. Viewport scrolling does not change which
terminal rows are mutable. Wrapped and hard-break matches across the retained
history boundary continue through the existing KMP checkpoints.

[Ghostty search](https://github.com/ghostty-org/ghostty/pull/14097) retains history
work, gates active search on content dirtiness and tracks screen generations.
[Windows Terminal's mutation ID](https://github.com/microsoft/terminal/blob/main/src/buffer/out/textBuffer.cpp)
and [xterm.js's line-cache invalidation](https://github.com/xtermjs/xterm.js/blob/master/addons/addon-search/src/SearchLineCache.ts)
also avoid treating renderer state as search validity. The managed token is a
CLR-specific invalidation mechanism, not a transplant of Ghostty's page allocator
or xterm.js's event/timeout cache.

New cases cover row/structure mutations, aliased wrappers, independent COW
owners/consumers, adoption, both sides of storage swaps, renderer acknowledgment,
screen/viewport transitions, slices, saturation and weak ownership. Active-window
cases add direct-history fallbacks, boundary blocks, both-region aliases, recycled
membership, stale-consumer capture and cross-boundary search equivalence. These cases
and `--managed-search-capture` are authored but unrun. The benchmark separates
idle, first/last active-row edits, history edits, wrap changes and scrolling at
1,024/16,384 rows, plus a paired
observed/unobserved write loop to quantify notification overhead. Before/after
profiling, allocation measurements and all validation remain deferred; no
measured speedup is claimed.

## Managed snapshot grid transport

The grid codec ports the row-transport work from
[Ghostty #13848](https://github.com/ghostty-org/ghostty/pull/13848):
[vectorized encoding](https://github.com/ghostty-org/ghostty/commit/973f619a2),
[narrow decoding](https://github.com/ghostty-org/ghostty/commit/2aaad3ca9),
[per-row wide repair](https://github.com/ghostty-org/ghostty/commit/7c1014ef6) and
[batched grapheme encoding](https://github.com/ghostty-org/ghostty/commit/593762cfa).
Runtime-vectorized trailing-zero detection and OR classification select the
same 1/2/4/8-byte row width. `Vector<T>` narrowing packs 1/2/4-byte output, while
8-byte output bulk-copies the managed wire words, which already contain link
IDs. One/two-byte input widens in vectors; surrogate BMP lanes become U+FFFD
before widening. Full-word semantic normalization remains scalar. Only rows
containing width markers run the separate wide-pair repair, including elided
default cells beyond an encoded prefix.

No hardware-vector or little-endian assumption leaks into the wire contract:
scalar tails and nonaccelerated/big-endian fallbacks retain explicit little-endian
conversion. Helpers validate spans before writes and operate within the existing
4 KiB stack buffer. Grapheme entries share that buffer across small records;
larger entries flush then stream in bounded chunks. Suffix codepoints bulk-copy
on little-endian hosts, entry order remains row-major and empty suffix maps avoid
a grid scan. An oversized owned suffix is rejected instead of truncating its
16-bit length field. Decode limits, suffix filtering, page ID resolution and
live allocator reconstruction remain unchanged.

Suffix decoding additionally counts valid nonzero scalars with runtime vectors
before allocating the exact admitted output size. All-valid little-endian
entries bulk-copy into owned arrays; malformed entries retain scalar filtering
in wire order, including a scalar tail/nonaccelerated/big-endian path. NUL,
surrogates and values above U+10FFFF do not consume the raw quota. Validated raw
arrays also supply the live restore's prefix count without decoding those same
bytes again. Its independent 64-suffix cap, allocation replacement boundaries,
whole-prefix failure and duplicate retry order remain intact; raw retention can
still be longer than live storage. This extends the buffered suffix work in
[Ghostty's codec change](https://github.com/ghostty-org/ghostty/commit/593762cfa)
without changing the lossless/raw versus bounded/live contract. New scalar-oracle,
quota, ownership, duplicate/framing and allocator-replay cases are authored but
unrun; the grid benchmark adds long-valid and filtered suffix workloads.

[Windows Terminal text export](https://github.com/microsoft/terminal/blob/main/src/buffer/out/textBuffer.cpp)
and [xterm.js serialization](https://github.com/xtermjs/xterm.js/blob/master/addons/addon-serialize/src/SerializeAddon.ts)
do not define this binary format; Ghostty remains the wire reference. New scalar
oracle, all-BMP, lane/tail/alignment, sentinel/bounds, width classification, suffix
batching and zero-allocation cases are authored but unrun. The
`--managed-snapshot-grid` harness separates encode/decode for blank, ASCII, BMP,
styled, linked, wide and dense/sparse-grapheme grids. Before/after measurements,
hardware-disabled execution, native golden/round-trip tests and the full suite
remain deferred; no measured speedup or cross-platform sign-off is claimed.

## Managed snapshot software checksum

The managed framing checksum now ports Ghostty's
[slicing-by-16 CRC32C](https://github.com/ghostty-org/ghostty/commit/36d8e3f77779939a4413ddcd72c05ab08aeae57d)
and [three-stream interleaving](https://github.com/ghostty-org/ghostty/commit/eb09bf82918de51f22b805dc705ed67b2968b984).
SSE4.2 and ARM CRC hosts retain the existing `BitOperations.Crc32C` word loop.
Without those instructions, spans use 16-byte slicing, with three independent
CRC chains for inputs of at least 4096 bytes. GF(2) zero shifts combine the
chains without changing initial/final XOR, little-endian framing or streaming
boundaries. Private, immutable-after-initialization polynomial tables use 16 KiB;
the separately initialized even-power matrices use 2 KiB, sufficient for every
int-sized span. Hardware-only use does not need these software tables. Warm
updates require no per-call allocations.

The expected benefit is reduced dependency-chain latency on non-CRC hosts;
hardware throughput is intended to stay unchanged. No measured gain is claimed.
The `--managed-snapshot-checksum` harness pairs the previous runtime word loop,
forced software and automatic selection over eleven payload sizes. Run it both
normally and with `DOTNET_EnableHWIntrinsic=0` in the final validation phase.
New bitwise-oracle, high-power matrix, alignment/tail, 4096-byte threshold,
streaming, golden-record, corruption, concurrency and warm-allocation tests are
authored but unrun. Full-suite, native round-trip and platform sign-off remain
deferred. Windows Terminal text export and xterm.js SerializeAddon, linked above,
have no equivalent binary checksum format; Ghostty remains the wire reference.

## Managed snapshot record scratch

The managed binary snapshot encoder follows Ghostty's
[bounded record-scratch change](https://github.com/ghostty-org/ghostty/commit/ee8095d37d9813669688cf2f666756e607b84713):
one reusable record buffer starts with 512 bytes on the stack and rents a larger
buffer only when required. Larger records reuse that rental; retired buffers are
returned with clearing on growth, success or failure. C# ref-struct lifetime rules
and constrained generic codec sinks prevent boxing or escaping stack pointers.
The existing Stream codec entry points use an allocation-free value-type adapter.

Record and cumulative payload bounds are checked before scratch writes/growth.
The buffer does not change record framing, CRC32C, page order, continuation,
snapshot ownership or caller stream lifetime. Source-page capture still owns
managed objects; this is not an allocation-free whole-snapshot claim. The
`--managed-snapshot-encode` benchmark measures full encode to `Stream.Null`, with
terminal setup and returned byte-array ownership excluded, to compare allocation
and throughput before/after this change.

[Windows Terminal's text/VT export](https://github.com/microsoft/terminal/blob/main/src/buffer/out/textBuffer.cpp)
and [xterm.js's SerializeAddon](https://github.com/xtermjs/xterm.js/blob/master/addons/addon-serialize/src/SerializeAddon.ts)
were checked; they do not define this binary record format. Ghostty remains the
wire-format reference. Golden codec, native round-trip, stack/pool boundary,
failure cleanup, zero-allocation small-record and exact-limit tests cover the port.

## Host visibility reports

Both VT processors implement `ITerminalVisibilityState`, following
[Ghostty #13494](https://github.com/ghostty-org/ghostty/pull/13494): `CSI ? 998 n`
queries and every enable of mode 2033 report current host visibility; effective
changes report only while enabled. `CSI ? 999 ; 1 n` means potentially visible,
and `CSI ? 999 ; 2 n` means known hidden. Repeated host assignments are silent.
Visibility survives RIS, API/session reset and synchronized-output publication;
it is not serialized into snapshots or inferred from keyboard focus.

`TerminalControl` observes itself and its visual ancestors, opacity, window
hide/minimize and attachment. The visibility state is installed before a new VT
processor consumes input. Updates use the same screen synchronization and response
path as parsing, so they do not corrupt incomplete CSI/OSC input or wait for a
render frame. External endpoints can opt into `ITerminalVisibilitySink`; old
endpoints and ancestors stop receiving updates when detached. Unknown compositor
occlusion/workspace suspension remains potentially visible: Avalonia's public host
state does not expose Ghostty GTK's compositor suspension signal.

The native embedding adds two repository-owned visibility accessors. The setter
uses Ghostty's encoder and a caller-owned nine-byte buffer; it does not replay VT,
allocate, retain pointers or call through a private wrapper layout. The managed
wrapper returns owned response bytes for ordered delivery, and the native VT
adapter forwards these to its normal response callback.

[Windows Terminal](https://github.com/microsoft/terminal/blob/main/src/terminal/adapter/adaptDispatch.cpp)
and [xterm.js](https://github.com/xtermjs/xterm.js/blob/master/src/common/InputHandler.ts)
were checked for their separate focus-reporting paths. Their focus mode is not
used as a substitute for Ghostty visibility reports. Focused protocol, native
argument/buffer, snapshot and Avalonia lifecycle tests cover this distinction.

## Hidden-terminal rendering resources

Both VT engines use the same visibility-aware Skia presenter. Hiding the
presenter or an ancestor, setting an ancestor's opacity to zero, minimizing its
window, or detaching it releases the owned retained framebuffer and compiled
framebuffer shaders. Hidden output and cursor invalidations do not schedule
composition frames. The terminal model continues to receive output; showing it
recreates resources lazily and fully redraws the latest screen, size, and shader
configuration, including rows whose dirty flags were already acknowledged.

This follows [Ghostty's hidden-surface resource release](https://github.com/ghostty-org/ghostty/commit/c4e16970a803b170e352432424f44192cb59f3ac),
[xterm.js's hidden refresh suspension](https://github.com/xtermjs/xterm.js/blob/master/src/browser/services/RenderService.ts),
and [Windows Terminal's full viewport refresh when painting resumes](https://github.com/microsoft/terminal/blob/main/src/renderer/base/renderer.cpp).
It is not a transplant of Ghostty's Metal/OpenGL swap chain.

Avalonia composition messages do not guarantee a current GPU context. The
framebuffer owner retains the borrowed context identity from Avalonia's public
Skia platform lease, re-enters it for release, and never disposes that context.
Retained GPU surfaces are unbudgeted so releasing a hidden framebuffer does not
leave it in Skia's shared resource cache. Context loss is handled without issuing
GPU operations; a custom backend without a re-enterable platform context uses a
raster framebuffer. Shared font/image caches and other terminals' GPU resources
are not purged. OS occlusion is not inferred from focus, and a visible background
terminal keeps rendering.

Headless coverage exercises hidden startup, ancestor/window/opacity transitions,
latest-state redraw, shader configuration ownership, reparenting and late
messages. A 20-hidden-plus-one-visible 960×600 fixture checks that hidden retained
framebuffer ownership falls from 46,080,000 bytes to zero while the visible
framebuffer remains allocated; this is not a whole-process or GPU-memory claim.
Native macOS CGL tests exercise release outside a current context, shared-target
survival, context loss/abandonment/disposal, retry, and raster fallback.
The presenter synchronizes its composition size after layout assigns new bounds;
antialias clip outsets are not treated as DPI scaling or framebuffer dimensions.

## Batched managed printing

Managed input now writes eligible narrow and wide runs with one row-local cell
template and one writable COW span. ASCII uses the runtime's vectorized range
scan without decoding; UTF-8 and REP use bounded 256-codepoint stack storage.
Snapshot-backed rows update contiguous old-style reference counts in groups.
Wrapping, page transitions, complex cells, hyperlinks, grapheme joins, character
sets, insert mode and disabled wrap retain the scalar printer's semantics.
Incomplete UTF-8 remains with the streaming decoder. Managed cells contain CLR
references, so Ghostty's packed-u64 SIMD stores are not copied into their layout.

The behavior follows Ghostty's
[printSlice implementation](https://github.com/ghostty-org/ghostty/blob/622b4eecd7d2ce1a10930537c17f0d61abdba817/src/terminal/Terminal.zig)
and [batched REP change](https://github.com/ghostty-org/ghostty/pull/13625).
[Windows Terminal's string writer](https://github.com/microsoft/terminal/blob/main/src/terminal/adapter/adaptDispatch.cpp)
and [xterm.js's print/REP handlers](https://github.com/xtermjs/xterm.js/blob/master/src/common/InputHandler.ts)
also share printing paths. RoyalTerminal retains Ghostty's last-codepoint REP,
not xterm.js's whole-grapheme extension.

`ManagedPrintSliceTests` compares bulk input against byte-at-a-time scalar input,
native Ghostty, retained COW readers, snapshot style capacities and round trips.
Three seeded edit/resize sequences add 900 mutation checkpoints. The maintained
`--managed-print` benchmark compares printing only, without PTY or renderer work.
One local macOS arm64 Release comparison against `b649ce2`, with tiered compilation
disabled in both processes to exclude tier-promotion timing, measured the median
of seven 25,000-feed samples after 25,000 warmups:

| Workload | Before (ms) | Batched (ms) |
| --- | ---: | ---: |
| ASCII row redraw | 71.151 | 7.823 |
| Narrow Unicode row redraw | 92.116 | 16.312 |
| Wide-cell overwrite (scalar cleanup) | 79.386 | 78.839 |
| REP | 72.421 | 11.105 |
| Alternating styled redraw | 142.780 | 18.101 |
| Snapshot-backed styled redraw | 1244.675 | 267.859 |
| DEC special charset (scalar) | 71.262 | 75.185 |

Allocations were unchanged: zero in the untracked workloads and 100,000,000 bytes
per sample for snapshot-backed styled redraws, including their existing SGR/page
bookkeeping. These isolated numbers do not establish application throughput,
cross-platform performance, or gains for every fallback workload.

## Snapshot history accounting

Incremental history admission measures logical Ghostty page bytes, not CLR heap
usage. Restored PAGE capacities are retained, including after partial pruning.
New live rows are assigned standard pages using their physical width and native
metadata defaults; subsequent admission checks reuse unoccupied tail slots rather
than charging another page. Slot identities survive COW and row rotations.
Recycling a CLR row after scrollback eviction releases its historical page identity.

Capacity growth observed at an admission checkpoint follows native default,
doubling and saturation rules. Styles and graphemes can project the current usage
over reserved rows with 25% headroom, bounded to 32 times the previous request and
the native four-GiB page ceiling. Admission uses usable set/map slots and rounded
bitmap chunks rather than treating raw capacity hints as usable item counts.
Grapheme replacement scratch is charged once, not once per retained cluster.

Measured growth is retained through later erases. An immutable replacement identity
updates only the live row set, so synchronized-output and search copies retain
their own charges. A page's charge disappears only when its last row leaves that
view. These rules follow Ghostty `PageList.grow`, `increaseCapacity` and page
replacement ownership; Windows Terminal and xterm.js use row-oriented storage and
do not define this snapshot budget. The managed hard scrollback row cap remains
independent of the logical native-page budget.

Changing `TerminalScreen.SnapshotScrollbackQuota` immediately evicts eligible whole
historical allocations in both buffers, using native byte/line minimums. A page
overlapping the active area is never split just to meet a quota. Row growth enforces
the line limit; a new tail allocation can recycle one old page for byte pressure,
but reuse of a free tail row does not re-enforce bytes. Resize enforces lines after
layout, before combined anchor remapping. Zero bytes also disables scrolling into
retained boundary history. These are `PageList` limits: unlike Ghostty's
terminal-level zero-byte setter, the snapshot policy intentionally preserves
resident READY overlap instead of erasing partial primary history. Removed anchors
are invalidated; surviving anchors and
raster placements shift once, and COW readers retain their original rows.
Host policy changes reach held live input and publication without reapplying an
unchanged byte setter on every batch. Host row rotation can interleave allocation
identities: their connected prefix remains indivisible, and a byte-growth recycle
cannot remove multiple allocations solely to work around that host representation.
These conservative cases can remain over quota. Runtime-limit, streaming, resize,
ownership and native continuation regression cases are authored; execution and
profiling remain deferred.

Column reflow now carries source-page allocation provenance through logical lines,
including lines that cross PAGE boundaries. The first destination inherits the
first source's adjusted capacity; later destinations inherit the page currently
being consumed. Deferred blank rows use the next nonblank source when allocating
another page. Width adjustments retain the layout's byte budget rather than using
extra pooled bytes; the native first/later-page fallbacks are distinct. All-blank
reflow retains the first page, and viewport padding reuses its remaining slots.
Accounting is isolated from text, style and tracked-anchor movement; ordinary
untracked screens do not create snapshot allocation metadata during reflow.

Reflow metadata now uses a dedicated copy path matching Ghostty's
[`ReflowCursor.writeCell` and `hyperlinkStringsFit`](https://github.com/ghostty-org/ghostty/blob/622b4eecd7d2ce1a10930537c17f0d61abdba817/src/terminal/PageList.zig).
Graphemes check map capacity before probing and freeing their suffix allocation;
growth retries until the suffix fits. Hyperlinks probe URI and explicit ID as
separate rounded allocations, free that scratch, then duplicate before set
deduplication—even when the destination already contains the same link. Set
failure releases both duplicated strings before growth/rehash, and replacement
pages receive a fresh string-capacity check. Ordinary row cloning keeps its
different lookup-before-allocation and failed-clone string-retention behavior.

The managed reflow copy shares immutable encoded link bytes and their cached
hash, avoiding an extra URI-sized CLR copy or hash while preserving logical
native allocation transitions. This is an expected hot-path cost reduction, not
a measured throughput claim. Windows Terminal's
[`TextBuffer::Reflow`](https://github.com/microsoft/terminal/blob/main/src/buffer/out/textBuffer.cpp)
and xterm.js's [`Buffer._reflow`](https://github.com/xtermjs/xterm.js/blob/master/src/common/buffer/Buffer.ts)
are visible-layout references but do not define Ghostty's page-local allocator.
Sixteen new cases cover map-versus-bitmap pressure, separate string rounding,
duplicate scratch, growth/rehash cleanup, retained owners and repeated native
resize comparisons. `--managed-snapshot-reflow` adds four snapshot-tracked
workloads alongside the existing untracked reflow harness. Test execution,
before/after profiling and CI remain deferred to the final validation phase.

Reflow now handles the native page-growth ceiling without marking a representable
destination as overflow. A later-row refusal clones the current row into a new
source-capacity-derived page before removing its old ownership; the upper page
retains its dead-ID/string history. A first-row refusal preserves the accepted
grapheme/link/style prefix, drops only the rejected stage and subsequent metadata,
and continues with following cells. A second set-insertion refusal after one
growth/rehash drops only that set's field, matching native unsafe-build recovery;
hyperlink failure does not prevent independent style copying. Screen payloads are
copied before this filtering, including bulk runs and wide tails, so rejected
metadata cannot be restored by a subsequent raw copy.

Managed split recovery deliberately resumes at the failed metadata stage instead
of replaying already-cloned ownership. This retains one owner for accepted
graphemes and links; it does not emulate native partial-cell replay's potential
`putNoClobber` failure or orphan-reference history. An unexpected row/page clone
failure throws into the processor's existing resize rollback instead of publishing
partial allocator state. Real CLR allocation failure also propagates. Twenty-eight
additional authored cases cover 4/16-KiB growth ceilings, five metadata pressure
types, retained sources, split-prefix ownership, grouped/scalar/wide payload
filtering, row-clone rollback with anchors, and bounded adversarial set retries.
Sparse logical capacities avoid allocating four-GiB pages in these cases.
These changes and tests have not yet been executed or profiled.

Managed style copy now looks up each occupied source chunk once, scans equal-ID
runs with bounded span operations and fills destination chunks in bulk. Grouped
reference increments retain preferred-ID insertion order and exact accepted
prefixes on failure. Page rebuild uses the same run-copy path, including logical
row remaps and inline background observations. Sorting scratch for physical
rebuilds uses at most 128 stack integers, otherwise a returned pooled integer
buffer sized to occupied chunks, never the PAGE dimensions. This extends
Ghostty's reflow style-run optimization to managed page rebuilds; it does not
change the wire layout or native capacity/probe rules. Sixteen additional
scalar-oracle, boundary, sparse/pool, remap, reference-release and failure cases
are authored. `--managed-snapshot-style-rebuild` adds uniform, alternating,
rotated-inline and sparse workloads for the final before/after measurements.
No test execution, allocation measurement or throughput improvement is claimed.

Non-reflow widening also rejects a zero-progress row clone instead of retaining
its payload with an overflow marker. An already-valid source table may become
unclonable in row order when 32 colliding styles precede another style that was
originally inserted first. The processor's staged transaction keeps the original
rows, metadata, anchors and held output usable on failure. Partial successful
pages may still be split and retried; only failure on an empty fresh page aborts.
The native source overlay terminates that same formerly unbounded retry, releases
the unlinked page and restores this source chunk's pins before rollback cleanup.
This does not claim whole-terminal native rollback after earlier source chunks
have committed. Twelve managed rollback/recovery cases and two native collision/
pin regressions are authored but unrun. Rebuild the native extension before
executing the native regressions: the previous library loops on their fixture.

Quota capacity measurement now obtains styles, grapheme cells/bytes and hyperlink
owners/cells/string bytes through one current-page census. It checks every supplied
row revision once, rejects foreign page identities, and uses indexed access rather
than boxed `IReadOnlyList` enumerators. Individual diagnostic queries share that
allocation-free gate. Cursor-only references, occupied allocator slices and COW
owners remain authoritative; dirty/untracked pages retain the existing cell-based
fallback. This reduces revision walks and weak-table lookups without changing
Ghostty's native allocation accounting or growth projection. Twelve new counter,
single-pass, invalidation, identity, COW, host-write and warm-allocation cases are
authored but unrun. `--managed-snapshot-usage` compares one combined query against
three indexed queries for current and dirty-tail pages at four row counts; that
paired baseline isolates traversal work, not the old enumerator allocations.
Full checkpoint costs and before/after profiling remain unmeasured.

This is not yet exact mutable allocator parity: the full failure/degradation and
mutation-order audit remains unfinished. Cursor-style pressure splitting uses
exact live row-layout selection, keeps the upper allocator and clones the suffix
before publication. Migrating links precede the style retry; failed SGR retries its
previous pen while cursor restoration/movement falls back to default. Other
unconnected paths cannot reconstruct allocator history from final cells.
Boundary, COW, recycling, source-provenance,
blank/wrapped reflow, growth-ceiling and native admission comparison tests are added;
execution and profiling are pending the full validation phase.

Live snapshot restore shares Ghostty's 64-suffix-codepoint bound with terminal
input, excluding the base scalar. Valid scalars beyond that bound are ignored only
when materializing live cells; the raw PAGE codec remains lossless and still
consumes the complete suffix and enforces caller decode limits. Unlike xterm.js's
combined-string appends and Windows Terminal's row/text storage, the managed engine
uses Ghostty's bound so snapshot restore cannot create a larger live cluster than
terminal input. Bounded UTF-16 scratch covers even supplementary base and suffix
scalars. Wire-preservation, limit and native continuation tests are added but unrun.

Grapheme restore also models native allocation pressure while consuming entries in
their original wire order. The native map limit and 16-byte bitmap chunks apply;
small allocations stay within a bitmap word and replacement keeps its old slice
alive until the new one fits. Failure drops the complete suffix and releases its
prefix, permitting a later duplicate to succeed. The raw grid retains its existing
first-valid-entry content and canonical re-encoding behavior independently of these
live outcomes. The temporary bitmap materializes words only for actual bounded
content, never in proportion to an untrusted capacity hint, and is released after
parsing. Invalid scalars/targets do not consume logical storage. Whole-prefix
failure, duplicate recovery, wire-order, huge-hint and seeded native comparison
tests are added; execution remains pending.

Style and hyperlink restore now honor native table/string pressure too. Logical
reference-counted sets reproduce Robin Hood probing, the 31-probe insertion guard,
duplicate-value references, dead-ID reuse and trailing-ID reclamation. Style hashes
use the native packed representation and integer mixer; hyperlink hashes include
native ID tags and 64-bit slice lengths with streaming Wyhash. Set/table capacity
hints do not allocate dense CLR tables.

Hyperlink IDs and URIs consume 32-byte bitmap chunks before value deduplication.
Ignored zero/duplicate wire IDs still perform native insertion/release, so their
dead entries may hold strings until a later insertion reclaims them. Failed URI
allocation frees its explicit ID, and the fixed hyperlink-cell map admits cells in
row order. Raw accepted tables remain independent of the live result. Temporary
hyperlink sets/bitmaps are discarded after parsing; accepted IDs and suffixes
remain. Style storage now additionally retains a decode-time allocator seed:
each accepted wire entry surrenders its temporary reference after cells take
their own, including equal values under different wire IDs. Dead slots and probe
placement survive until reuse or rehash. Allocation identities expose isolated
copies of that seed so mutable bookkeeping cannot change raw pages or held COW
frames. Sparse 256-cell chunks use 16-bit IDs, avoiding per-cell dictionary
entries and allocation proportional to absent prefixes or capacity hints.

The style-storage lifecycle model includes cursor-only release/add, cell
write/erase, native rehash-versus-growth failure reasons, preferred-ID insertion
and row-major rebuilds. A rebuild drops dead entries and leaves cursor restoration
to its owner. Unit and native cursor/write/erase comparisons are added but unrun.
The processor now uses the model for each changed SGR parameter/group, including
intermediate styles reset later in the same CSI, and for primary/alternate SCREEN
cursor restoration. New styles are charged before printing and retain their
capacity after erasure. Live entries reconcile changed row revisions before a
pen change; unchanged rows are not rescanned cell by cell. Decoded inline
background overrides retain their native style identity during reconciliation.
Ordinary non-snapshot/non-quota terminals do not create the tracker.

Live allocator entries use weak page keys and fork on COW mutation. Style-only
changes do not copy terminal cell arrays; synchronized-output publication transfers
the private allocator state with its row ownership. New empty/collision pressure,
rehash, transient-write, cursor-restore and COW/publication comparisons are added
but unrun. The immutable seed remains decode/rebuild-time state, separate from
the tracker owned by the mutable screen.

Cursor movement now transfers the style reference on page changes, including
explicit positioning, index/reverse index, text wrapping, Kitty placement's line
feeds and the optional Sixel cursor advance. Same-page movement retains the
reference. DECRC installs the saved pen on the departure page before movement;
47/1047 screen switches and 1049 entry install the incoming pen at the dormant
destination cursor before copying its position. A 1049 return instead resumes
the dormant primary cursor before restoring, without copying the alternate pen.
Compound DEC mode parameters observe each intermediate movement. These choices
follow Ghostty `Screen.cursorChangePin`, `Screen.cursorCopy` and
`Terminal.restoreCursor`/`switchScreenMode`; Windows Terminal and xterm.js have
different storage models and no equivalent PAGE allocation contract.

The pinned Ghostty resize implementation has one intentionally unported allocator
quirk: partial alternate-history erasure changes a node's row-layout serial without
replacing its allocation, so `Screen.resize` skips releasing a temporary hyperlink
reference when the native page layout permits reusing that allocation. A subsequent
resize then grows the native hyperlink table from 192 to 384 bytes unnecessarily;
layouts that replace the allocation do not retain the orphan. Managed leases use
allocation identity and release the reference;
the differential regression checks identical cursor/link state, exactly one managed
live link and the specific native capacity divergence in both active-buffer orders.
This is not exact allocation-history parity and does not change the native dependency.

When scrollback pulling is enabled, managed VT reflow also preserves Ghostty's
active-row space below the cursor, discounting new wrap continuations above it.
Narrowing applies the new height before reflow; widening applies it afterward.
Height growth pads below a non-bottom cursor instead of pulling history into view.
This follows `PageList.resizeCols`'s `preserved_cursor` policy, not Windows
Terminal's fixed-buffer cursor mapping or xterm.js's `ybase`/cursor-line policy.
Generic screen-only resizing and explicit ConPTY viewport preservation are unchanged.
Differential coverage exercises both pull-scrollback settings, live and restored
screens, wrapped cursors, mixed width/height changes, writes and resize round trips.

Screen-switch cursor copies now have their own failure boundary. If installing
the entering style at the dormant destination fails, the destination keeps its
position, pending wrap, pen, protection, cursor shape, semantic state and implicit
hyperlink counter. The old pen is reacquired in the surviving allocator rather
than restoring a stale page-local ID; a second allocation failure safely falls
back to default. Any completed page split, buffer switch, required clear, charset
transfer and hyperlink closure remains committed. Failure only when subsequently
moving to the copied position still uses the normal movement fallback, not cursor
rollback. Mode 1049 clears reset the dormant pending-wrap flag before a failed
copy; a successful copy retains the entering cursor's wrap state. Erase operations
own their wrap reset, including mode-switch clears, while history-only ED3 and
ignored ED/EL parameters leave it unchanged. Cursor-copy, COW, split, output-hold
and native wrap-continuation tests are authored but unrun.

Streaming tail line feeds assign slots using an owner-local high-water mark
instead of measuring the full history for every new row. Checkpoint-assigned
slots update that mark, and COW forks preserve independent ownership. Page
transitions still reconcile row groups; this is not an end-to-end performance
claim. Discarding alternate storage releases its retained cursor-page entry;
clearing all storage drops the tracker. Movement, screen-switch, tail-slot,
Sixel, host-clear and native capacity comparisons are added but unrun.

Live row edits now update style references before their cell changes. Printing,
wide-cell cleanup, ED/EL/ECH, protected erase runs, hidden-column removal, prompt
redraw and full/partial row shifts use explicit write/clear/move operations.
An erased background-only cell releases its native style even when its visible
attributes do not change. Cross-page row copies clear the entire destination run
first, insert using preferred source IDs and retry the whole run after capacity
growth. Within a page, whole-row shifts exchange cell-array ownership and COW
flags without copying arrays; partial-width shifts clear the destination and move
the source run. ICH/DCH permute style references before releasing the vacated run.
These distinctions follow Ghostty `Page.clonePartialRowFrom`/`moveCells`,
`Screen.clearCells` and the terminal insert/delete implementations.

Clear operations group equal style IDs into one reference-count update, and a
single reusable empty 256-cell chunk avoids repeated allocations when replacing
the last styled cell. Untracked screens do not allocate the style tracker. Added
tests cover identical-background erasure, protected holes, transient copy growth,
preferred IDs, same-page moves, COW storage, chunk reuse and native capacities;
execution and performance measurement remain pending.

Snapshot-aware reflow, row retirement and style/grapheme/hyperlink mutation hooks
are connected, but complete mutation-time parity still requires the remaining
failure/degradation audit. Live grapheme append now keeps the previous suffix if
growth or its single retry fails, while retaining earlier width, tail and cursor
edits. Cross-page widening copies retain the source and only the accepted scalar
prefix on failure, stopping before the tail and final scalar; completed same-page
moves remain committed if the final append fails. Copied prefixes preserve a
remapped inline base and supplementary scalars. A refused hyperlink cell-map
insertion omits that cell's link but leaves a surviving OSC 8 cursor active.
Failed cursor starts/migrations publish the dropped link without consuming an
implicit ID. A newly registered pending protocol identity is now also removed
from both managed lookup indices on refusal or thrown preparation, including
implicit page migration and resize restarts. Existing/public registrations and
cell-owned source identities are never discarded by that rollback. Collision
chains remain immutable for COW readers; head removal needs no new allocation,
and only a colliding prefix is rebuilt before publication. The token watermark
rewinds only if no later registration exists. Registry insertion prepares both
indices before publishing either. This follows `Screen.startHyperlinkOnce`'s
pending-value cleanup, preventing repeated refused OSC 8 values from accumulating
URI/identity objects for the screen's lifetime. It does not introduce general
pruning of successful public registrations. New refusal/retry, migration,
resize, held-output, exception, collision/COW and weak-lifetime cases are authored
but unrun; allocation and throughput profiling remain deferred.
[Windows Terminal](https://github.com/microsoft/terminal/blob/main/src/buffer/out/textBuffer.cpp)
prunes unreachable hyperlink IDs as rows retire, while
[xterm.js](https://github.com/xtermjs/xterm.js/blob/master/src/common/services/OscLinkService.ts)
uses line-marker lifetimes. Neither defines the native PAGE refusal contract;
Ghostty defines that boundary here, with stable public registrations preserved.
None of these refusals marks an otherwise representable page as
overflowed. Focused near-four-GiB logical-capacity, COW and recovery cases are
authored but unrun; fixtures do not allocate huge native pages. These boundaries
follow Ghostty `Screen.appendGrapheme`, `startHyperlink`, `cursorSetHyperlink` and
`Terminal.print`; Windows Terminal and xterm.js have different storage models.

Page replacement now distinguishes a failed cell clone from cursor-only style
reinsertion failure. Like `Screen.increaseCapacity`, it commits the successful
clone, defaults a refused cursor pen, and independently attempts hyperlink
restoration once. A COW-owned notification updates active/dormant pen registers,
including snapshot capture and later input, without discarding cell styles or
protection. Explicit later SGR may establish a new pen. Deterministic crowded-probe
regressions cover grapheme/link-driven growth, independent cursor details, saved
cursors and COW ownership without forcing process OOM. These tests are authored
but unrun; no new performance claim is made.

Exhausted cross-page row-copy retries are now fatal to the managed terminal
owner, rather than falling through as a successful copy of the source payload.
Ghostty's `Screen.clonePartialRowGrowCapacity` panics here because callers have
already moved rows and pins. RoyalTerminal deliberately contains the failure to
the screen/processor instead of crashing its embedding process: further input,
reset/session reuse, resize, search, selection and snapshot/continuation export
reject the partial state. A fresh processor and screen are required. COW readers
retain their independent state, and failed synchronized-output staging is never
published by timeout or disposal. Cursor accounting skips exception unwinding;
the existing output worker propagates the failure and stops its drain. Successful
capacity growth still retries the copy. Twenty-four full-width/rectangular,
grapheme/style/link-map/string-pressure, COW, lifecycle, held-publication and worker
regression cases are authored but unrun.

CLR allocation failures in scalar/batched printing, grapheme append/transfer,
character shifts, row/cell erasure, hidden-cell retirement, raster text clearing,
prompt redraw and cursor/row metadata coordination now latch the affected owner
before exception cleanup. The latch stores the original exception without
allocating a tracker or `ExceptionDispatchInfo`; retained COW owners remain
independent, and copying a failed owner preserves its failure. Exception filters
run before row-revision stamping and cursor restoration, so cleanup cannot mark
partially updated metadata synchronized or hide the first failure. Native logical
capacity refusals still retain their existing retry/degradation behavior. This
is failure containment, not a promise of successful recovery from process OOM.

These are internal mutation boundaries, not a catch around arbitrary input:
title/bell/response observers may fail after a valid mutation, and a failed staged
resize must still roll back to its usable original owner. Instance-local
allocation tripwires cover partial metadata edits, held-output nonpublication,
revision cleanup, copied failures and resize rollback; those new tests are
authored, not yet executed. Wider allocator/publication failure auditing remains.

Character insertion/deletion now reuse one writable span per row, avoiding
repeated COW checks and metadata revision increments per shifted cell. Printing,
erasure and raster clears detach writable cell storage before committing metadata
where no full-row ownership swap is needed. Full clears retain their no-copy
replacement path. There are no per-cell journals, delegates or rollback snapshots
on the normal path. This ordering follows the prepare-before-install approach in
[Windows Terminal `ROW::_resizeChars`](https://github.com/microsoft/terminal/blob/main/src/buffer/out/Row.cpp)
and the separate cell/combined-string storage in
[xterm.js `BufferLine`](https://github.com/xtermjs/xterm.js/blob/master/src/common/buffer/BufferLine.ts),
while preserving Ghostty's page pressure and partial-append semantics. The
`--managed-print` benchmark now includes tracked/untracked character shifts,
tracked erase and grapheme append for final before/after measurement. No measured
speedup or current-head CI result is claimed in this implementation phase.

Synchronized-output preparation now stages the complete owner and deadline, and
reserves mode-table capacity, before exposing either a held frame or enabled mode
2026. Failed preparation retains the visible owner; failed pre-publication work
retains the old hold/deadline and mode for retry. Timeout and protocol release use
the same commit ordering. A source or destination with a latched mutation failure
cannot participate in ownership transfer. Partial quota enforcement and native or
host history retirement latch the affected owner before cleanup; a failed private
quota update cannot publish on input, timeout or disposal. Dormant-buffer scopes
prepare missing raster collections before changing live fields, since a throwing
constructor would not run their restore logic.

History prepend keeps all decoding, hyperlink registrations, anchor arithmetic
and prompt inspection before row publication, like Ghostty
`PageList.PageAllocation.prepend`. Empty raster lists are reused rather than
allocated for every unlinked history page. State/history commit tails mark dirty
rows under their existing serialized-access contract, without entering another
monitor after ownership transfer. COW page forks and same-identity reflow tables
use `ConditionalWeakTable.AddOrUpdate` instead of removing/re-adding weak entries;
new page identities are registered before old entries are retired. Tail slot
bookkeeping is reserved before consuming a free slot. These changes reduce
unnecessary registry churn and commit-time work, not the required COW snapshots.

The chosen visibility behavior follows Ghostty's renderer suspension and timer,
[Windows Terminal's synchronized-output mode](https://github.com/microsoft/terminal/blob/main/src/terminal/adapter/adaptDispatch.cpp)
and [xterm.js's buffered refresh/timeout](https://github.com/xtermjs/xterm.js/blob/master/src/browser/services/RenderService.ts).
The managed host additionally needs explicit COW publication safety because its
published rows are shared with independent readers. Twenty publication/quota/
history/ownership failure and retry cases are authored but unrun. The
`--managed-print` benchmark adds tracked/untracked hold cycles; final build, test,
benchmark and CI execution remains deferred. This is not yet a completed audit
of every allocator failure or platform path.

Screen-only buffer switching now prepares missing rows and raster collections
before publishing the selected buffer, following Ghostty `ScreenSet.getInit`
and `switchTo`, [Windows Terminal's alternate-buffer creation](https://github.com/microsoft/terminal/blob/main/src/cascadia/TerminalCore/TerminalApi.cpp)
and [xterm.js's fill-before-activation ordering](https://github.com/xtermjs/xterm.js/blob/master/src/common/buffer/BufferSet.ts).
The existing Ghostty-compatible persistent alternate-buffer policy is unchanged.
Failed screen-only preparation remains retryable; failure after selection or
within a protocol switch faults the owner, since protocol mode/cursor registers
may already have changed. Row recycling, multi-row scroll-clear, theme recoloring
and raster-anchor shifts also contain partial allocation failures. A failed owner
cannot be repaired by clearing all rows. A normal screen reset prepares replacement
rows directly without normalizing a dormant buffer only to discard it.

Resize failures latch their private staging owner before cursor-lease cleanup.
Faulted leases do not fork shared tables during unwinding, preserving the original
failure and the outer transaction's usable pre-resize owner. Successful and
non-faulted abandoned leases retain their existing reference-restoration rules.

Raster-image retirement skips registry scans when scrolling changes anchors but
removes no placements. Empty/single-image cleanup needs no scratch allocation;
multi-image cleanup reuses owner-local scratch and removes stale dictionary entries
without a temporary deletion list. This scratch is not snapshot state and is not
shared by COW owners. Replacement reserves registry/list capacity before deleting
overlap or clearing text. Thirty structural/cursor/raster regression cases and a
`--managed-raster` benchmark (1/2/8/64 live images, warmed replacement, seven samples)
are authored but unrun. Expected savings are fewer temporary collections and
unnecessary source scans, not a measured throughput claim; before/after profiling
and all execution remain in the final validation stage.

Snapshot metadata coordination now reuses two bounded row-group buffers and one
retained-slot set per tracker. Leases clear row references on both success and
failure; scratch is neither copied nor shared with independent COW owners. Page
synchronization skips metadata-map rescans only after its first full reconciliation
and only when every live physical slot has the expected revision. Restored seeds
still discard metadata outside installed rows; dirty non-cursor rows, changed
slot membership and interleaved pages retain their full reconciliation path.
Grouping still scans the row buffer: this is not constant-time page lookup.
Style retirement also enumerates removable dictionary chunks directly instead of
allocating a copy of the chunk keys.

Ghostty `Screen.cursorChangePin` retains same-page state and migrates references
across pages; its `RefCountedSet` recycles entries in page storage. The managed
reference set now keeps one cleared entry wrapper for tail trimming, preferred-ID
replacement and dead-bucket reuse. Deletion callbacks, IDs, probe/backshift order,
native rehash/capacity refusals and reference counts are unchanged. The spare keeps
no encoded hyperlink payload and is not copied into another allocator. This is
bounded CLR reuse, not a dense allocation from snapshot capacity hints. Windows
Terminal's `ROW` and xterm.js's `BufferLine` remain row-storage references, not
definitions of Ghostty's PAGE allocation contract.

Twenty-two new revision/retirement, allocation, seed, COW, collision and weak-owner
lifetime cases are authored but unrun. `--managed-print` adds isolated snapshot
pen changes and 128-row cross-page cursor cycles. Expected benefits are fewer
temporary collections, metadata scans and per-style wrapper allocations; timings,
allocation measurements and regression execution remain deferred to final validation.

Full-width IL/DL now clamp the count to the affected rows and traverse once in
the native direction, copying directly from the requested distance. In-place
SU/SD share this bounded movement; the primary top-origin history path still
creates rows individually. Discarded intermediate rows no longer cause needless
metadata growth or a fatal copy when the command only needs to clear the region.
Tracked cell and raster anchors move by the bounded delta; the existing Kitty
margin wrapper retains stationary IL/DL placements and scroll-specific clipping.
Effective IL/DL reset pending wrap and return to the left margin, while no-ops
outside either margin axis preserve both. Wide-cell inspection now uses read-only
references, keeping valid ASCII/wide rows shared with COW readers until a real
repair or erase. Ghostty's `Terminal.insertLines/deleteLines` defines traversal,
allocation and wrap semantics; Windows Terminal's
[`_InsertDeleteLineHelper`](https://github.com/microsoft/terminal/blob/main/src/terminal/adapter/adaptDispatch.cpp)
also scrolls a bounded rectangle, whereas
[`xterm.js`](https://github.com/xtermjs/xterm.js/blob/master/src/common/InputHandler.ts)
uses repeated line splices and has no Ghostty PAGE allocator. Forty-two new
large-count, discarded-metadata, pressure, COW, anchor/raster and pending-wrap
cases are authored, including native comparisons, but remain unrun. The expected
gain is one row traversal instead of one traversal per requested line; no measured
speedup is claimed before profiling.

Scrolling now distinguishes explicit SU/SD from LF/IND. SU/SD account for the
temporary top/bottom cursor visit and its return, including unprinted style
allocation, implicit hyperlink reissue and native pen/link degradation. Primary
history growth also accounts for each intermediate page before painting its
background. Full-width LF/IND and alternate full-screen SU rotate row ownership
within each logical page; only boundary rows are cloned. Surviving wrap flags,
wide spacers, semantic metadata and COW cell storage remain intact, while the
recycled row loses old metadata and takes the accepted background. Page identities
and physical allocator slots remain owned, with no history created by alternate
rotation. The cursor remains on its page during bounded LF/IND; row rotation
does not create a spurious hyperlink migration. Fatal boundary copies retain the
existing owner-fault/no-publication behavior. Reverse index preserves pending
wrap when it scrolls, but clears it on its cursor-up fallback, including a clamped
row-zero move. Forty additional cursor,
rotation, COW, background, single-row and native differential cases are authored
but unrun. These decisions follow Ghostty `Terminal.scrollUp/scrollDown/index`,
`Screen.cursorScrollRegionUp` and `PageList.eraseRow[Bounded]`; Windows Terminal
rectangle scrolling and xterm.js line splices do not supply the per-page cursor
contract. No throughput or allocation benchmark claim is made yet.

Post-rotation page growth/rehash and checkpoint replacement now clone metadata
in logical row/column order and rebase physical slots together with revision
coverage and the tail watermark. Grapheme slices and hyperlink strings are
repacked in that order; style references retain preferred-ID and inline-background
observations. A failed clone keeps the old mapping. In-flight cell writes, cursor
hyperlink-map retries and bulk reconciliation resolve their addresses again after
every successful replacement. COW publications keep their original allocator/slot
pairs without detaching unchanged cell arrays. Translation is sparse in retained
rows and occupied metadata; style traversal keeps compact chunks rather than a
per-cell sort. Source and destination strides are independent and the existing
host hidden-column policy is retained. These decisions follow Ghostty
`Page.cloneFrom/clonePartialRowFrom`; WT ROW and xterm.js BufferLine do not define
its page allocator contract. Forty additional mapping, retry, failure, COW,
tail-reuse, huge-hint and native continuation cases are authored, pending execution.
Top-origin history scrolling with a bottom margin now also rotates within each
page. It first appends the tail and migrates the cursor to its next logical row,
then visits the stationary suffix tail-first, cloning only the rows that cross
page boundaries. It clears the recycled cursor row after those copies. This
preserves contiguous allocation ownership, physical slots and the native order
of cursor-link migration, metadata growth and copy retries. Unchanged same-page
cell arrays remain shared with COW readers. Host anchors and raster placements
below the margin shift once after any history pruning; held output publishes the
rows and allocators together. Exhausted boundary copies use the existing fatal
owner/no-publication contract. Cursor-page entry also retains the incoming
hyperlink's original identity through style installation: if that grows or
rehashes the destination page, the link is restored once before the style retry
and normal link migration. Refusal drops only the link, without growing its
tables or consuming an implicit ID; successful restoration is released by the
subsequent migration. This also applies to ordinary cursor movement, with
explicit/implicit links and provisioned/empty destination storage.
Ghostty `Screen.cursorScrollAboveRotate` defines
the page contract; Windows Terminal pans then scrolls the suffix, and
[xterm.js BufferService.scroll](https://github.com/xtermjs/xterm.js/blob/master/src/common/services/BufferService.ts)
inserts a line without page-local allocators. New ownership, pruning, COW, hold,
pressure/failure and native continuation cases cover fresh and reused tails.
The remaining mutation audit and performance profiling are still outstanding.

Unrepresentable allocation state rejects additional
history rather than wrapping a capacity or undercharging it; quota eviction can
remove such a page only once it is wholly historical. A subsequent representable
content checkpoint can recover admission. Builds, native comparisons and allocation/
throughput profiling of these paths remain pending.

Managed bitmap searches safely reject an oversized span at the last word rather
than reading past the bitmap. A hash-checked native correctness overlay now applies
the same end check to the pinned allocator, without changing its allocation order
or consuming bits on failure. Native/managed regressions cover oversized URI and
explicit-ID spans, occupied prefixes and complete reuse after failure; the native
rebuild and execution remain pending. Windows Terminal and xterm.js do not use this native
PAGE contract, so Ghostty defines these restore decisions. Golden hash vectors,
bitmap/set lifecycle cases, adversarial hash collisions, seeded native comparisons
and post-restore overwrite tests are added but unrun. Event-level accounting and
pressure-driven splitting for subsequent live mutations are still unfinished.

## Font thickening

`TerminalControl.FontThicken` enables macOS CoreText font smoothing for terminal
text and IME preedit, for either VT engine. `FontThickenStrength` ranges from 0
(lightest smoothing) to 255 (strongest); it has no effect while thickening is
disabled. These values are also available as `Thicken` and `ThickenStrength` in
`TerminalFontRenderingSettings` and are persisted in appearance profiles.

This follows Ghostty's macOS smoothing path: grayscale CoreText masks, linear-gray
color space, a strength-controlled gray drawing color, and padded glyph bounds.
It is independent of the existing `FontEmbolden` synthetic-bold setting. Shaping,
cell advances and colors remain controlled by RoyalTerminal. Color-font glyphs
retain the Skia renderer, and other operating systems retain normal rendering,
matching Ghostty's platform support for this setting. Glyph and font caches are
bounded, invalidated when font settings change, and disposed with the renderer.

## Kitty image storage

The managed engine retains raw RGB images at three bytes per pixel, matching
Ghostty's protocol storage. RGBA and decoded PNG use four. Rendering creates one
cached, owned RGBA view without changing the image's storage charge or eviction
generation; geometry-only reads do not expand pixels. Frame edits and composition
use copy-on-write, so an earlier render publication keeps its original pixels.

`BasicVtProcessorOptions.KittyGraphicsStorageLimitBytes` is an admission budget,
not a bound on all process memory. Like Ghostty, animation composition promotes
the root to RGBA before resolving a new frame's base or reserving its full canvas.
Promotion and existing-frame edits are quota-exempt, so retained bytes can exceed
the admission limit. Subsequent image/frame admission reclaims actual bytes using
transient/placement priority and generation order, excluding the animation target.
An append may fail after evicting other images; a deficit larger than the limit is
rejected before eviction. Frame deletion and retransmission release their stored
bytes. The native build includes a hash-checked correction to an eviction assertion
that previously excluded valid over-budget RGB promotion.

The separate `KittyGraphicsMaxImageBytes` safety setting bounds loaded/decompressed
data and the largest RGBA view before allocation. Renderer caches, temporary load
buffers and caller-retained publications are not charged to protocol storage.
Unrendered RGB payloads retain three rather than four bytes per pixel; rendered
RGB images can hold both source and cached view. No whole-application memory or
speed improvement is claimed. Allocation, ownership, quota and cross-engine tests
are included; execution and profiling are pending implementation-phase validation.

Kitty APC control fields are parsed incrementally into a fixed-size table and
an eleven-byte temporary field, following Ghostty rather than xterm.js's fixed
header-length cap. `KittyGraphicsMaxApcBytes` counts only encoded payload, not the
`G` identifier or control fields. Zero still permits control-only operations.
Payload capacity cannot exceed the configured bound; completion decodes in place
and transfers ownership. Failed/disabled commands discard subsequent bytes without
retaining a large APC buffer. Rejected commands neither cancel a previously
accepted chunked image nor change its quiet policy. Normal APC exit/cancellation,
reset and parser-continuation behavior are preserved. Focused split-input, resource
and ownership tests are added; allocation measurements and execution remain pending.

Graphics payload decoding follows Ghostty's default simdutf forgiving-base64
policy: optional final padding, ignored ASCII whitespace and unused final bits,
but rejected misplaced/excess padding and invalid alphabet characters. Full
groups use the runtime's in-place decoder; the short tail is decoded without an
extra array. This is separate from strict clipboard decoding. Pinned Ghostty's
scalar fallback has inconsistent whitespace/invalid-padding behavior; the managed
engine deliberately follows the default decoder on every platform. Differential
tests cover shared valid inputs on all native builds and whitespace on SIMD builds.

Animation ticks follow the native host's write-then-render order. Stop, gap edits,
frame uploads and screen switches within one input write execute before its next
tick; parser command boundaries do not independently advance playback or invalidate
an upload's saved image generation. DECSET 2026 is an explicit exception: it ticks
and publishes the completed prefix before freezing presentation. Releasing the hold
lets the remainder of that write run before the next tick. Idle deadlines and
external resize still refresh animations. Deleting an earlier frame preserves the
displayed frame's identity and elapsed gap, including when it was the last frame;
the reviewed native frame-deletion overlay now covers that case as well. Fake-clock
cross-engine tests and a raw-native generation regression are added but unrun.

## Kitty drag and drop

Both VT adapters implement `ITerminalDragDropTarget`. A registered OSC 72 client
receives drag movement/leave and drop messages, then requests MIME representations
by index. Chunked registration and acceptance, multiplexer IDs, base64 data chunks,
completion markers and BEL/ST responses follow the pinned Ghostty state machine.
Registration survives RIS; new sessions unregister. Held data is released on
conclusion, a new drag, cancellation, unregister or processor disposal.

Avalonia's composed drop behavior advertises plain text, file URI lists and
supported typed platform MIME data. It captures representations only at drop time,
without opening files or retrieving remote contents. The host offers copy only:
its OS drag session finishes before the client concludes the asynchronous transfer,
so it cannot safely promise a source-file move. The core contract still encodes
copy/move operations for custom hosts. Unregistered drops remain available to the
embedding application's handlers; no shell commands or unsolicited paste are generated.

Drops are bounded to 16 representations and 64 MiB. Like the pinned Ghostty core,
remote transfer requests receive `EINVAL`, and unsupported drag-out receives
`EPERM`; neither capability is advertised. Native host hooks are repository-owned
extensions, not new upstream public C APIs. The native boundary copies all retained
data and serializes with normal terminal mutations.

## Kitty desktop notifications

Both VT adapters implement `ITerminalNotificationSource`; embedders can configure
`TerminalControl.NotificationHost` with a nonblocking `ITerminalNotificationHost`.
OSC 99 queries advertise only the backend's actual capabilities. Without a host,
notifications and support queries are silently ignored. The default application
now installs Linux freedesktop, macOS UserNotifications and Windows toast backends
for live sessions, shared by both engines; capture replay never installs a desktop
presenter. New platform implementations await runtime sign-off;
this is not a claim of verified desktop delivery.

The shared protocol implements chunked title/body/buttons/icon assembly, strict
safe UTF-8 and base64 input, application/type metadata, occasions, urgency, sound,
replacement identities, activation/close/alive replies and monotonic expiry.
Strings remain plain text, including literal markup. Encoded text preserves
newlines/tabs and removes other control codes. Callback feedback is consumed only
during serialized terminal refresh; it cannot write directly to the PTY from an
OS callback thread. Superseded and prior-session callbacks are ignored. Effects
remain responsive during synchronized-output holds without releasing the frame.

Limits are 64 unfinished and 64 active notifications, 4 MiB retained assembly
and request payload budgets, 64 KiB per text field, 1 MiB per icon, 32 names/types
or buttons, and a 128-entry/16 MiB session-local icon LRU. Individual OSC payloads
follow Ghostty's 2048-byte plain / 4096-byte encoded limits; metadata is capped at
8192 bytes and IDs at 256 ASCII identifier characters. Input never becomes an
arbitrary file path, shell command, or OS notification identity. Resource names
are restricted to single identifiers resolved inside configured local XDG roots.
Hosts must separately
bound native image decoding and their own queues. RIS clears unfinished chunks;
session changes, host replacement, detachment and disposal close owned revisions.

The Linux backend uses explicit D-Bus serialization and the negotiated desktop
service capabilities; no reflection proxy, shell process or libnotify dependency.
It binds calls/signals to a unique daemon owner, fails stale revisions on daemon
restart and retries connection initialization. A per-window async worker owns at
most 128 requests / 8 MiB of retained payload. Close is recorded as state even
when admission is full. In-flight delivery finishes before cleanup so its returned
OS ID can be closed; window closure waits asynchronously for this owned cleanup.
Transport operations are bounded, and no desktop call waits under the VT lock.

Per-pane facades cache focus/visibility on the UI thread and route activation to
the originating tab/pane. Queued focus is invalidated on session/host teardown.
Bodies are escaped only when the server supports markup; literal title text is
preserved, and servers without body support receive the body in the summary.
Images decode only their first PNG/JPEG/GIF frame, with 1 MiB encoded, 2048-pixel
per-axis and 1-megapixel decoded limits. All standard icon aliases and ordered
custom names use local XDG themes, inheritance, size/scale selection and unthemed
fallbacks. Locally installed desktop entries supply application icons; their
commands are never executed and caller-supplied paths/URLs are rejected. Explicit
names precede transmitted images; the application name is an implicit icon only
when neither names nor image data is supplied. Icons are advertised when the
daemon reports static or animated icon support; only the first frame is sent.

Theme roots follow XDG precedence, with GNOME GSettings, KDE and GTK configuration
sources for theme selection and hicolor/freedesktop defaults. Optional GLib
settings access uses explicit native imports, not GTK initialization or a shell.
Theme documents, lookup work and caches are bounded; worker-side refresh observes
file/theme changes on subsequent requests after five seconds. Standard and local
sound names follow sound-theme inheritance, locale/profile and generic-name
fallbacks, including user `.disabled` overrides. Both `sound-name` and resolved
`sound-file` are supplied: the server's `sound` capability guarantees the latter,
not the former. All standard sounds are advertised only when available locally
(or explicitly disabled by the user); otherwise only system/silent are advertised.

Wayland activation-token integration remains unfinished. Avalonia 12.1.1's
[native Wayland activation is a no-op](https://github.com/AvaloniaUI/Avalonia/blob/12.1.1/src/Avalonia.Wayland/WindowImplBase.cs)
and exposes no token-aware activation feature. Linux focus capability is therefore
advertised only for an actual X11/XWayland window handle, not inferred from session
environment variables. Native Wayland delivery/reporting remains usable without
claiming that clicking a notification can focus the originating terminal.

New tests cover backend negotiation, replacement, early/stale signals, queue
saturation, in-flight close/shutdown, image limits and headless pane lifecycle.
Additional tests cover XDG root/name/theme order, desktop-entry masking, all icon
and sound aliases, locale and disabled sounds, cache invalidation, bounded lookup,
path traversal rejection and backend-specific focus capability.
An isolated loopback D-Bus peer exercises actual message serialization without
contacting the user's desktop bus. These tests have not yet been executed.

Reference decision: the pinned Ghostty OSC 99 implementation supplies a parser
but no stream/desktop delivery. Windows Terminal's OSC dispatcher has no OSC 99
handler and xterm.js exposes host registration through `registerOscHandler`.
RoyalTerminal follows the [Kitty desktop notification protocol](https://sw.kovidgoyal.net/kitty/desktop-notifications/)
for the shared host lifecycle. Three reviewed native overlays expose the parsed
command without adding an upstream action enum value. Callback, protocol and
headless lifecycle tests have been added; execution awaits full validation.
Linux resource resolution follows the [icon theme specification](https://specifications.freedesktop.org/icon-theme/latest/)
and [sound theme specification](https://specifications.freedesktop.org/sound-theme/latest-single/).

The macOS presenter uses a separate universal arm64/x64 host bridge bundled in the
Avalonia application package; it does not depend on the selected VT engine or add
Ghostty VT exports. Apple UserNotifications requires a real `.app` bundle with a
bundle identifier. Missing native assets, unbundled command-line hosts, denied
authorization and a notification-center delegate owned by another embedding host
leave support unavailable. Initialization and support queries never prompt for
permission: only a real delivery can request alert/sound authorization.

Source-generated JSON carries bounded requests to a native per-client owner. Native
completion blocks never call managed function pointers. An owned managed poller
reads completion/activation and delivered-notification state, so callbacks from
superseded requests or previous sessions cannot activate the current pane. Delivered
replacements reuse their OS ID with a new revision token; a still-pending replacement
gets a different ID so late cancellation cannot remove its successor. Categories
retain other applications' registered categories and are removed on owner teardown.
Actions preserve button indices; the shared protocol decides whether to focus the
originating pane. The OS can still activate the application for its default action.

macOS icon names use standard system symbols or application bundle icons, in order,
then bounded single-frame PNG data. Attachment preparation/file writes run off the
AppKit queue and use generated private temporary directories, removed on completion
or cancellation. Only system/silent sounds are advertised, matching the upstream
macOS presenters. Low urgency is passive; normal/high preserve ordinary notification
policy without requesting a critical-alert entitlement. All-close-event and full
urgency capability flags are deliberately absent. Delivered-state polling provides
an eventually refreshed alive cache (two missing observations after a delivery
grace period); `c=1` receives the protocol's `untracked` response.

Closing a pane cancels its outstanding delivery wait without stopping other panes;
one native authorization wait serves the remaining requests without retaining the
cancelled payloads. Window close cancels all waits and awaits native cleanup.
An OS add completion that exceeds the bounded shutdown wait remains natively owned
and removes its own late request, without retaining a managed callback or handle.
Objective-C tests use a fake center for replacement/cancellation/delegate lifetimes;
managed tests use a fake transport. The universal bridge build and native tests are
wired into macOS CI and its artifact into cross-platform NuGet packaging. Build,
test execution, package inspection and real permission/activation sign-off remain
pending until the full validation phase.

The Windows presenter uses a separate C++/WinRT host bridge for x64 and arm64,
sharing the source-generated command transport, managed ownership and cancellation
with macOS. Its native actor owns the COM apartment, notification objects, event
subscriptions and local image files. No managed callbacks, reflection, PowerShell,
Windows App SDK runtime, shell execution or remote image URLs are used. Disabled
notification settings and unavailable native assets suppress advertised support.

Packaged hosts use their package identity. Unpackaged hosts register a per-user
Start Menu shortcut with a stable executable-path-derived AUMID, following the
[desktop toast identity contract](https://learn.microsoft.com/windows/win32/shell/enable-desktop-toast-with-appusermodelid).
The shortcut points only to the actual host executable, never to terminal data;
an existing nonmatching shortcut is not overwritten. Generic `dotnet`/test hosts
are excluded. The registration persists for Windows notification settings. It does
not install a COM activator or URI protocol handler, and does not alter taskbar pins
or the host's process-wide AppUserModelID. Installer-managed identity and interactive
desktop behavior require platform sign-off.

The design follows Windows Terminal's `DesktopNotification.cpp`: activation
callbacks belong to live toast objects, and tag/group identities support replacement.
Each native client owns a unique group, so windows do not clear each other's toasts.
Retired tokens cannot focus replacement sessions. XML is built through DOM text and
attribute setters; opaque button arguments cannot contain terminal-provided commands.
Windows supports at most five buttons; the first five retain their original indices.
Ordered stock/application icon names use a fixed local shell namespace, followed by
bounded normalized PNG input. Private, generated image files live only as long as the
native toast lease. No arbitrary caller path is opened. System/silent sounds are
supported; named sound support is not advertised. Low urgency suppresses the popup,
normal uses default priority, and high requests high priority without bypassing Focus
Assist or system notification policy.

Because Windows can launch a second host instance in addition to delivering an
in-process activation, embedders should call
`TerminalNotificationLaunch.IsInertActivation(args)` before creating their desktop
lifetime, and exit for this inert sentinel. The demo entry point does this. Closing
a notification cannot reopen a terminal or replay a command. Delivered-history polling
maintains an eventually refreshed alive cache; full close-event support is not claimed.
Shutdown revokes handlers, removes owned toasts and releases images. A stalled OS RPC
can outlive the bounded managed wait under native-only ownership; the bridge remains
loaded so eventual cleanup is safe.

Windows bridge source builds require Visual C++ build tools (x64 and ARM64) and the
Windows SDK, via `scripts/build-windows-notifications.ps1`. CI builds both architectures
and has a fake-platform native lifecycle harness plus cross-platform managed tests;
these tests do not register shortcuts or show desktop notifications. CI and release
packaging include both Windows DLLs and the universal macOS dylib. All new build,
test, packaging and real Windows activation/sign-off work awaits the full validation
phase; source implementation is not evidence of platform success.

## Ghostty-compatible shaders

RoyalTerminal also supports a Ghostty/Shadertoy-style shader compatibility mode in the managed Skia renderer. This is intentionally separate from the native Ghostty VT binding and from Ghostty renderer interop.

Use `TerminalShaderLanguage.GhosttyShadertoy` when you have a single-pass `mainImage` shader that samples `iChannel0`:

```csharp
using RoyalTerminal.Shaders;

Terminal.ShaderSources =
[
    new TerminalShaderSource(
        "Ghostty Compatible Shader",
        ghosttyStyleSource,
        TerminalShaderLanguage.GhosttyShadertoy)
];
```

The shader runs as a RoyalTerminal framebuffer post-process effect, so it works regardless of whether the active VT engine is managed or Ghostty-backed. Native Ghostty renderer `custom-shader` injection is not part of the current interop path.

See [Ghostty/Shadertoy Shader Compatibility](/articles/shaders-ghostty-shadertoy) for the supported uniforms, source shape, and limitations.

## Start with the high-level wrappers

If you want Ghostty behavior inside RoyalTerminal, you usually begin with the managed wrapper types instead of the raw ABI mirror.

| Type | Purpose |
| --- | --- |
| `GhosttyTerminal` | Managed lifetime wrapper for the native terminal. |
| `GhosttyRenderState` | Managed wrapper for render-state extraction from the native terminal. |
| `GhosttyFormatterScreenOptions` | Screen-level formatter options. |
| `GhosttyFormatterExtraOptions` | Formatter extras for palette, modes, tabstops, keyboard state, and screen details. |
| `GhosttyFormatterOptions` | High-level formatter request model. |
| `GhosttyFormatter` | Formatter for exporting terminal state. |
| `GhosttyKeyEncoder` | Native-backed key encoder. |
| `GhosttyKeyEvent` | Mutable key event payload. |
| `GhosttyMouseEncoder` | Native-backed mouse encoder. |
| `GhosttyMouseEvent` | Mutable mouse event payload. |
| `GhosttyPaste` | Static helper for paste encoding. |
| `GhosttySelection` | Managed selection value used by formatters and helpers. |
| `GhosttySelectionGesture` | Owned state machine for native text-selection gestures. |
| `GhosttySelectionGestureEvent` | Reusable selection pointer/click event. |
| `GhosttyTrackedGridReference` | Owned reference that follows a cell through scroll, pruning, and reflow. |
| `GhosttyColorUtilities` | Native-backed color parsing, palettes, color math, X11 names, and color-scheme reports. |
| `GhosttyUnicode` | Ghostty-exact codepoint and grapheme width helpers. |
| `GhosttyKittyGraphics` | Managed helper for Kitty graphics extraction. |
| `GhosttyKittyGraphicsImage` | Managed Kitty image snapshot. |
| `GhosttyKittyGraphicsPlacementIterator` | Managed iterator over Kitty placements. |
| `GhosttySys` | Static system helper surface. |
| `GhosttyVtHelpers` | Static helper surface for protocol encoders and build info. |
| `GhosttyBuildInfoSnapshot` | Full native build metadata snapshot. |
| `GhosttyBuildFeatures` | Compact native build capability snapshot. |
| `TerminalBuffer` | Managed helper for reading terminal content. |
| `TerminalDataProcessor` | Static helper for processing terminal data through Ghostty-backed models. |
| `NativeLibraryLoader` | Native library loader for `libghostty-vt`. |

These are the types used by RoyalTerminal itself when it wants native VT behavior without forcing consumers to work directly against raw pointers and C structs.

## Engine-neutral effects and Unicode

Hosts that can use either VT engine should query the terminal contracts instead
of depending directly on Ghostty types:

| Contract | Capability |
| --- | --- |
| `ITerminalEffectSource` | Clipboard writes, desktop notifications, progress reports, and working-directory changes. |
| `ITerminalUnicodeWidthProvider` | Codepoint width and first-grapheme width using the active engine's rules. |

`GhosttyVtProcessor` implements these contracts with the normalized
libghostty callbacks and Ghostty Unicode tables. `BasicVtProcessor` implements
the same contracts with managed OSC parsing and RoyalTerminal Unicode tables.
The effect callbacks are policy boundaries: applications still decide whether
clipboard writes and desktop notifications are allowed.

## The raw VT mirror is also public

Under those wrappers, `GhosttyVtNative` exposes the Ghostty VT ABI directly. This is not the right layer for most applications, but it is the right layer for advanced interop, diagnostics, or custom wrappers.

At the current Ghostty revision, `TerminalNew` takes `columns` and `rows`
directly. Configure the returned terminal through `TerminalSet`; the former
`GhosttyTerminalOptions` constructor struct no longer exists.

### Root VT exports

`GhosttyVtNative`, `GhosttyResult`, `GhosttyVtKeyAction`, `GhosttyVtKey`, `GhosttyVtMods`, `GhosttyOscCommandType`, `GhosttyOscCommandData`, `GhosttySgrAttributeTag`, `GhosttySgrUnderline`, `GhosttySgrUnknown`, `GhosttySgrAttributeValue`, `GhosttySgrAttribute`, `GhosttyColorRgb`

### Core protocol and build-info exports

`GhosttyString`, `GhosttyMode`, `GhosttyModeReportState`, `GhosttyOptimizeMode`, `GhosttyBuildInfoData`, `GhosttyFocusEvent`, `GhosttySizeReportStyle`, `GhosttySizeReportSize`, `GhosttyDeviceAttributesPrimary`, `GhosttyDeviceAttributesSecondary`, `GhosttyDeviceAttributesTertiary`, `GhosttyDeviceAttributes`, `GhosttyPointCoordinate`, `GhosttyPointTag`, `GhosttyPoint`

### Formatter exports

`GhosttyFormatterFormat`, `GhosttyFormatterScreenExtra`, `GhosttyFormatterTerminalExtra`, `GhosttyFormatterTerminalOptions`

### Input and pointer exports

`GhosttyKittyKeyFlags`, `GhosttyOptionAsAlt`, `GhosttyKeyEncoderOption`, `GhosttyMouseAction`, `GhosttyMouseButtonId`, `GhosttyMousePosition`, `GhosttyMouseTrackingMode`, `GhosttyMouseFormat`, `GhosttyMouseEncoderSize`, `GhosttyMouseEncoderOption`

### Kitty graphics exports

`GhosttyKittyGraphicsData`, `GhosttyKittyGraphicsPlacementData`, `GhosttyKittyPlacementLayer`, `GhosttyKittyGraphicsPlacementIteratorOption`, `GhosttyKittyImageFormat`, `GhosttyKittyImageCompression`, `GhosttyKittyGraphicsImageData`

### Render-state exports

`GhosttyRenderStateDirty`, `GhosttyRenderStateCursorVisualStyle`, `GhosttyRenderStateData`, `GhosttyRenderStateOption`, `GhosttyRenderStateRowData`, `GhosttyRenderStateRowOption`, `GhosttyRenderStateRowCellsData`, `GhosttyRenderStateColors`, `GhosttyRenderStateRowSelection`, `GhosttyBuffer`

### Screen exports

`GhosttyStyleColorTag`, `GhosttyStyleColorValue`, `GhosttyStyleColor`, `GhosttyStyle`, `GhosttyGridRef`, `GhosttyCellContentTag`, `GhosttyCellWide`, `GhosttyCellSemanticContent`, `GhosttyCellData`, `GhosttyRowSemanticPrompt`, `GhosttyRowData`

### Selection, system, and terminal exports

`GhosttySelectionRange`, `GhosttySelectionOrder`, `GhosttySelectionAdjust`, `GhosttySelectionGestureBehavior`, `GhosttySelectionGestureBehaviors`, `GhosttySelectionGestureGeometry`, `GhosttySelectionGestureAutoscroll`, `GhosttySelectionGestureData`, `GhosttySelectionGestureEventType`, `GhosttySelectionGestureEventOption`, `GhosttyAllocatorVtable`, `GhosttyAllocator`, `GhosttySysImage`, `GhosttySysDecodePngCallback`, `GhosttySysOption`, `GhosttyTerminalScrollViewportTag`, `GhosttyTerminalScrollViewportValue`, `GhosttyTerminalScrollViewport`, `GhosttyTerminalCompressionMode`, `GhosttyTerminalCompressionResult`, `GhosttyTerminalScreen`, `GhosttyTerminalScrollbar`, `GhosttyTerminalBellCallback`, `GhosttyTerminalWritePtyCallback`, `GhosttyTerminalTitleChangedCallback`, `GhosttyTerminalEnquiryCallback`, `GhosttyTerminalXtversionCallback`, `GhosttyTerminalSizeCallback`, `GhosttyTerminalColorSchemeCallback`, `GhosttyTerminalDeviceAttributesCallback`, `GhosttyTerminalPwdChangedCallback`, `GhosttyTerminalClipboardWriteCallback`, `GhosttyTerminalDesktopNotificationCallback`, `GhosttyTerminalProgressReportCallback`, `GhosttyTerminalOption`, `GhosttyTerminalData`

## Runtime enums, structs, and callbacks

The public Ghostty surface also includes the broader runtime enum and action/config mirror types that are not part of `GhosttyVtNative` itself.

### Runtime enums

`GhosttyPlatform`, `GhosttyClipboard`, `GhosttyClipboardRequest`, `GhosttyMouseState`, `GhosttyMouseButton`, `GhosttyMouseMomentum`, `GhosttyColorScheme`, `GhosttyMods`, `GhosttyBindingFlags`, `GhosttyInputAction`, `GhosttyKey`, `GhosttyInputTriggerTag`, `GhosttyBuildMode`, `GhosttyPointTag`, `GhosttyPointCoord`, `GhosttySurfaceContext`, `GhosttyTargetTag`, `GhosttySplitDirection`, `GhosttyGotoSplit`, `GhosttyGotoWindow`, `GhosttyResizeSplitDirection`, `GhosttyGotoTab`, `GhosttyFullscreen`, `GhosttyFloatWindow`, `GhosttySecureInput`, `GhosttyInspectorAction`, `GhosttyQuitTimer`, `GhosttyReadonly`, `GhosttyPromptTitle`, `GhosttyMouseShape`, `GhosttyMouseVisibility`, `GhosttyRendererHealth`, `GhosttyColorKind`, `GhosttyOpenUrlKind`, `GhosttyCloseTabMode`, `GhosttyProgressState`, `GhosttyQuickTerminalSizeTag`, `GhosttyKeyTableTag`, `GhosttyActionTag`, `GhosttyIpcTargetTag`, `GhosttyIpcActionTag`

### Runtime structs

`GhosttyClipboardContent`, `GhosttyInputKey`, `GhosttyInputTriggerKey`, `GhosttyInputTrigger`, `GhosttyCommand`, `GhosttyInfo`, `GhosttyDiagnostic`, `GhosttyString`, `GhosttyText`, `GhosttyPoint`, `GhosttySelection`, `GhosttyEnvVar`, `GhosttyPlatformMacOS`, `GhosttyPlatformIOS`, `GhosttyPlatformUnion`, `GhosttySurfaceConfig`, `GhosttySurfaceSize`, `GhosttyConfigColor`, `GhosttyConfigColorList`, `GhosttyConfigCommandList`, `GhosttyConfigPalette`, `GhosttyQuickTerminalSizeValue`, `GhosttyQuickTerminalSize`, `GhosttyConfigQuickTerminalSize`, `GhosttyTargetUnion`, `GhosttyTarget`, `GhosttyResizeSplit`, `GhosttyMoveTab`, `GhosttySizeLimit`, `GhosttyInitialSize`, `GhosttyCellSize`, `GhosttyDesktopNotification`, `GhosttySetTitle`, `GhosttyPwd`, `GhosttyMouseOverLink`, `GhosttyKeySequence`, `GhosttyKeyTableActivate`, `GhosttyKeyTableValue`, `GhosttyKeyTable`, `GhosttyColorChange`, `GhosttyConfigChange`, `GhosttyReloadConfig`, `GhosttyOpenUrl`, `GhosttyChildExited`, `GhosttyProgressReport`, `GhosttyCommandFinished`, `GhosttyStartSearch`, `GhosttySearchTotal`, `GhosttySearchSelected`, `GhosttyScrollbar`, `GhosttyActionValue`, `GhosttyAction`, `GhosttyRuntimeConfig`

### Runtime delegates

`GhosttyWakeupCallback`, `GhosttyActionCallback`, `GhosttyReadClipboardCallback`, `GhosttyConfirmReadClipboardCallback`, `GhosttyWriteClipboardCallback`, `GhosttyCloseSurfaceCallback`

## Which layer should you choose?

Use the layers in this order:

1. stay in the main RoyalTerminal packages if all you need is a terminal control, VT processor, or renderer
2. use the high-level GhosttySharp wrappers if you need native terminal behavior directly
3. drop to `GhosttyVtNative` and the runtime mirror types only if you are building new interop or diagnostics on top of Ghostty itself

That keeps the common path small while still preserving the full native escape hatch for advanced consumers.
