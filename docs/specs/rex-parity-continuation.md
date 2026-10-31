# Experimental workspace parity increments

This document supersedes the matching entries in the [original implementation matrix](./rex-experimental-implementation.md) at `a298208`. The [research inventory](./rex-product-research.md) describes Rex; this page describes **RoyalTerminal**. [Original local test counts](./rex-experimental-validation.md) belong to that earlier revision and are not current CI totals.

## RX-003 / RX-006: window and block placement

A session owns processes. A window contains a tiled split tree, floats and an unplaced shelf. Each block has a `windowId`, a `placement` (`Tiled`, `Floating`, or `Unplaced`), and optional `floatingBounds`. Only tiled IDs may appear in the split tree, exactly once. Changing placement does not dispose the transport or reset the event cursor, PTY, control lease, or inspection engine.

`block.place` moves a block inside or between windows of the same session. A tiled move can insert before or after a destination leaf; removing the source leaf collapses its parent. Floating rectangles use integer ten-thousandths of window width/height. The complete rectangle must fit within the window. `block.swap` exchanges placements, including float bounds. Both operations use the same structural revision as rename and split. A rejected stale `expectedRevision` must leave both trees unchanged.

`session.move_window` reorders tabs using one-based positions. `session.close_window` requires an explicit surviving destination for a nonempty window. Its terminals move to the destination shelf without stopping. Closing the last window is rejected. Use explicit session closure to terminate the processes.

In the browser, open the command palette to float, shelve, move, swap, or reorder. Drag a floating pane’s handles or focus a handle and use arrow keys. A shelf button restores the same terminal, not a new process. The native viewer still shows one selected block rather than the full browser layout.

## RX-010: replay and native reconnection

The optional [cold replay archive](./rex-replay-recovery.md) retains original output bytes, resizes, and lifecycle events after hot journal eviction. Enable `ROYALTERMINAL_MUX_COLD_REPLAY_MIB=4` to give each block four additional MiB of logical storage. Fresh viewers reconstruct from sequence one; a surviving parser can resume from its own applied cursor. Cold pages, hot events and live output share one sequence and one replay fence. Compression is lossless; this is not a styled screen capture or disk persistence.

The [native reconnect policy](../../samples/RoyalTerminal.Multiplexing.Desktop/RECONNECT.md) preserves the actual processor and screen on transient loss. It drains accepted output before resuming the transport; it never resends input. An orderly disconnect may be an administrator releasing a lease and is not retried. Losing the required prefix from **both** tiers remains an explicit `HistoryGap`.

## RX-012: process exit

```sh
mux=samples/RoyalTerminal.Multiplexing.Cli
# Launch once. Arguments following -- belong to the child.
dotnet run --project "$mux" -- run Work --wait --pipe -- /bin/sh -c 'exit 37'
# Observe an already-running or already-exited block without taking its lease.
dotnet run --project "$mux" -- wait Work Build
```

The CLI returns the observed child exit code and writes a structured receipt. Cancelling a wait does not close or relaunch the process. A block removed before observation is an error, not exit zero. Shell exit-code conventions apply; scripts can distinguish a real exit from a CLI error by inspecting the receipt and stderr.

## RX-014 / RX-020: structural observation, terminal events, attention

Use the correct cursor for each data stream:

| Operation | Cursor | Guarantee |
| --- | --- | --- |
| Terminal attachment | Block VT event sequence | Exact bytes and geometry through the replay fence, then live output. |
| `session.wait_change` | Session layout revision | Returns a structural snapshot after a change or timeout. Intermediate changes may coalesce. |
| `terminal.events` | Per-block metadata sequence | Ordered retained transitions with explicit metadata gaps. Does not require or create a terminal attachment. |

`terminal.events` takes `sessionId`, `blockId`, optional `after`, `limit` (1–128), and `timeoutMs` (0–25,000). Omit `after` to start at the current position without reading old data. Use zero to request events since creation. Continue from **`nextSequence`**, not `lastSequence`: a response can contain only part of the available history. An equal cursor with an empty event list is a timeout, not process exit. `ended` becomes true on the last page after exit or disposal. An expired prefix returns `HistoryGap`; a cursor ahead of this block returns `Conflict`. Retention is capped by both 512 entries and a 512-KiB logical charge; each block allows at most 32 waiting readers. A scoped grant is checked again after waiting.

