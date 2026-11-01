# Handheld navigation and receiver outcomes

## What a script can safely infer

`client.queue` returns `{clientId, sequence}` as soon as the broker admits a batch. Query `client.view_result` with that same client ID, session ID and positive sequence. Set `timeoutMs` between 0 and 20000 to poll or wait. Waiting never renews consent or consumes a command. Only an administrator can publish or query results. Use the original receipt; never repeat a toggle or process launch just because a wait timed out.

```sh
receipt=$(royalterminal-mux ui -s "$SESSION" -C "$CLIENT" pane.zoom) || exit 1
args=$(jq -c --arg s "$SESSION" '{sessionId:$s,clientId,sequence,timeoutMs:10000}' <<<"$receipt")
royalterminal-mux api call client.view_result "$args"
```

`SESSION` in that example is a UUID, not a label. In either Lua host, call `rex.call('client.view_result', {sessionId=s, clientId=c, sequence=n, timeoutMs=5000})` for an **already published** batch. Actions staged inside an outer Lua callback are not published until the callback succeeds.

| State | Meaning |
|---|---|
| `queued` / `offered` | No final outcome. A wait timeout returns the latest available state. |
| `applied` | The receiver says every action's presentation callback succeeded. This does not prove a physical screen repaint or a person saw it. |
| `declined` | `applied` is the exact length of the successful prefix. The next action was declined; the remainder did not run. Earlier actions are not undone. |
| `unreported` | A legacy receiver advanced its delivery cursor without reporting presentation. Success is unknown. |

On upgraded brokers the private lease advertises `completionProtocol: 1`. Only the receiver with its original grant and private reader key can report an offered batch in sequence. Repeating the identical report is safe. Conflicting counts, fabricated sequences and unoffered batches are rejected. A receiver does not replay local effects after an ambiguous HTTP failure. Sixty-four completed results per live receiver are retained in memory. An evicted result returns 410, a withdrawn/expired receiver returns 404, and a revoked grant cannot read a result. Unavailable is not evidence of failure. Up to sixteen result waits per receiver are admitted independently from its delivery poll.

## Phone behavior

Android and iOS use the same Avalonia presentation implementation. An opting-in phone negotiates v3 (relative focus, tab switching and a local zoom toggle); legacy v1 phones remain absolute-focus-only. On an older host the new phone negotiates downward, or falls back to basic registration after a definitive unknown-method response only. It never registers as desktop.

Spatial navigation uses the same normalized geometry and deterministic tie breaking as the desktop overview. Absolute focus follows a block moved to another window; a stale or shelved anchor is not silently replaced. Cyclic tab traversal includes empty windows. The phone displays one terminal at a time. Switching observes the new one; it does not request control. A locally controlled phone declines remote navigation. Zoom hides the header and status on this phone only, keeps Restore and Detach reachable, and neither changes the shared grid nor replaces the terminal connection.

The default [CI budget](../../scripts/workspace/CI.md) runs shared phone interactions on Linux. This is not a claim of hardware testing, live-provider delivery, or exhaustive Rex compatibility.
