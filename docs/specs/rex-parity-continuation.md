# Experimental workspace parity increments

This is the change record after the [original implementation](./rex-experimental-implementation.md), baseline `a298208`. The earlier acceptance matrix and its [validation record](./rex-experimental-validation.md) describe that baseline; the following entries supersede its corresponding boundaries. Nothing here claims Rex wire compatibility or recovery of an arbitrary live process after reboot.

## RX-003 / RX-006: window and block placement

A session owns processes. A window owns a tiled split tree, a floating plane and an unplaced shelf. The block descriptor supplies `windowId`, `placement` (`Tiled`, `Floating`, `Unplaced`), and optional `floatingBounds`. A block has exactly one placement. Floating and unplaced IDs must not appear in the tiled tree. A home window remains defined for an unplaced block. Changing placement neither disposes the transport nor resets the event cursor, PTY, control lease, or inspection engine.

`block.place` moves a block within or between windows of the **same session**. A tiled placement can split relative to a destination leaf. Removing a leaf collapses its parent before insertion. A floating rectangle uses integer ten-thousandths of the home window: widths/heights are positive and the entire rectangle must fit; an observer's device dimensions do not change the shared layout. `block.swap` exchanges the complete positions of two terminals, including floating bounds. Both operate under the same structural revision as rename and split. A stale `expectedRevision` must leave both trees unchanged. Use `api describe METHOD` for the exact request schema and example.

`session.move_window` reorders a tab. `session.close_window` requires an explicit destination when the source owns terminals. The terminals move to that destination's shelf; closure never implies process termination. Closing the last window is rejected. Explicitly close the session to terminate it.

In the browser use the command palette to float, shelve, move or swap the focused terminal, reorder windows or close a window while preserving its blocks. Drag a floating pane's handles or use arrow keys on the handles. The shelf restores an existing terminal without a second launch. The native sample still uses its single selected terminal surface.

## RX-012: supervised command exit

```sh
mux=samples/RoyalTerminal.Multiplexing.Cli
# Run once and return the actual child status. Arguments after -- are never parsed as CLI options.
dotnet run --project "$mux" -- run Work --wait --pipe -- /bin/sh -c 'exit 37'
# Wait for a process that already exists. Known GUIDs also work for scoped observers.
dotnet run --project "$mux" -- wait Work Build
```

The wait path resolves the target once, then reads retained process status without acquiring a control lease or replaying the terminal. It writes a structured exit receipt. Cancelling a wait does not close or relaunch the process. A removed block or an unreadable result is an observation failure, not exit zero; stderr distinguishes CLI failures from a child that returned the same numeric status.

## RX-014: structural observation

`session.wait_change` is a scoped long poll, not a lossless event log. Supply `sessionId`, `afterRevision`, and optional `timeoutMs`. An older revision returns immediately. At the current revision the broker registers the waiter under the mutation lock; unrelated sessions do not wake it. Return the new snapshot/revision to the next call. Timeout returns the current snapshot, and callers must compare revisions. Intermediate changes may coalesce. Terminal output remains on the per-block ordered stream. A host checks grant validity after waiting and before returning private state.

## RX-019: Lua scripts

`royalterminal-mux lua FILE [ARGS...]` executes a **trusted local script** with an external Lua 5.1 interpreter and the `dkjson` module. Choose an explicit interpreter using `ROYALTERMINAL_LUA51`. This is not an embedded Lua engine, an upload-and-execute endpoint, or a server-side `init.lua` loader. The broker never parses script source. The CLI keeps the bearer credential in its own process and mediates API calls; the child is not an OS sandbox.

```sh
export ROYALTERMINAL_MUX_SESSION='<session GUID>'
dotnet run --project samples/RoyalTerminal.Multiplexing.Cli -- lua \
  samples/RoyalTerminal.Multiplexing.Cli/examples/workspace-watch.lua
```

Supported scripting includes `rex.call`, session methods, script arguments, JSON, logging, local registered actions and nested invocation, cancellable one-shot/repeating timers, stop, sleep, and `on` / `wait` for `session_view_changed`. Timers share a cooperative dispatch loop. Long running callbacks can delay timers; there is no hard real-time guarantee. Local actions are not exported as persistent actions to other clients. No Lua script argument or command is retried across an uncertain network result.

The [Lua API reference](https://www.superlogical.com/rex/docs/reference/lua) distinguishes configuration scope and script scope. Additional Rex functions, server-side configuration callbacks, subprocess management and event families are not implied by the presence of a real interpreter here.

## Acceptance and remaining boundaries

The [broker placement tests](../../tests/RoyalTerminal.Multiplexing.Tests/PlacementTests.cs), [CLI tests](../../tests/RoyalTerminal.Multiplexing.ClientTests/ProcessWaitTests.cs), and [browser tests](../../samples/RoyalTerminal.Multiplexing.Server/browser-tests/e2e/placements.spec.mjs) exercise the distinct lifecycle and access requirements. The [Lua smoke script](../../scripts/workspace/smoke-lua.sh) runs an actual interpreter against a real host, verifies the bearer is not inherited, and checks a process returning 37. A completed CI run is evidence for the particular revision tested; adding a test is not evidence it passed.

Checkpoint-plus-live recovery after history eviction, persisted session recovery, native/mobile workspace composition, privileged multi-account login and PAM, Tailscale identity integration, signing, and the rest of the Rex Lua surface remain separate acceptance areas.