Current terminal event families are bell, title change, OSC 7501 status-record set/removal, and child exit. A status transitioning from working to blocked and then clearing can generate distinct events even when the bytes arrive in one transport callback. Text reported by a program is untrusted data. These records are not a general server event bus, native notifications, or Rex wire envelopes. Rex's [event catalog](https://www.superlogical.com/rex/docs/reference/events) and [OSC 7501 reference](https://www.superlogical.com/rex/docs/build/program-status) describe the upstream concepts.

`session.attention` aggregates current status **records**, not process counts, across windows and blocks, including hidden terminals. Blocked counts are broken down into permission, question, and authentication. Per-block snapshots are coherent; aggregation is not a global lock over process output. Missing inspection during startup or teardown is reported as unavailable rather than idle. An observer can use **Inspect workspace attention** from the browser command palette or read the API; neither takes control.

```lua
-- In an explicitly run, trusted local workspace script:
local ids = {sessionId = rex.session_id, blockId = assert(rex.args[1], 'pass a block GUID')}
local page = assert(rex.call('terminal.events', ids))
repeat
  ids.after = page.nextSequence
  ids.timeoutMs = 10000
  page = assert(rex.call('terminal.events', ids))
  for _, ev in ipairs(page.events) do print(ev.sequence, ev.name, ev.recordId or '') end
until page.ended
```

## RX-019: Lua scripting

`royalterminal-mux lua FILE [ARGS...]` runs a **trusted local file** with an external Lua 5.1 interpreter and `dkjson`. `ROYALTERMINAL_LUA51` selects the interpreter. The CLI mediates authenticated API calls; it does not pass the host bearer token into the Lua environment. This is not isolation from other processes under the same OS account. The server has no script-upload endpoint.

The adapter supports `rex.call`, session methods, arguments, JSON, logging, local actions and invocation, timers, sleep, and stop. The `on` / `wait` convenience functions currently subscribe to structural `session_view_changed`; use `rex.call('terminal.events', ...)` for terminal metadata. Timers use a cooperative event loop. Long-running callbacks can delay subsequent ticks. This is not an embedded server-side configuration runtime, and local actions are not installed into other clients’ configurations. See [the upstream Lua reference](https://www.superlogical.com/rex/docs/reference/lua) and the [runnable example](../../samples/RoyalTerminal.Multiplexing.Cli/examples/workspace-watch.lua).

## RX-016 / RX-022 / RX-025: personal server distribution

The [Linux personal bundle](../../scripts/workspace/README.md) includes the host, CLI and browser assets, a systemd user unit, installation/removal scripts, and `SHA256SUMS`. The user service exposes a private Unix socket, not public TCP. HTTP credentials are still required over that socket. An upgrade stages a new release for the next deliberate start; it does not restart the running host or interrupt a shell. Stopping the host still ends its processes. The bundle is unsigned; checksums alone do not establish provenance. The smoke test launches the installed payload under a temporary home, not a privileged or machine-wide login service.

## Acceptance and open work

| Area | Distinguishing acceptance check |
| --- | --- |
| Layout | [Browser scenario](../../samples/RoyalTerminal.Multiplexing.Server/browser-tests/e2e/placements.spec.mjs) preserves the same xterm, WebSocket and controller through a move. |
| Replay | [Broker tests](../../tests/RoyalTerminal.Multiplexing.Tests/ColdReplayTests.cs) compare bytes and geometry event-for-event, including mid-page resume, incompressible data and exited readers. |
| Native reconnect | [Headless tests](../../tests/RoyalTerminal.Multiplexing.DesktopTests/PreservedReconnectTests.cs) split a CSI across sockets and inspect the glyph in the original screen. |
| Status and sharing | [Browser scenario](../../samples/RoyalTerminal.Multiplexing.Server/browser-tests/e2e/attention.spec.mjs) reads an unplaced process's approval request as an observer without a PTY attachment; rejects a foreign session. |
| Lua and process lifecycle | [Linux smoke](../../scripts/workspace/smoke-lua.sh) executes the real interpreter, changes the actual session, and propagates exit 37 from a real child. |
| Personal installation | [Unix socket smoke](../../scripts/workspace/smoke-personal.sh) checks the credential boundary, shell I/O, safe upgrade and removal. |

Remaining work includes compact parser checkpoints independent of any retained prefix, persistent workspace recovery/supervision, full native/mobile workspace composition, system-login/PAM privilege separation, Tailscale discovery and identity, complete Lua configuration/subprocess/event APIs, general event subscriptions and OS notifications, signed cross-platform installers, and end-to-end production qualification. This branch is an independent experiment, not a compatible Rex client/server replacement.
