# Experimental workspace parity: current implementation

This status page supersedes the matching entries in the [original design and acceptance matrix](./rex-experimental-implementation.md) at `a298208`. The [research inventory](./rex-product-research.md) describes Superlogical Rex; this page describes **RoyalTerminal**. Original local test counts in the [baseline validation record](./rex-experimental-validation.md) belong to that earlier revision; use the PR’s Checks tab for the current head.

## What changed

| Requirements | Available in the experimental branch | Explicit boundary |
| --- | --- | --- |
| RX-003 / RX-006 | Tiled, floating and unplaced blocks; move/swap within or across windows; window close/reorder; browser keyboard and pointer controls. | Same session. The native desktop sample does not yet present the entire multi-pane layout. |
| RX-010 | Lossless cold replay; native preserved-parser reconnect; authenticated **binary managed checkpoint plus live suffix**, even after both journals have lost the prefix. | The managed CLI imports checkpoints. Browser xterm and the interactive Avalonia sample do not automatically replace their displayed surfaces from binary checkpoints. No process-restart survival. |
| RX-012 | `wait` and `run --wait` observe and return the actual child exit code. | No input lease or inferred success on disappearance. |
| RX-014 / RX-020 | Session revision waits, ordered per-block bell/title/status/exit events, and workspace/window/block attention counts in the API and browser. | No general system-wide event bus, push notifications or native notification integration. |
| RX-019 | Trusted local Lua 5.1 scripts with authenticated calls, actions, timers, arguments and structural events. | Requires an external interpreter and `dkjson`; not an embedded server-configuration runtime or full Rex Lua API. |
| RX-016 / RX-022 / RX-025 | Per-user Linux service, private Unix socket, verified unsigned build artifact, staged upgrades and removal. | No privileged/PAM login broker, Tailscale identity service, signed packages, or production service qualification. |

## Moving live terminals

A session owns processes. A window contains tiled leaves, floats and an unplaced shelf. Only tiled IDs appear in its split tree; a hidden block retains a home window. `block.place` can insert before/after a destination leaf or assign a floating rectangle in ten-thousandths of window dimensions. `block.swap` exchanges positions, including float bounds. Both validate `expectedRevision` in the structural transaction. A failure cannot move one side without the other.

`session.move_window` takes a one-based position. `session.close_window` asks for a surviving destination when the source contains terminals. They appear on its shelf without losing their PTY, stream sequence, capture engine or controller lease. Closing the last window is rejected. The browser palette exposes these operations and restores a shelved pane without recreating its xterm. Floating handles support both drag and arrow keys.

## Recovering a terminal

1. A surviving parser resumes from its applied cursor. The [native reconnect controller](../../samples/RoyalTerminal.Multiplexing.Desktop/RECONNECT.md) drains accepted bytes before reconnecting and does not resend user input. A normal administrator disconnect is not treated as a transient network fault.
2. The optional [cold history tier](./rex-replay-recovery.md) extends the range of exact byte/geometry replay. Set `ROYALTERMINAL_MUX_COLD_REPLAY_MIB=4` for a four-MiB logical cold budget per block. Normal attaches still reject missing history explicitly.
3. The [managed checkpoint protocol](./rex-managed-checkpoints.md) captures the parser and its actual broker cursor under the same gate. `mirror` can start without any retained prefix. `mirror --wait` follows the suffix as an observer through exit. If the short suffix expires during transfer, the CLI discards that provisional replica and tries a new cut, at most three attempts. Over-budget/unrepresentable states fail; no text-only substitute is presented as a complete parser.

```sh
mux=samples/RoyalTerminal.Multiplexing.Cli
# Inspect a reconstructed managed screen now, or observe until exit without taking input control.
dotnet run --project "$mux" -- mirror Work Build
dotnet run --project "$mux" -- mirror Work Build --wait
# Launch once; arguments after -- belong to the child. This returns 37.
dotnet run --project "$mux" -- run Work --wait --pipe -- /bin/sh -c 'exit 37'
```

Capture, replay, checkpoint and disk restore are distinct. Neither an in-memory archive nor a serialized parser transfers ownership of an OS process or PTY across a host failure.

## Reading events and attention

