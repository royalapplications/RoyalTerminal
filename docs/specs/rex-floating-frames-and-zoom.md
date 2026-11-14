# Floating split frames and explicit window zoom

A frame is one window-owned rectangle and one place in z-order, with its own binary split tree. Its terminals keep their original processes, streams and access grants. Zoom is presentation state on the window; it is not a change to that tree or a request for a larger terminal grid.

## Lua and wire example

```lua
local function shell(label)
  return rex.layout.block { flavor = 'com.superlogical.terminal.shell', label = label }
end
local f = assert(rex.session.new_layer {
  window_id = rex.window_id,
  bounds = { x=1000, y=1000, width=8000, height=8000 },
  layout = rex.layout.horizontal(.60, shell('Editor'),
    rex.layout.vertical(.40, shell('Build'), shell('Logs')))
})
local logs = f.block_ids[3]
assert(rex.session.zoom_block { block_id=logs, zoom=true })
assert(rex.session.zoom_block { block_id=logs, zoom=true }) -- no revision change
assert(rex.session.zoom_block { block_id=logs, zoom=false })
-- When finished: assert(rex.session.close_layer { layer_id=f.layer_id })
```

`block_ids` is in depth-first layout order. A compound `layer_id` survives removal of its first leaf. A standalone floating terminal retains its legacy one-block receipt. `session.resize_layer`, `raise_layer` and `lower_layer` act on whole frames; `resize_pane` changes the nearest matching divider. A strict tree replacement (`session.set_layer_layout`) must retain exactly the same leaves. Closing one terminal collapses its empty divider. Closing the frame stops only its current members. The broker rejects stale structural revisions, duplicate members and interleaved stack order. Nested launches preflight geometry and templates, stage terminals on the shelf (not as visible tiles), and publish the frame as one mutation. External side effects of programs that have already started cannot be rolled back. Plain and encrypted archive restore remap block, frame and window identities to fresh shells.

`session.zoom_block` is an administrator operation in the host API: `{sessionId,blockId,zoom?,expectedRevision?}`. Omitting `zoom` toggles. Repeating an explicit value does not change revision. Clearing A never clears B's zoom; shelving, moving away or closing the zoomed terminal clears that window's target. A stale `expectedRevision` still fails. `session.view` exposes `windows[].zoomedBlockId`; observers can read it. The browser and native overview follow it without a controller upgrade. Desktop edit mode uses unzoomed geometry. The older per-client `pane.zoom` is a separate local toggle and is not a substitute for the shared setter.

## Phone and hardware acceptance

On the native Android or iOS client, connect over HTTPS, observe a terminal, then tap **Follow**. The phone follows zoom in that terminal's window, replaces the visible observer when necessary, and returns to the original terminal on restore if it still exists. It does not take control, resize a process or activate a different OS application. **Unfollow**, local zoom, detach, and disconnect let the operator leave the mode.

The default PR check remains one Ubuntu runner. It exercises nested PTYs, duplicate true/false zoom, scoped denial, close, archive reconstruction, native headless surfaces and browser model tests. For physical-device evidence, use a dedicated test broker with a trusted certificate, a workspace client built from the commit under test, and an unlocked, paired device. Android requires `adb`; iOS requires `idevice_id`, `ideviceinfo`, and a working screenshot service (`idevicescreenshot` and appropriate developer support). Install the native client separately. No cloud enrollment or paid device service is started by this test.

```sh
# ROYALTERMINAL_MUX_TOKEN is an administrator token in the private operator shell only.
python3 scripts/workspace/physical-frame-check.py android --serial "$ANDROID_SERIAL" \
  --url "$BROKER_HTTPS" --build "$(git rev-parse HEAD)" --out out/hardware-android
python3 scripts/workspace/physical-frame-check.py ios --serial "$IPHONE_UDID" \
  --url "$BROKER_HTTPS" --build "$(git rev-parse HEAD)" --out out/hardware-ios
```

The script gives the handset a temporary observer token, asks the operator to read randomized terminal codes, captures three device screenshots, and checks server attachment roles and geometry. It closes its disposable session and revokes the token. Only a completed hardware run writes `result.json`; merely building the app or passing a simulator/headless test is not physical-device evidence. Screenshots can contain device UI: review before sharing. The commit in the record is supplied by the operator, not a hardware attestation.
