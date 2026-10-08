# Bounded history snapshots

`ITerminalHistorySnapshotSource` is an optional capability implemented by the
managed and Ghostty processors. `TerminalControl.GetHistoryBufferInfo` and
`CaptureHistory` forward it on the UI thread under `TerminalScreen.SyncRoot`.
Other processors return `Unsupported`; an uninitialized control returns
`BufferUnavailable`. Old Ghostty libraries without the native extension return
`Unsupported` rather than falling back to viewport-only data.

## Capture and ownership

```csharp
// TerminalControl calls must run on its UI thread.
TerminalHistoryStatus status = control.CaptureHistory(
    new TerminalHistoryCaptureRequest(MaxRows: 200, MaxCharacters: 64 * 1024),
    out TerminalHistorySnapshot? snapshot, cancellationToken);

if (status == TerminalHistoryStatus.Success)
{
    // Owned pages can be read on any thread, even after terminal disposal.
    TerminalHistoryPage page = snapshot!.ReadPage(0, 50, 16 * 1024, cancellationToken);
}
```

Capture includes history and the live screen of the active buffer, independently
of the displayed viewport and synchronized-output holds. A tail request retains
the newest contiguous suffix. Explicit ranges retain a prefix:

```csharp
TerminalHistoryStatus metadataStatus = control.GetHistoryBufferInfo(out var buffer);
if (metadataStatus == TerminalHistoryStatus.Success)
{
    var range = new TerminalHistoryRange(buffer!.AvailableRange.Start, 20);
    TerminalHistoryStatus status = control.CaptureHistory(
        new(20, 16 * 1024, buffer, range), out var snapshot, cancellationToken);
}
```

Use the returned `AvailableRange`; never assume its origin is zero. Buffer IDs
are instance-specific, and layout epochs change on resize/reflow, reset, history
clear, recreation and structural edits. Ordinary prefix eviction advances the
origin without changing the epoch. The native adapter also excludes page slack
beyond the host scrollback limit. A partially unavailable explicit range is
rejected without clamping. Buffer switches select the other buffer's identity;
existing owned snapshots remain readable.

Rows are physical rows, in oldest-to-newest order. Empty rows and explicitly
written trailing spaces survive. Trailing erased cells and wide-character
spacers are omitted. Surrogate pairs, combining sequences and emoji graphemes
remain complete. Wrap flags describe the original physical rows; capture does
not join soft wraps or expand boundaries into adjacent rows.

Both budgets must be positive. Characters are UTF-16 code units, including one
LF between retained rows. Complete rows are retained: capture stops before the
next row exceeds a budget, and never skips an oversized row. `BudgetTooSmall`
means the first required row cannot fit. Zero-length explicit ranges are valid;
a page offset at the snapshot end returns an empty successful page.

`RowLimit` records rows excluded by the row bound; `CharacterLimit` records a row
that could not fit the remaining character budget. Both can apply. Omitted rows
are not formatted to estimate their character count. The capture engine visits
only selected rows and the first rejected row. Native row references are located
from the nearer end of the page list, and cell reads use direct grid references.
No full-history export, viewport mirror or temporary screen is involved.

Snapshots own immutable strings and row arrays only. They retain no processor,
screen, native handle or borrowed grid reference. `SnapshotId` identifies a
capture, not a live terminal revision. Snapshot-relative `ReadPage` offsets and
`NextOffset` stay stable after output, eviction, resize, buffer switches and
disposal. The library imposes no timer or expiration.

## Outcomes and synchronization

| Outcome | Meaning |
| --- | --- |
| `Success` | Metadata, snapshot or page is available. |
| `Unsupported` | Processor/native library lacks the capability. |
| `BufferUnavailable` | Source is unavailable or the explicit buffer is not active. |
| `LayoutChanged` | Explicit range's layout epoch is obsolete. |
| `HistoryEvicted` | Explicit range starts below the retained origin. |
| `RangeUnavailable` | Explicit range extends beyond the available end. |
| `BudgetTooSmall` | The first required complete row exceeds the character budget. |
| `Disposed` | The processor has been disposed. |

