# Workspace event streams

RoyalTerminal implements three observation scopes from the [Rex events reference](https://www.superlogical.com/rex/docs/reference/events). The transport, UUIDs, cursors and authorization are RoyalTerminal's own. An event subscription does not attach a terminal, grant input, or replay commands.

| CLI | API method | Authorization and meaning |
| --- | --- | --- |
| `events --json` | `server.events` | Administrator. Session creation, destruction and structural revision changes. |
| `events --all --json` | `server.all_events` | Administrator. Mixed server/session activity in publication order. |
| `events -s SESSION --json` | `session.events` | An administrator or a share for that session. Structural events, authenticated terminal attachments and terminal-reported metadata. |
| `events SESSION` | `session.terminal_events` | Previous session-wide terminal-metadata cursor; unchanged for existing consumers. |
| `events SESSION BLOCK` | `terminal.events` | One block's original terminal metadata and cursor. |

Without `--json` the CLI emits the compact native records. With `--json` it emits one JSON envelope per line. For instance, `window_created` is nested in a session event, and `terminal.child_exited` has both a session and a block envelope. The top-level `sequence` extension is the exact cursor for the selected scope. `--after N` resumes a stream; `--count N` exits after flushing the Nth record, including partway through a returned page.

## Ordering, retention and access

Each scope has its own contiguous sequence. A scoped token cannot use a global cursor to infer activity in another session. Never reuse a cursor from a different method, session or broker lifetime. Omit `after` to establish a start-at-now fence without consuming history. Pages contain `firstSequence`, `lastSequence`, `nextSequence`, `events` and `ended`. Advance to `nextSequence`, not the high-water mark. A cursor before retention returns `HistoryGap`; one ahead of the feed returns `Conflict`. A timeout returns an empty page and does not advance the cursor. HTTP authorization is checked before and after waiting. Server-wide feeds require administrative access, even for a controller share.

The broker records structural mutations under its own mutation lock. A window created and immediately closed appears even when no client ever sees it in a snapshot. Revision conflicts never publish. Roles are per terminal attachment; the reported `principal` is a capability class, not an asserted OS user identity. Child startup events are transferred as a retained prefix and then live events in the same order; a truncated prefix invalidates prior cursors instead of silently fabricating a complete history. Output bytes and bearer credentials are never event payloads. Titles and program status are private, untrusted text.

Each mixed ring admits up to 1,024 entries or 2 MiB of accounted data, whichever comes first. Per-journal and whole-hub waiter caps are 32 and 128. At most eight retired session journals remain available for final draining. The all-activity stream orders **admission** across different children, not the physical time of concurrent writes to different PTYs. The existing per-block event journal remains authoritative for terminal bytes and geometry.

## Lua scripts

A script subscribes to its session, or to all sessions with `do FILE --all`. The first `rex.on` registration establishes the fence synchronously. The same script can subscribe to `window_created`, `window_closed`, `session_view_changed`, `block_created`, `block_closed`, `client_connected`, `client_disconnected`, `block_event`, or a particular terminal event. An all-session script also sees server creation/destruction and attachment join/leave. `rex.wait` opens its own fence; it does not steal another handler's records. Handlers are registered during script load. Each callback has its own `ctx`; a session or block call with omitted IDs targets the event source, not whichever window launched the script.

```lua
rex.on('terminal.child_exited', function(ctx, ev)
  local session = assert(rex.session.view())
  print(session.label, ctx.block_id, ev.exit_code)
end)
```

The integration test starts two sessions, opens and closes a transient window, launches a real child that rings the bell and exits with status 37, compares CLI envelopes with paged API records, and revokes the observer share. The broker tests separately exercise eviction, cancellation and optimistic conflicts. These tests do not establish compatibility with every upstream event, creator, API or deployment.
