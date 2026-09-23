# Managed Ghostty snapshot compatibility work

Status: **incomplete**. The implemented framing, metadata and continuation pieces
do not yet constitute an import/export API for a managed terminal.

The compatibility target is Ghostty's version 1 `GHOSTSNP` wire format at
`4ae9f1a2de5484de3d6a13fe03676b8853b9c41c`, rather than a separate RoyalTerminal-only dump. Upstream
`src/terminal/snapshot/main.zig` and the per-record codecs are the authoritative
format definitions. Windows Terminal and xterm.js do not offer this same wire
contract; their screen/serialization mechanisms are not interchangeable codecs.

## Implemented and tested

- The 10-byte envelope and 10-byte tag/length/CRC32C record header, including
  rejection of unknown tags, oversize payloads and non-empty checkpoints.
- Borrowed in-memory record payloads and a pooled streaming reader. Reader state
  becomes unusable after corruption. FINISH stops before trailing transport data;
  disposing a decoder does not dispose the caller's stream.
- Fixed 16-byte unresolved style records. Default, indexed and RGB colors remain
  distinct. Structural truncation fails; complete but invalid optional style
  entries can be discarded by PAGE decoding.
- Length-delimited implicit/explicit hyperlink entries, preserving arbitrary
  bytes rather than assuming UTF-8. Invalid empty strings are consumed as a whole
  entry; an unknown kind or truncated length/data is a structural failure.
- Managed parser ground checks, bounded continuation capture, streaming export
  and processing through the next ground boundary. The export cap defaults to
  64 KiB, independently of OSC/clipboard limits.
- Complete PAGE/grid payloads: four compact cell widths, canonical trailing zero
  elision, wide-pair/scalar/semantic normalization, bounded UTF-32 suffixes,
  first-entry-wins style/hyperlink tables and reference resolution. Capacity hints
  are metadata, not allocation requests. Golden and native round trips cover both
  screens and styled history; streaming re-encoding allocates zero bytes when warm.
- Complete TERMINAL payloads: all 103 header bytes, current/saved/default modes,
  per-axis margin normalization, screen routing, input/cursor policies, dynamic
  colors, source scrollback policies, tab bits, original/sparse override palettes,
  and arbitrary-byte PWD/title. Strings have an aggregate byte limit checked before
  copying; dimensions are bounded before constructing state. Reserved values use
  upstream defaults, including optional booleans and absent colors. Golden bytes,
  truncations, limits, native installation and malformed semantic headers have
  focused coverage. Warm streaming re-encoding allocates zero bytes.
- Complete SCREEN payloads: current and saved cursor pens/flags/charsets, cursor
  style and independent implicit hyperlink counter, protected mode, all eight
  Kitty keyboard stack entries, semantic-click state, advisory history extent
  and optional arbitrary-byte cursor hyperlink. All nonzero saved-cursor presence
  bytes consume the suffix. Malformed final hyperlinks are discarded at the
  record boundary as upstream permits; valid links and null markers reject
  trailing bytes. Cursor installation helpers distinguish physical page width
  from terminal width. Native differential cases cover later printing/restore
  and 100 malformed semantic records; all 65,536 charset words are checked.
- The fixed HISTORY header, including zero-page sequences and key/count/resource
  validation. This routes a later sequence, not a request to allocate its declared
  number of pages.
- Ordered decoding transactionally through READY, then one owned history PAGE at
  a time through FINISH. Declared screen keys are unique in each group and may
  arrive in either order; active pages must cover terminal height but may have
  mixed physical widths. Failed reads poison the decoder without invalidating
  already-owned READY state. Caller streams stay open and trailing transport bytes
  remain unread. Cell/page/payload-byte budgets are aggregate, while string/suffix
  limits are per-record/per-page; source scrollback policies are not these limits.
- Snapshot continuation validation is a pure, non-executing scan matching upstream
  minimal/unfinished/side-effect-free rules, including UTF-8 bounds, ignored string
  controls, DCS C1 payload overrides and APC termination effects. It is independent
  of BasicVtProcessor, so parsing untrusted input cannot replay terminal callbacks.
  All 5,106 native differential cases agree; warmed validation allocates zero bytes.
- Combined transcoding through all managed codecs preserves the complete golden
  fixture exactly. Live native snapshots with 100 styled history lines, Unicode,
  hyperlinks, either screen and unfinished CSI round-trip through the ordered
  reader and native restoration, retaining behavior after completing the input.

Tests read the pinned upstream golden fixtures and check every truncation boundary,
malformed fields, byte-for-byte re-encoding and allocation-free borrowed reads.
They do not assert that a fully decoded managed terminal already exists.

## Required storage changes before a complete adapter

`TerminalCell` now retains four-byte logical foreground/background/underline
identities alongside resolved ARGB. Both VT integrations populate these from
original styles, with focused print/erase/save/restore/wide/reflow/hold tests.
This removes the need to guess whether identical displayed RGB values came from
default, indexed or explicit colors. The snapshot adapter still needs to map
these identities to its style records. Existing-cell theme resolution now uses
the logical identities, with native/managed collision and underline regressions.
Wide-edge printing, overwrite and erase paths now have focused differential
coverage; broader state-transition coverage is still required.

The wire format also preserves protected cells, semantic cell content, row semantic
prompt and wrap-continuation flags. The current managed model lacks some of these
fields. A complete adapter must implement their runtime semantics and tests,
not merely deserialize values that are then silently dropped. Wide spacer-head
and spacer-tail distinctions are now explicit in `TerminalCell.IsWideSpacerHead`
without increasing the 48-byte cell budget; the complete codec still needs to
preserve them through export/import.

Hyperlink IDs on the wire include an explicit arbitrary-byte ID or an implicit
numeric ID in addition to the URI. The existing screen URL-to-token registry is
not sufficient to retain all of that identity. Preserve identity at the domain
boundary while keeping presentation URL access convenient.

## Remaining codec and integration order

1. READY installation into a usable managed terminal and incremental history
   reconciliation. The ordered wire reader is implemented but does not yet apply
   pages to a live screen. History ingestion must remain safe if live input, reset
   or resize occurs after READY, matching upstream generation/width/limit checks
   and dropping the remaining older pages after the first gap.
2. Managed processor adapter and public ownership/error contracts. PAGE/grid,
   TERMINAL, SCREEN and HISTORY payload decoding/re-encoding are implemented;
   constructing semantic records from live managed state and installing them in
   both managed screens still belong to the adapter, rather than replaying their
   original bytes. A partial
   decode must not overwrite the caller's existing terminal on failure. Parser
   continuation must resume byte-for-byte across UTF-8 and control-string splits.
   A native-valid wire continuation is not proof that the current managed parser
   supports every corresponding state: its CSI/control/ignore-state handling and
   current/saved charset semantics need integration coverage before exposure.
3. Native-to-managed and managed-to-native differential tests, including every
   upstream complete fixture, both screens, history, pending wrap, saved cursors,
   palette/RGB identity, malformed inputs and streaming IO failures. Benchmark
   sparse/plain/styled/grapheme-heavy histories separately from renderer work.

Snapshot version 1 intentionally omits Kitty image/placement storage and downloaded
glyph glossary registrations. It preserves Kitty placeholder text. This is an
upstream format limit, not a reason to omit other representable state or to claim
that graphics survive snapshot restoration.
