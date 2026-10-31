# Managed parser checkpoints: recovery beyond both history tiers

**Available to managed consumers and the CLI**, in addition to the [lossless hot/cold event path](./rex-replay-recovery.md). A fresh client can reconstruct the current managed parser after the initial byte stream has been evicted. This is a live-host recovery feature, not process restoration after a server crash or reboot. The browser xterm renderer does not import this binary format, and the interactive Avalonia sample does not automatically replace its control surface with one yet.

```sh
# Read and restore the current state now, without attaching to the remote terminal.
royalterminal-mux mirror Work Build
# Recover a fresh parser, subscribe as observer, print its final screen, and return the child exit code.
royalterminal-mux mirror Work Build --wait
```

## How the cut is taken

`GET /api/sessions/{sessionId}/blocks/{blockId}/checkpoint` requires read access for that session. The inspection engine serializes the actual `BasicVtProcessor` using the repository's existing binary snapshot codec. The broker descriptor is sampled **under the same parser gate** that publishes output, canonical resize and process exit. Because one transport read may become several broker events, a separate output-byte or callback counter would give the wrong cursor.

The response has media type `application/vnd.royalterminal.ghostty-snapshot-v1`, `Cache-Control: no-store`, a bounded `Content-Length`, exact `X-Workspace-Session` and `X-Workspace-Block` GUIDs, and `X-Workspace-Sequence`. `X-Workspace-Exit` is present for an observed exit. The transfer preserves the buffers, cursor, modes, style and unfinished UTF-8/VT parser input supported by the existing codec. Feature-specific limitations of that codec still apply; OSC 7501 metadata uses its own separate inspection APIs. This is not a screenshot, a replay prefix, a dump of arbitrary process memory or an xterm.js serialization.

No provisional bytes are published on encode failure. Two concurrent checkpoint transfers per sample host, an 8-MiB wire limit, bounded records, cells, pages, continuations, and transfer deadlines protect this experimental path. An over-budget or unrepresentable state fails explicitly. Encoding temporarily blocks parsing for this terminal; neither throughput nor checkpoint latency is guaranteed. No precise total heap limit is implied by these logical quotas.

## Restoring and attaching

`MultiplexingCheckpointClient.ReadAsync` validates the block and session IDs, format, cursor and length before returning owned bytes. `ManagedTerminalSnapshot.Restore` must finish successfully before exposing its processor. For live observation, start the ordinary `MultiplexingTerminalTransport` with `Observer = true` and `AfterSequence = checkpoint.Sequence`. Feed bytes into the restored processor and apply canonical resize events in order. Do not send parser-generated responses back to the host.

A successful attach includes **every** event after the cut, including events that arrived while the checkpoint was in flight. This two-request protocol does not pin history while the client is disconnected: if that short suffix is evicted before attach, the CLI discards the provisional replica and takes another cut (at most three attempts). It does not replay input, acquire control, or silently skip data. It returns an error on sustained churn, a rejected capability, malformed snapshot, or unexpected stream termination.

[The network acceptance tests](../../tests/RoyalTerminal.Multiplexing.ServerTests/CheckpointRecoveryTests.cs) force 4,200 broker events with cold retention disabled, require a normal fresh attach to fail, and cut inside an unfinished UTF-8 character or CSI. They deliver the suffix before WebSocket attachment and check the resulting glyph and cursor location on the restored screen. This distinguishes genuine parser continuity from text-only recovery.
