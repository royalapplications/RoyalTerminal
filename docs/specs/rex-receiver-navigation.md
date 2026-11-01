# Client action discovery, native navigation and pane zoom

The Rex [CLI reference](https://www.superlogical.com/rex/docs/automate/cli) and [custom-action guide](https://www.superlogical.com/rex/docs/customize/actions) distinguish a particular app's actions from operations on the server. A browser and an older phone can show the same terminal without supporting the same presentation commands. RoyalTerminal describes each consenting receiver rather than guessing from its OS. This is a RoyalTerminal protocol, not a Rex wire implementation.

## Try it

In the desktop overview choose **Allow remote focus**, or opt a browser tab in through its command palette. With the administrator credential and the ID of that view:

```sh
royalterminal-mux views -s "$SESSION"
royalterminal-mux -C "$CLIENT" actions -s "$SESSION" --json
royalterminal-mux focus -s "$SESSION" -C "$CLIENT" --direction right
royalterminal-mux ui -s "$SESSION" -C "$CLIENT" pane.zoom
royalterminal-mux ui -s "$SESSION" -C "$CLIENT" client.tab.next
```

`view-actions -s SESSION -C CLIENT` is equivalent to targeted `actions`. Bare `actions` retains its existing server-automation meaning. A missing or ambiguous receiver is an error. Discovery does not take control, send a key, renew or acknowledge a receiver lease, or change the workspace revision. `client.actions` takes `sessionId` and `clientId`. The result identifies the receiver and includes its `navigationVersion` and `forms`. Each form has a `name`, display `title`, at most one `requiredArgument`, and optional `acceptedValues`. `pane.focus(blockId)` and `pane.focus(direction)` are distinct. Enqueue admission uses the same definitions, but a catalog is not a guarantee that the receiver will still exist at enqueue time.

In either Lua host, `rex.client.actions()` reads the catalog for the calling view. `rex.client.actions{session_id=...,client_id=...}` selects explicit IDs. This read is forbidden while loading a configuration. API failures return `nil, error`; missing or ambiguous selectors raise a script error. To perform a specific action only when the selected client accepts it:

```lua
local catalog = assert(rex.client.actions())
for _, form in ipairs(catalog.forms) do
  if form.name == 'pane.focus' and form.requiredArgument == 'direction' then
    return rex.session.focus_direction{direction='right'}
  end
end
return false
```

| Receiver | Advertised actions |
| --- | --- |
| Browser v1 | Absolute and directional pane focus, window focus, next/previous tab, zoom, palette, sidebar |
| Desktop overview v3 | Absolute and directional pane focus, window focus, next/previous tab and zoom |
| Desktop navigator v2 | As v3, without zoom |
| Legacy native or mobile v1 | Focus by block ID or window ID only |

`client.negotiate_view` takes the session, kind, label and `maxVersion`; the host selects the highest compatible version. Older receivers can continue using their original registration methods. The SDK only falls back to an older method after a definitive `404 method_not_found`. It does not create a second lease after a timeout, revoked credential, unsupported response, or ambiguous network failure.

## Focus and zoom semantics

Desktop `left/right/up/down` uses normalized rectangles for tiled and floating terminals in the displayed window. Candidates must be on the requested side of the focused pane's center. Overlap on the perpendicular axis wins, followed by forward center distance, perpendicular offset, and stable block ID. It stops at an edge. `next/previous` instead traverses visible slots cyclically. Without a remembered pane it starts at the first/last slot. Toolbar focus preserves that memory; removing the pane clears it. Shelved blocks are excluded. Slow observer handshakes never move focus.

**Zoom** in the toolbar, **Alt+Z** inside an overview terminal, or the remote `pane.zoom` action expands the focused pane. With several panes and no remembered focus the action is declined. Escape inside the zoomed pane restores the layout. The other controls remain attached and parse output. Their scrollback, parsers and PTY grids are not resized. Changing focus to a different pane reveals it; editing shared placement or removing the zoomed pane restores the normal projection. If another client moves a divider during local zoom, leaving zoom shows the *new* divider position. Zoom changes no one else's screen. This is a toggle, not an idempotent 'ensure zoomed' command.

A local edit, missing target, or snapshot change can cause a receiver to decline. A queue receipt is not proof the command was displayed. Malformed batches are rejected before the first callback. Local refusal stops the remaining actions in the batch without undoing earlier effects. These presentation operations never grant permission to type in a shell.

## Evidence

`ViewCapabilityTests` checks declared forms against admission; `ViewNegotiationTests` checks fallback and malformed batches. `OverviewSubscriptionTests` delays a real transport handshake while another native pane is focused. `ObserverZoomTests` drives bytes into a hidden observer and checks its parser and restoration geometry without any outbound socket messages. `LiveViewProfileTests` connects the SDK over HTTP to a running broker with a scoped observer token: it can receive zoom but cannot enumerate or issue actions. `scripts/workspace/smoke-remote-views.py` verifies the CLI and both Lua paths against a real Kestrel host and PTYs. These checks do not establish physical-device or every-action Rex parity.
