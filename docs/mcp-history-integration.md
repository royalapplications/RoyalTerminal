# RoyalTerminal MCP history handoff

RoyalTerminal 0.6.2 supplies owned physical-row snapshots. The RoyalTerminal
plugin exposes them through the generic `observe_session` operation with
observation ID `terminal.history` and format `application/json`. Existing SDK
interfaces and shell permission categories remain unchanged.

## Request and response

Parameters are an object with optional `token`, `offset` and `limit`.
A missing/null token starts a capture and requires offset zero. Offset defaults
to zero and is snapshot-relative; limit defaults to 100 and accepts 1–500 rows.
Unknown fields, wrong types, negative offsets, invalid tokens and unsupported
formats are rejected before capture. Tokens are opaque 43-character base64url
values produced from 32 cryptographically random bytes.

The owned JSON projection has this shape:

```json
{
  "token": "<opaque snapshot token>",
  "offset": 0,
  "lines": ["oldest retained physical row", "", "next row"],
  "nextOffset": 3,
  "truncated": true
}
```

`nextOffset` is null at the end. Supply the same token and the next offset in
the next request. An offset exactly at the captured end returns an empty terminal
page with no continuation. Rows retain oldest-to-newest ordering, blank rows,
written spaces and grapheme content; soft wraps are not unwrapped.

Each initial capture retains the newest 2,000 physical rows of the active buffer,
including live screen rows, within 262,144 UTF-16 units including LF separators.
The outer observation's `CapturedAt` remains the original capture time.
Its `Truncated` and the projection's `truncated` describe capture retention,
not ordinary pagination. `ContinuationToken` is supplied only while another
page remains. Typed `ReadHistoryAsync` uses the same capture/page service and
maps row text to `TerminalHistoryPage.Lines`.

## Bounds, lifetime and access

Responses honor `MaxBytes`, capped at 1 MiB. Pagination reserves 1,024 metadata
bytes plus three bytes per requested row, then allows one UTF-16 unit per six
remaining bytes. A bounded UTF-8 stream verifies actual serialized bytes. Rows
are complete: oversized required rows fail; JSON and graphemes are never split.

The plugin retains at most four captures per adapter owner, 64 globally and
16 MiB of retained UTF-16 row text. Oldest captures are evicted on insertion.
Expiration is fixed at five minutes and never extended by reads. Lookups enforce
expiration; a 30-second sweep timer exists only while entries remain.

Entries retain owned snapshots, opaque owner identity, lifecycle epoch and time
metadata. They retain no control, processor, transport or invocation authorization.
Connect/reconnect start, failed/cancelled connect, disconnect, terminal exit and
adapter disposal invalidate that owner's tokens. Display rehosting preserves
tokens. Expired, evicted, foreign and obsolete tokens fail without recapturing.

Discovery checks readiness and history metadata without capturing. Capture runs
through the control's locked UI-thread forwarding. Authorization and cancellation
are rechecked before dispatch, after dispatch, at capture and before response.
The shell additionally enforces exact generation/client/catalog identity and
Enabled + Observe consent, visibility, Execute and McpObserve. Control consent
does not grant history access. Unsupported native sources use unsupported-operation
handling; unavailable and budget failures use sanitized operation failures.

## Staging and verified scope

The local feed is
`D:/src/RoyalApps/RoyalTerminal/artifacts/mcp-history-feed`.
All eight existing DesktopClient RoyalTerminal pins are 0.6.2; no dependency or
project reference was added. Thirty packages were staged. The six VT libraries
were rebuilt from the history extension source; unchanged renderer libraries and
the macOS PTY launcher were reused from the existing 0.6.1 artifacts.
Local Linux/macOS VT builds disable SIMD. Windows x64 uses the release CPU
baseline and passed the AVX/VEX disassembly check.

`manifest.json` records package and native SHA-256 hashes.
`scripts/verify-history-packages.py` verifies matching dependencies, all six
packaged history-export sets and byte equality with the isolated consumer cache.
The final consumer cache is
`D:/src/RoyalApps/RoyalTerminal/artifacts/mcp-history-consumer-cache`.

Verified locally on Windows ARM64:

- RoyalTerminal: 45 managed/native/Headless history tests; native ABI tests;
  older 0.6.1 native library explicitly reports Unsupported.
- DesktopClient: 280 full-plugin tests passed (four explicit cases not run), including
  34 targeted plugin checks against the final staged packages,
  including managed/native observation and continuation, retention, expiration,
  eviction, JSON byte limits, cancellation and revocation.
- SDK: 11 contract regression checks.
- Shell: 141 generic MCP regression checks and authenticated generic transport
  cases including terminal.history; affected projects build without warnings.

Windows x64, Linux x64/ARM64 and macOS x64/ARM64 binaries were cross-built and
their packaged exports verified; execution on those platforms is not claimed.
CI/release now explicitly run history contracts, native ABI tests and export
checks. Real remote-session acceptance through the combined native plugin and
authenticated transport remains required before public release; local backend,
shell policy and transport tests exercise those boundaries separately.

The broader RoyalTerminal unit/integration restore remains blocked by uncached
ReactiveUI.Reactive 26.0.1, ReactiveUI.Avalonia.Reactive 12.1.6, SshNet.Agent
2026.0.0 and coverlet.collector 10.1.0. Settings and SSH-agent packages are not
part of this staged consumer feed. No public publication, commits or pushes were
performed.
