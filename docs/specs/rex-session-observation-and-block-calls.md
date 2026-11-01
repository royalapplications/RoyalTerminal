# Session-wide terminal observation and typed block operations

This supplement advances the [workspace and automation specification](./rex-client-and-automation-parity.md). It describes the independent RoyalTerminal experiment, not a client that can connect to a Rex server. Reference contracts: Superlogical's [Lua API](https://www.superlogical.com/rex/docs/reference/lua), [block method discovery](https://www.superlogical.com/rex/docs/automate/server-api), [events](https://www.superlogical.com/rex/docs/reference/events) and [script examples](https://www.superlogical.com/rex/docs/automate/lua-events). Inspect RoyalTerminal's running catalog using `royalterminal-mux api describe METHOD` for its exact argument and result schemas.

## Observe work before its first process exists

Subscribe with a session-scoped observer token; no control lease or block selection is required. A single subscription covers existing, new, tiled, floating and shelved terminals **inside that session only**. A process that exits before its launch call returns is included. For example:

```sh
# Start at now. Print three terminal events using a session_event / block_event JSON envelope.
royalterminal-mux events -s Work --json --count 3
# Resume the raw session feed after the last record durably consumed by your app.
royalterminal-mux events Work --after 47
# A block-scoped cursor is a different number space. Its stream terminates after the child exits.
royalterminal-mux events Work Build --after 0
```

```lua
-- watch.lua: ROYALTERMINAL_MUX_SESSION identifies this workspace; no default block is needed.
rex.on('terminal.child_exited', function(ctx, ev)
  assert(ev.type == 'block_event' and ev.name == 'child_exited')
  print(ctx.block_id, ev.exit_code, ev.error or '')
end)
rex.on('terminal.pwd_changed', function(ctx, ev)
  local t, err = rex.block.call('com.superlogical.terminal', 'title', {})
  if t then print(t.title, ev.pwd) else rex.log('error', err) end
end)
```

The short and fully qualified terminal event names are aliases. `block_event` observes all terminal records. Lua receives one flat event with a `payload` table and matching `ctx.block_id`. Each removed OSC 7501 record is delivered as `program_status_removed` with a one-element `ids` array, including the empty ID for the root. A callback's implicit block is the emitting block: no need to guess based on focus. A directory URL is untrusted terminal metadata. The broker does not infer which OS process issued an escape sequence.

### Ordering, admission and backpressure

`session.terminal_events` returns `firstSequence`, `lastSequence`, `nextSequence`, `ended`, and `events`. Omit `after` for a start-at-now watermark. `nextSequence` advances only as far as the last record in a bounded page. The session's sequence is its publication order; `blockId` and `blockSequence` identify the source. Keep structural revisions, VT replay cursors, session metadata cursors and individual block metadata cursors separate. Across concurrently starting children there is no claim of wall-clock order for unadmitted startup output.

Each journal retains at most 512 records and 512 KiB of estimated metadata, with at most 32 waiting readers. Pages contain at most 128 events; long polls last at most 25 seconds. Admission copies the retained startup prefix and installs forwarding under the child lock, so no transition can fall between the two. If a child overflows its own feed before admission, the session reports an explicit history gap rather than silently presenting a complete history. A child exit does not end the session feed. Session closure wakes its waiters. Grant validity is checked after awaiting the page. These feeds are memory-only: they are not audit logs or replacements for persisted terminal checkpoints.

## Block methods

Inspect all eleven methods by their `com.superlogical.terminal.` prefix. From Lua use `rex.block.call(creator, method, args)`; the HTTP/API client may use the qualified name or `block.method` with `creator`, `method`, `sessionId`, `blockId` and nested `args`. Unrecognized fields are rejected. IDs used by the HTTP experiment are GUIDs; the example might therefore differ from a Rex `api describe` result. A scoped observer does not gain input privileges through generic dispatch or Lua.

