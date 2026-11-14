# Workspace editing, mobility, Lua and local distribution

This supplements the previous [operating contracts](./rex-operations-and-clients.md). RoyalTerminal is an independent host and protocol. The reference is Superlogical’s public [Lua API](https://www.superlogical.com/rex/docs/reference/lua), [CLI](https://www.superlogical.com/rex/docs/automate/cli), [actions](https://www.superlogical.com/rex/docs/reference/actions), and [keybinding behavior](https://www.superlogical.com/rex/docs/customize/keybindings). A reproduced workflow is not proof that an arbitrary Rex script, client, or server is interchangeable.

## Author a workspace without taking input

In the native Avalonia viewer, open the workspace overview, select a window and choose **Edit layout** using an administrator credential. Drag a tiled terminal onto another tile’s edge to split beside it; drop in its central half to swap. Drag a floating pane to move it, or Shift-drag to resize. Escape cancels without a network mutation. The toolbar exposes the same operations without using a pointing device. It also shelves/restores blocks, moves them between windows, grows/shrinks the selected tile’s nearest split, creates shells/windows, reorders tabs and renames blocks/windows. A nonempty window needs an explicit preservation destination before closing.

A gesture captures the window, revision and initial dimensions. A changed revision or size invalidates it instead of guessing which target the user intended. Editing produces at most one management mutation at release. Observer sockets do not become controllers. An ordinary sharing capability can continue to watch but cannot edit. A successful HTTP response does not substitute for a new authoritative view from the session subscription.

## Define a reusable process layout

`rex.layout.block`, `horizontal`, and `vertical` build plans without I/O. Both trusted `init.lua` actions and CLI scripts can submit a plan. Example for a Unix server:

```lua
local function shell(label, argv)
  return rex.layout.block { flavor = 'com.superlogical.terminal.shell', label = label,
    options = { command = argv, shell = 'none', exit = { on_completion = false } } }
end
rex.action { name = 'review', run = function(ctx)
  return assert(rex.session.new_window { session_id = ctx.session_id, label = 'Review',
    layout = rex.layout.horizontal(0.6, shell('Git', {'git', 'status'}), shell('Shell', {'/bin/sh'})) })
end }
rex.mode('review', {exclusive = true})
rex.bind('ctrl+b>r', 'client.mode.enter', {name = 'review', once = true})
rex.bind('review/r', 'review')
```

`session.launch_layout` accepts up to 16 shell leaves. It validates the whole description, including literal argument arrays, layout ratios, working directories and supported exit policy, before starting a child. Each creation checks the structural revision under the broker mutation lock. New leaves are returned in depth-first order. Splitting against a tile preserves unrelated subtrees. On a failure the operation tries to dispose only blocks it acquired; an unexpectedly populated new window is preserved. Launch is **not** a transaction across child processes: filesystem effects or already-delivered explicit initial input cannot be undone. There is no automatic retry of a possibly applied launch.

## Inspect and reload keyboard configuration

With trusted Lua enabled on the host and the CLI authenticated as administrator:

```sh
royalterminal-mux keymap --mode review  # inspect published declarations
royalterminal-mux config check         # parse the operator's current file, no publication or callback invocation
royalterminal-mux config reload        # publish all validated actions and bindings or leave the old set intact
royalterminal-mux send-key Work Shell up ctrl+c
royalterminal-mux events Work Shell --after 0 --count 3
```

Use the browser palette’s **Reload server Lua shortcuts** to fetch and apply the operator's configuration. `rex.bind`, `rex.unbind`, `rex.bindings`, `rex.mode`, and `rex.modes` are configuration-scope functions. Anonymous handlers run newest first until one returns true. Numeric callback IDs are meaningful only with the originating SHA-256 configuration generation: dispatch from a stale client is rejected before a different callback can run. Unknown actions are reported, not interpreted as arbitrary shell commands. Supported browser actions include pane split, zoom, send-key, tab creation/navigation and mode enter/exit. This is not the entire Rex client action catalog; in particular physical-key interpretation and arbitrary passthrough keys are not asserted compatible on all national keyboard layouts.

The CLI `send-key` encodes a bounded batch with the server’s live VT keyboard mode and requires a free exclusive control lease. A browser already holding the lease asks `terminal.encode_keys` for the bytes, then uses its original WebSocket; a focus or lease change aborts the send. The `events` command emits compact NDJSON for one block; its metadata cursor does not name a VT output byte or a structural revision. Without `--after`, it starts at now. Expired history is an error, never silently skipped.

## Native phone clients and notifications

The `Mobile.Android` and `Mobile.iOS` sample projects share a compiled-XAML view, the managed terminal control, checkpoint importer and reconnect controller. Supply a trusted HTTPS host and an administrator or scoped token. Choose **Observe** or explicitly **Take control**. Observers cannot use the touch input strip. Removing the view cancels outstanding operations, disposes its terminal and clears in-memory credentials. A phone shell is remote; the app does not spawn an OS shell on the device. These projects are opt-in so ordinary repository builds do not require Android/iOS workloads.

The browser separately supports user-visible, empty-body Web Push. Generate a dedicated P-256 signing key outside the web root, and configure the host before starting it:

```sh
umask 077
openssl ecparam -name prime256v1 -genkey -noout -out workspace-vapid.pem
export ROYALTERMINAL_MUX_PUSH_KEY="$PWD/workspace-vapid.pem"
export ROYALTERMINAL_MUX_PUSH_CONTACT='mailto:operator@example.org'
```

Use **Enable background push for this terminal** on a secure origin. On iOS use the Home Screen web app. The registered service worker shows generic notification text; it does not fetch or cache private workspace state. The broker sends an empty signed request to an allowlisted provider origin, never a workspace token, title or shell output. The provider can still see the endpoint, timing, audience and public VAPID identity. Watches are memory-only, limited to eight per grant and 64 per broker, bounded by grant validity and 24 hours, coalesced and removed after permanent delivery failure. Disabling via the palette unsubscribes the device. Revocation prevents new broker deliveries, but cannot recall one accepted by a provider. Browser Web Push is not a native APNs/FCM SDK integration. Provider delivery, physical-device power policy and lock-screen behavior require separate validation with real devices.

## Local macOS app and signing

On macOS, `WORKSPACE_CODESIGN_IDENTITY=- bash deploy/macos/package.sh /absolute/new/output` builds a self-contained architecture-specific `.app` with its own desktop client, PTY helper, broker and CLI. Opening the app starts or reuses a per-user launchd broker behind a private Unix socket. Local clients never fall back to a TCP listener. `Contents/MacOS/Workspace cli sessions` addresses that broker. Closing the UI leaves it running; `Contents/MacOS/Workspace stop` is an explicit destructive shutdown. Install in a stable location; moving/replacing a running bundle does not migrate its processes. Back up private work before stopping. This personal bundle does not configure a password login service, public browser endpoint or automatic login item.

For operator distribution, select a Developer ID Application signing identity; optional notarization additionally requires an explicitly configured `WORKSPACE_NOTARY_PROFILE`. The macOS script signs inner Mach-O files before sealing the app; only JIT apphosts receive the JIT entitlement. `deploy/signing/Sign-Workspace.ps1` signs the Windows launcher plus a catalog for the entire payload. `deploy/signing/workspace-manifest.py` signs or verifies a canonical archive name, size, digest and full source revision using an independently provisioned key. These controls authenticate provenance, not the correctness of a program. PR jobs use temporary/test identities only. They do not produce publisher-trusted, store-approved or notarized releases.

## Acceptance boundaries

The draft's checks distinguish headless role and pointer tests, real OS-child/PTY tests, Chromium-service-worker tests, emulator/simulator launches, disposable-certificate validation and release signing. A green build is not a device launch, and a simulated provider callback is not a real provider delivery. Current test evidence is attached to the PR. Remaining review includes the complete cross-client action/event surface, physical-key mappings and input methods, archive protection, FreeBSD deployment, distribution trust, real tailnet enrollment and physical-device/provider acceptance. There is no live-process restoration after reboot and no claim of Rex wire compatibility.