| Read | Cursor | Contract |
| --- | --- | --- |
| Terminal attachment | Per-block VT sequence | Ordered output, geometry and lifecycle. |
| `session.wait_change` | Session structural revision | Latest snapshot after a change or timeout. Intermediate revisions may coalesce. |
| `terminal.events` | Per-block metadata sequence | Ordered retained transitions; no PTY attachment. |

`terminal.events` accepts `sessionId`, `blockId`, optional `after`, `limit` (1–128), and `timeoutMs` (0–25,000). Omit `after` to start at the current position, or use zero for history since creation. Continue from **`nextSequence`**, not `lastSequence`: pages can contain only part of the available history. A timeout is not an exit. `ended` is true on the final drained page after exit/disposal. Lost history is `HistoryGap`; an ahead cursor is `Conflict`. Limits are 512 records, a 512-KiB logical charge, and 32 waiting readers per block. Read privileges are checked again after a wait.

`session.attention` counts current OSC 7501 **records**, not processes, by window and block. Approval, question and authentication counts subdivide blocked records; they are not additional blocked records. Hidden terminals count. A temporarily unavailable block is explicitly identified. The browser’s **Inspect workspace attention** command is available to observers and does not attach. These are advisory per-terminal snapshots, not a global transaction across all running processes. Upstream references: [Rex events](https://www.superlogical.com/rex/docs/reference/events) and [OSC 7501](https://www.superlogical.com/rex/docs/build/program-status).

```lua
-- Pass the block GUID as the first argument and set ROYALTERMINAL_MUX_SESSION.
local ids = {sessionId = rex.session_id, blockId = assert(rex.args[1])}
local page = assert(rex.call('terminal.events', ids))
repeat
  ids.after = page.nextSequence
  ids.timeoutMs = 10000
  page = assert(rex.call('terminal.events', ids))
  for _, ev in ipairs(page.events) do print(ev.sequence, ev.name, ev.recordId or '') end
until page.ended
```

## Lua and Linux installation

`royalterminal-mux lua FILE [ARGS...]` uses an external Lua 5.1 interpreter (`ROYALTERMINAL_LUA51` overrides its path). The parent CLI mediates authorized API calls and does not pass its bearer in the child environment. This is **not** isolation between mutually untrusted OS users. There is no script-upload endpoint. Local actions and cooperative timers live for the script process, not in shared `init.lua`. Structural changes have `on`/`wait` helpers; use `rex.call` for the terminal metadata feed. See the [runnable example](../../samples/RoyalTerminal.Multiplexing.Cli/examples/workspace-watch.lua) and [Rex Lua reference](https://www.superlogical.com/rex/docs/reference/lua).

The [Linux personal bundle](../../scripts/workspace/README.md) uses a systemd user unit, credential-protected Unix socket, immutable staged release directories and explicit removal. Staging an upgrade does **not** restart the running host. An explicit restart still terminates its processes. Included checksums detect corruption, not publisher identity.

## Acceptance evidence

[Browser placement](../../samples/RoyalTerminal.Multiplexing.Server/browser-tests/e2e/placements.spec.mjs) checks identity, connection and lease after moving a pane. [Cold replay](../../tests/RoyalTerminal.Multiplexing.Tests/ColdReplayTests.cs) compares the full byte/geometry timeline. [Native reconnect](../../tests/RoyalTerminal.Multiplexing.DesktopTests/PreservedReconnectTests.cs) checks a glyph across sockets in the original parser. [Checkpoint recovery](../../tests/RoyalTerminal.Multiplexing.ServerTests/CheckpointRecoveryTests.cs) checks fresh parsers after eviction, including partial UTF-8 and CSI. [Observer attention](../../samples/RoyalTerminal.Multiplexing.Server/browser-tests/e2e/attention.spec.mjs) checks an unplaced process, no control acquisition and rejection of a foreign session. The [CLI/Lua smoke test](../../scripts/workspace/smoke-lua.sh) runs actual processes and a checkpoint mirror. The [personal Unix smoke](../../scripts/workspace/smoke-personal.sh) launches the installed payload in a temporary home; it does not claim a privileged systemd/PAM installation was tested.

Still open: complete GUI adoption of checkpoints; richer native desktop/mobile layouts and notifications; full Rex-style Lua configuration, subprocess and event APIs; multi-account privilege separation/PAM; Tailscale discovery and identity; persistent session supervision; signed multi-platform distribution and production qualification. This is an independent experiment, not a wire-compatible Rex replacement.