| Access | Methods | Meaning and limits |
| --- | --- | --- |
| Read | `title`, `pwd`, `size`, `program_status` | Canonical title, reported directory, grid/pixel geometry and per-program records. Values from a child are untrusted. |
| Read | `format` | Text, HTML or styled VT, with optional trimming and unwrapping of retained screen contents. Four-million-character response limit. |
| Read | `process` | Actual live child identity when available. Linux PTYs can also identify a foreground process group and its leader after checking the controlling terminal and session. An unavailable leader is not replaced with the shell; a pipe has no terminal foreground group. No argv or environment is returned. |
| Control | `write`, `resize` | Requires a control-capable grant and a free exclusive lease. Write UTF-8 text **or** base64 data, at most 64 KiB per call. A successful write is not confirmation that a shell executed it. An existing interactive controller must use its own stream. |
| Control | `reset`, `set_theme` | RIS or validated color escapes are parsed and published as ordered **display output**, never typed into the child. These operations do not restart the process. Unfinished VT or UTF-8 fragments produce a conflict with no side effects. Theme arguments are optional default foreground, background and cursor in `#RRGGBB`. |
| Administrator | `list_dir` | Immediate child directory names under an absolute path, sorted ordinally. No recursion, no symlink children, at most 4,096 entries scanned and 512 directories returned. A scoped observer or controller cannot enumerate host paths. |

## Recovered browser history

After journal eviction, the Wasm worker owns a restored managed parser. Use **History** or **Shift+Page Up** on the recovered pane to inspect retained plain text. The transcript is captured once when the dialog opens; search and ordinary text selection therefore remain stable while the live terminal receives output. Closing it returns to the live view without a new attachment. The primary screen includes its retained history; the alternate-screen view exposes only the alternate viewport. Four Mi UTF-16 code units is a conservative extraction budget, not a promise to export every possible long or styled screen. This is a searchable text view, not a reconstruction of xterm.js's original styled scrollback or scroll position.

## Delivery of a terminal's final alert

Closed-page Web Push keeps the pending bit through child exit and transient provider errors. Retryable outcomes are network failure, provider timeout, HTTP 408, 429 and 5xx. At most six delivery attempts are made, with 10, 20, 40, 80 and 160 seconds before successive retries. Successful sends coalesce for 30 seconds. Expiration or revocation stops new attempts; a permanent rejection such as HTTP 410 drops the watch. Provider acceptance does not prove a device displayed a notification. No workspace text or credential is put in the provider payload. There is no persistent notification queue or exactly-once delivery guarantee.

## Reproduce the acceptance tests

- [Real block methods and permissions](../../scripts/workspace/smoke-block-protocol.py): a real pipe child reports its PID, receives `00 ff 41` unchanged and exits with 37. The test checks display-only reset, directory permissions and calls through both script hosts.
- [Session-wide events and named keys](../../scripts/workspace/smoke-terminal-tools.sh): a read-only watcher starts while a workspace is empty, observes two later process exits, and cannot read a neighboring workspace or type into either terminal.
- [Lua event payloads](../../scripts/workspace/smoke-lua.sh) and [parser-boundary tests](../../tests/RoyalTerminal.Multiplexing.InspectionTests/DisplayCommandTests.cs): callbacks receive the emitting block; attempted display mutations do not eat an unfinished escape sequence or character.
- [Chromium checkpoint recovery](https://github.com/royalapplications/RoyalTerminal/blob/experiment/rex-workspaces-20261030/samples/RoyalTerminal.Multiplexing.Server/browser-tests/e2e/checkpoint.spec.mjs): the source emits 10 MiB, evicts its first replay events, pauses mid-character, then emits more output and switches buffers during a read-only attachment.

The terminal method inventory here does not imply full product parity. Server-wide and per-client lifecycle events, OS-attributed process-change events, clipboard policy, native-platform UX, external push delivery, real-tailnet access and distributor trust each require separate contracts and acceptance evidence. Restoring display history does not restore a terminated OS process.