Invalid arguments throw argument exceptions. Cancellation throws
`OperationCanceledException` before capture, between rows and within cell loops;
no partial snapshot is returned. Direct processor callers must hold the owning
screen lock across capture and serialize all input, resize, reset and history
mutations with that same lock. Processor disposal acquires the same lock.
Control callers must follow existing UI-thread affinity; forwarding supplies
the lock. Owned page reads require neither lock nor UI thread.

## Royal Connect consumer handoff

Cross-checked against DesktopClient's current generic session automation API on
2026-10-07. Its retained `ISessionAutomationTerminal.ReadHistoryAsync` and
`TerminalHistoryPage` contracts fit the owned snapshot without SDK contract
changes. MCP now routes through `ISessionObservations.ObserveAsync` and
`observe_session`; implementing the typed method alone does not expose history.
The following typed mapping belongs in that subsequent integration:

```csharp
// Authorize this session and token before every page response.
TerminalHistoryPage page = captured.ReadPage(offset, maxRows, maxCharacters, cancellationToken);
if (page.Status != TerminalHistoryStatus.Success)
    throw MapHistoryFailure(page.Status);

ImmutableArray<string> lines = page.Rows.Select(row => row.Text).ToImmutableArray();
return new DesktopClientTerminalHistoryPage(
    token, page.Offset, lines, page.NextOffset, captured.Truncated);
```

