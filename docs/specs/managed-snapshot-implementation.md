# Managed Ghostty snapshot compatibility work

Status: **incomplete**. The implemented framing, metadata and continuation pieces
do not yet constitute an import/export API for a managed terminal.

The compatibility target is Ghostty's version 1 `GHOSTSNP` wire format at
`22391ed6491f`, rather than a separate RoyalTerminal-only dump. Upstream
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

Tests read the pinned upstream golden fixtures and check every truncation boundary,
malformed fields, byte-for-byte re-encoding and allocation-free borrowed reads.
They do not assert that a fully decoded managed terminal already exists.

## Required storage changes before a complete adapter

`TerminalCell` now retains four-byte logical foreground/background/underline
identities alongside resolved ARGB. Both VT integrations populate these from
original styles, with focused print/erase/save/restore/wide/reflow/hold tests.
This removes the need to guess whether identical displayed RGB values came from
default, indexed or explicit colors. The snapshot adapter still needs to map
these identities to its style records. Remaining legacy existing-cell theme
remapping and wide-boundary normalization paths need review before claiming all
state transitions preserve native semantics.

The wire format also preserves protected cells, semantic cell content, row semantic
prompt and wrap-continuation flags. The current managed model lacks some of these
fields. A complete adapter must implement their runtime semantics and tests,
not merely deserialize values that are then silently dropped. Wide spacer-head
and spacer-tail distinctions likewise must survive export/import.

Hyperlink IDs on the wire include an explicit arbitrary-byte ID or an implicit
numeric ID in addition to the URI. The existing screen URL-to-token registry is
not sufficient to retain all of that identity. Preserve identity at the domain
boundary while keeping presentation URL access convenient.

## Remaining codec and integration order

1. PAGE/grid codecs: dimension and allocation limits, first-entry-wins table
   semantics, reference remapping, four compact cell widths, canonical trailing
   zero elision, malformed wide-pair normalization, and bounded suffix decoding.
2. SCREEN/current and saved cursor state, pen, charsets, mouse/keyboard state and
   both primary/alternate grids; TERMINAL modes, tab stops, theme overrides,
   dimensions, title/PWD and remaining format-defined terminal state.
3. Complete snapshot encoder/decoder with strict record ordering. READY exposes
   a usable terminal before optional HISTORY, whose pages arrive newest first.
   Incremental history ingestion must remain safe if live input, reset or resize
   occurs after READY, matching upstream's reconciliation rules.
4. Managed processor adapter and public ownership/error contracts. A partial
   decode must not overwrite the caller's existing terminal on failure. Parser
   continuation must resume byte-for-byte across UTF-8 and control-string splits.
5. Native-to-managed and managed-to-native differential tests, including every
   upstream complete fixture, both screens, history, pending wrap, saved cursors,
   palette/RGB identity, malformed inputs and streaming IO failures. Benchmark
   sparse/plain/styled/grapheme-heavy histories separately from renderer work.

Snapshot version 1 intentionally omits Kitty image/placement storage and downloaded
glyph glossary registrations. It preserves Kitty placeholder text. This is an
upstream format limit, not a reason to omit other representable state or to claim
that graphics survive snapshot restoration.
