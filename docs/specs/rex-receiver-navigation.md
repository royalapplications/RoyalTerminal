# Client action discovery and native pane navigation

The Rex [CLI reference](https://www.superlogical.com/rex/docs/automate/cli) and [custom-action guide](https://www.superlogical.com/rex/docs/customize/actions) distinguish a particular app's actions from server operations. RoyalTerminal makes the same distinction on its own protocol. A session can contain a browser and an older native client at the same time; the fact that both show the same terminal says nothing about whether they can present the same command.

## Discover first

In the desktop overview choose **Allow remote focus**, or explicitly opt a browser tab in through its palette. A phone supports the existing absolute-focus profile. Run these commands with an administrator credential for the broker and select the consenting UI. `SESSION` below is an ID or an unambiguous session selector; use the actual client ID reported by `views`.

```sh
royalterminal-mux views -s "$SESSION"
royalterminal-mux -C "$CLIENT" actions -s "$SESSION" --json
royalterminal-mux focus -s "$SESSION" -C "$CLIENT" --direction right
royalterminal-mux ui -s "$SESSION" -C "$CLIENT" client.tab.next
```

`view-actions -s SESSION -C CLIENT` is equivalent. A missing or ambiguous receiver is an error. No catalog command takes control, sends a key, extends the receiver lease, acknowledges queued actions, or changes the shared layout. Plain `actions` continues to mean the existing server automation command. `client.actions` accepts `sessionId` and `clientId` and returns `clientId`, `sessionId`, `kind`, `navigationVersion` and `forms`. Each form describes one wire shape: `name`, human-readable `title`, at most one `requiredArgument`, and optional `acceptedValues`. `pane.focus(blockId)` and `pane.focus(direction)` are distinct forms. The same definitions enforce enqueue admission. A catalog is a snapshot, not a promise that a client will still exist when an action is queued.

In either Lua host, `rex.client.actions()` reads the catalog for the invocation origin; `rex.client.actions{session_id=...,client_id=...}` explicitly selects IDs. This RoyalTerminal helper does not change the server's action registry. Discovery is not available while loading a configuration. On an API error it returns `nil, error`; missing or ambiguous selection is reported as a script error. Example:

```lua
local catalog = assert(rex.client.actions())
for _, form in ipairs(catalog.forms) do
  if form.name == 'pane.focus' and form.requiredArgument == 'direction' then
    return rex.session.focus_direction{direction='right'}
  end
end
return false -- older phone or desktop: leave its screen alone
```

## Receiver behavior

| Profile | Actions registered |
| --- | --- |
| Browser v1 | Focus a block or window, spatial/cyclic pane focus, next/previous tab, zoom, palette, sidebar |
| Desktop navigator v2 | Focus a block or window, spatial/cyclic pane focus, next/previous tab |
| Native v1 / mobile v1 | Explicit block or window IDs only |

Desktop `left/right/up/down` uses the normalized rectangles of tiled and floating blocks in the displayed window. A candidate must be on the requested side of the focused pane's center. Prefer overlapping projections on the perpendicular axis, then the nearest forward center, then the smallest perpendicular offset; stable IDs break ties. No wrapping at an edge. `next/previous` instead traverses the visible projection cyclically; with no remembered pane it starts at the first/last entry. A toolbar may hold keyboard focus without losing the remembered pane. Removing a pane removes that reference. Shelved blocks never participate. Switching windows observes the newly visible terminals; multiple observers cannot change the process's authoritative grid. A slow handshake must not focus a pane on completion.

A local layout edit, a vanished target, or a snapshot that changed during presentation can cause a receiver to decline. A queue receipt is **not an on-screen acknowledgement**. Unknown commands are rejected before any callback in the affected batch. A local presentation refusal stops later actions in that batch but does not imply that earlier actions were undone. Registration falls back to absolute-only mode **only** on a definitive `404 method_not_found`; network errors and revocation never cause duplicate registration.

## Acceptance

The server tests enumerate each declared form and verify whole-batch rejection on a legacy receiver. The SDK tests distinguish unsupported registration from credential errors, and reject malformed mixed batches before the first effect. Avalonia headless tests exercise focus ownership at a delayed handshake, boundaries and wrap, an empty tab, and observer-only cleanup. The existing real-process remote-view smoke runs the production CLI and both Lua hosts against Kestrel, checks action discovery without side effects, and attempts it with a read-only sharing token. These are independent workflows; they do not establish physical-device acceptance or equivalence to every Rex action.