`DesktopClientTerminalHistoryPage` above denotes the consumer's
`TerminalHistoryPage` type (alias it to avoid the library type's name).
`Offset` is relative to captured rows, not the live buffer's monotonic origin.
`NextOffset` continues the same immutable snapshot. `Truncated` describes bounded
capture retention, independently of whether more pages remain in that snapshot.
Wrap flags remain available in RoyalTerminal; the existing consumer's `Lines`
contract receives physical row strings.

Royal Connect owns generation-scoped expiring tokens, authorization before
capture and every page response, bounded token storage and aggregate retained
text limits. Remove token references on expiration/session-generation changes.
Advertise an implemented observation descriptor through
`IPluginSessionAutomation.SessionObservations` and report readiness through
`CanObserve`. The former `Scrollback` capability enum and specialized MCP tools
were removed by DesktopClient's generic API refactor. Local Enabled + Observe
consent and the shell's visibility/Execute/McpObserve checks apply. Do not reuse
a token against a new live capture. This change does not implement those
DesktopClient responsibilities.

### Current MCP integration requirements

DesktopClient's authoritative sources are `docs/mcp-session-automation.md`,
`docs/adr/0002-generic-plugin-session-automation.md`, the shared SDK's
`Automation/SessionObservations.cs` and `Automation/SessionAutomation.cs`, and
`src/Common/UI.Desktop/Mcp/McpRuntimeApi.Automation.cs`.

1. Add a plugin-scoped observation (for example, ID `terminal.history`)
   with a localized descriptor, owned parameter schema and implemented format.
   Define token/offset/limit validation, initial capture policy and row/page bounds.
   Reject unknown fields and unsupported formats without capturing content.
   The existing standard-operation helper recognizes only `terminal.screen` and
   `desktop.image`; history needs an explicit observation dispatch branch.
2. Construct `SessionObservation` with owned JSON `Data`, capture timestamp,
   snapshot retention `Truncated` and optional `ContinuationToken`. Include
   snapshot-relative offset, next offset and lines in the JSON projection.
   Preserve the original capture timestamp across pages. Do not derive session
   generation from `SnapshotId`, `BufferId` or `LayoutEpoch`.
3. Honor `SessionObservationRequest.MaxBytes` before constructing large results.
   Current JSON/text limit is 1 MiB of UTF-8. RoyalTerminal budgets count UTF-16
   text plus LF separators, so reserve JSON metadata, row delimiters, escaped
   characters and token overhead when deriving capture/page budgets. A character
   limit is not a serialized-byte limit. Validate the actual bounded serialized
   result; never truncate serialized JSON. Continuation tokens are at most 4096
   characters. Capture retention can have a separate, bounded storage budget.
4. A null typed history token starts a capture; subsequent reads resolve that
   same capture. Own tokens per exact session/client generation, with explicit
   expiration, bounded count and aggregate storage. Reject expired/foreign/stale
   tokens rather than silently recapturing. Release retained references on session
   end, generation replacement and adapter disposal. Display rehosting preserves
   session identity. Owned snapshot readability after terminal disposal does not
   authorize MCP delivery after the session ends.
5. Check invocation authorization and cancellation before capture, after any UI
   dispatch/wait and before returning each page. Pass the authorization's token
   to capture and pagination; never retain the authorization in the token store.
   The shell already rechecks generation, client/automation identity, readiness,
   descriptor retention and consent around observation calls. The adapter must
   also recheck at its backend boundary and before delivery.
6. Acquire history on the control's UI thread through its locked forwarding API.
   Paging owned data can happen off-thread. Readiness must check the source
   capability/native extension, not the viewport mirror or renderer alone.
   Map unsupported, unavailable and budget failures explicitly and safely.
7. Ship matching RoyalTerminal managed and native artifacts exposing this API.
   DesktopClient pins RoyalTerminal packages together to 0.6.2; a version pin
   alone does not establish inclusion of these local changes. The approved integration upgrades existing pins without adding dependencies or project references.

Acceptance for that adapter must cover discovery without capture, disabled
consent, independent Observe/Control grants, revocation while queued and before
return, expired/foreign tokens, reconnect/rehosting, bounded token cleanup,
Unicode/JSON byte expansion, immutable continuation after terminal changes,
unsupported native libraries and fresh native/platform observation checks.
RoyalTerminal's focused tests verify the source capability; they do not verify
the DesktopClient adapter or the refactored MCP transport.

## Reference decision and validation

Use buffer-based access as in [Windows Terminal's TextBuffer](https://github.com/microsoft/terminal/blob/main/src/buffer/out/textBuffer.hpp),
[Ghostty's page/grid references](https://github.com/ghostty-org/ghostty/blob/main/src/terminal/c/grid_ref.zig)
and [xterm.js's buffer API](https://github.com/xtermjs/xterm.js/blob/master/src/common/public/BufferApiView.ts).
Physical rows and separately owned bounded pagination match Royal Connect's
existing contract. Clipboard-style unwrapping and wide-glyph boundary expansion
are intentionally excluded. Existing selection/export/binary/screen snapshot
APIs are unchanged.

Run the focused suite after building and staging the matching native library:

```text
zig build test-history -Dtarget=aarch64-windows -Doptimize=ReleaseFast
dotnet test tests/RoyalTerminal.HistoryTests/RoyalTerminal.HistoryTests.csproj
```

The suite exercises both backends, owned pagination, physical text semantics,
budgets, identities, eviction, lifecycle races, unchanged presentation/parser
state, allocation scaling and Avalonia Headless forwarding. Shared engine tests
count visited rows and deterministically cancel between rows. Native ABI tests
cover sized structures, row boundaries and layout changes.

Validated locally on Windows ARM64: 45 focused xUnit tests passed with no skips;
the native ReleaseFast build and `test-history` step passed; the affected
Avalonia build completed with zero warnings and errors. The staged 0.6.2 feed includes freshly rebuilt VT libraries for all six runtime targets; runtime execution was verified on Windows ARM64.

The larger `RoyalTerminal.Tests` project currently requires uncached versions of
`ReactiveUI.Reactive` (26.0.1), `ReactiveUI.Avalonia.Reactive` (12.1.6),
`SshNet.Agent` (2026.0.0) and `coverlet.collector` (10.1.0). Its offline restore
is blocked; the focused project avoids the unrelated app/settings dependency
chain without changing production dependencies. Validation on other native
platforms still belongs in CI.

## Completed consumer integration

Royal Connect now implements this contract through terminal.history. See
[the integration handoff](mcp-history-integration.md) for the schema, token
limits, staged 0.6.2 package hashes, verified platforms and remaining release gates.
