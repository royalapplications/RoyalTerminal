# Managed parser checkpoints: recovery beyond both history tiers

The CLI, the native Avalonia workspace and the browser workspace can restore the existing managed parser when the hot/cold event prefix has been evicted. This is a live-host recovery feature, not process restoration after a server crash or reboot. See [interactive recovery, Lua and agent integration](./rex-interactive-parity.md) for the client-specific bounds.

```sh
# Read and restore the current state without attaching to the remote terminal.
royalterminal-mux mirror Work Build
# Recover a fresh parser, subscribe as observer, print its final screen, and return the child exit code.
royalterminal-mux mirror Work Build --wait
```

## How the cut is taken

`GET /api/sessions/{sessionId}/blocks/{blockId}/checkpoint` requires read access for that session. The inspection engine serializes the existing `BasicVtProcessor` using the binary snapshot codec. It samples the broker descriptor under the same parser lock that publishes output, canonical resize and process exit. One transport read may become several broker events: neither a byte offset nor a callback count is a valid event cursor.

The media type is `application/vnd.royalterminal.ghostty-snapshot-v1`. The response is `no-store`, includes a bounded `Content-Length`, echoes the session and block GUIDs, and supplies `X-Workspace-Sequence`. `X-Workspace-Exit` is present for an observed exit. The codec preserves supported buffers, cursor, modes, styles and unfinished UTF-8/VT input. OSC 7501 records use separate inspection APIs. This is not a screenshot, a process-memory dump or an xterm.js serialization.

The server does not publish provisional bytes on encode failure. Two concurrent checkpoint transfers per sample host, an 8 MiB wire limit, record/cell/page/continuation limits and write deadlines bound this path. Unrepresentable states fail explicitly. Encoding temporarily blocks parsing for that terminal. These logical quotas do not establish a hard total heap limit or a latency guarantee.

## Restoring and attaching

Validate session, block, media type, length and cursor before decoding. Publish a new replica only after importing the complete snapshot. Reattach with `AfterSequence = checkpoint.Sequence` and apply **all** subsequent output and canonical geometry in sequence. The inspection host alone answers terminal queries. Never replay keystrokes to compensate for a disconnection.

The two-request protocol does not pin history while a client is disconnected. A fast producer can evict the short suffix during checkpoint transfer. Clients retry a bounded number of times and then report a recovery error. The CLI mirror always resumes as an observer. Interactive clients request only their authorized role; a snapshot grants no control lease.

[Server acceptance tests](../../tests/RoyalTerminal.Multiplexing.ServerTests/CheckpointRecoveryTests.cs) force 4,200 events, require normal fresh replay to fail and cut inside UTF-8 or CSI. [Native tests](../../tests/RoyalTerminal.Multiplexing.DesktopTests/InteractiveCheckpointTests.cs) cover both initial attach and transient disconnect with no duplicated input. The [Chromium scenario](../../samples/RoyalTerminal.Multiplexing.Server/browser-tests/e2e/checkpoint.spec.mjs) uses a real pipe process, evicts the journal, completes a multibyte character in the recovered viewport, changes screen buffers and verifies the child's exit code.
