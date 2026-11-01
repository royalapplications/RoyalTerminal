# Starting a terminal off the tiled layout

`session.new_block` starts a terminal already shelved. `session.new_layer` starts a single terminal floating above the existing layers of a specified window. Both accept a validated `layout` terminal leaf and an optional `expectedRevision`. A floating layer also accepts integer `bounds: {x,y,width,height}` in ten-thousandths of the window. The broker checks the revision, allocates the block and inserts its final placement in one structural change. Even a view that reads the session while the PTY is starting never sees a temporary tile. A failed start removes the block and its floating-order entry. A detached display does not imply a detached process: normal inspection, capture, exit events and explicit close still apply.

`session.close_layer` takes `sessionId`, `blockId` and optional `expectedRevision`. A stale revision or a nonfloating block is a conflict, not a request to kill the terminal in its new position. These three operations require administrator access. `new_layer` currently accepts **one terminal per floating layer**, not a split tree. Unsupported trees are rejected without starting any children.

## Lua

```lua
local shell = rex.layout.block { flavor = 'com.superlogical.terminal.shell' }
local hidden = assert(rex.session.new_block { layout = shell })
local tool = assert(rex.session.new_layer { window_id = rex.args.window, layout = shell, focus = true })
-- tool.block_id identifies the process; tool.focus identifies a presentation request on one receiver.
-- Check results outside an action, after its batch has actually been published:
if tool.focus.state == 'queued' then
  local result, err = rex.client.result(tool.focus, 5000)
  if not result then error(err) end
  -- result.applied is a prefix length, not a boolean; result.state may still be 'offered'.
end
```

Both local scripts and trusted configuration actions accept explicit `focus=true` on `new_window`, `new_split` and `new_layer`. The default remains nonfocusing. A `client_id` pins the destination; otherwise the invocation's client or a uniquely opted-in receiver is used. The adapter checks the action catalog before it creates the layout. A named client that disappears after preflight is never replaced by another client. A successful launch returns its stable process/window IDs even when an immediate focus request cannot be confirmed: **do not retry the launch to retry focus**. Inside an action, focus is staged and published only after the outer action succeeds. Process launches are not transactional with later script errors. See [receiver results](./rex-handheld-and-delivery.md) and the [Rex Lua reference](https://www.superlogical.com/rex/docs/reference/lua).
