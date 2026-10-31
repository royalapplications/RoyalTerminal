# Rex experiment: operating contracts, client workflows, and recovery

This continuation starts at `e45e2f5`. Review it alongside the [original implementation](./rex-experimental-implementation.md), [placement and observation contracts](./rex-parity-continuation.md), [managed checkpoints](./rex-managed-checkpoints.md), [interactive clients](./rex-interactive-parity.md) and [public Rex research](./rex-product-research.md). The software is experimental, independently implemented and not wire-compatible with Rex.

## Which thing survives which failure?

| Event | What remains | What does not |
| --- | --- | --- |
| Close the browser tab or Avalonia view | The broker's terminal, process, workspace and canonical parser remain. | The closed view's control lease. |
| Lose the client stream | An interactive client can resume retained output, or import a managed parser checkpoint and resume after its sequence. | Unconfirmed input is never replayed. |
| Restart the Linux entry proxy | The independent account broker retains its processes and workspace. | Existing proxy connections; clients must reconnect. |
| Stop the broker | A previously saved archive contains topology and parser state for every captured block. A new broker can recreate the layout with explicitly requested fresh shells. | The old live PTY, PID, process memory, connections, input lease, and share tokens. |
| Lose the filesystem or media | Nothing here supplies off-site backup or an encryption key. | Local archives are not a substitute for an operator backup policy. |

## Native whole-window observation

Select a session in the Avalonia desktop client, then **Workspace overview**. The window selector follows server ordering. Nested horizontal/vertical split ratios and floating rectangles are mapped from normalized coordinates onto the actual client area. Shelved blocks remain named on the shelf; they do not acquire live surfaces. Resizing the overview rearranges the local controls without resizing the remote PTYs.

The overview opens one **observer** attachment per visible block, even with an administrator credential. A layout-only change repositions the existing controls; removal detaches the affected observer. Structural updates use `session.wait_change` rather than polling the screen contents. A failed subscription clears the native panes and selection; reopening establishes a new authorization context. The interactive single-terminal view remains available for explicit control. The overview is not a native drag-to-edit layout authoring surface.

## Terminal activity without taking control

`terminal.events` uses a separate per-block cursor, not the VT byte-stream sequence or the session layout revision. The server now publishes `terminal.pwd_changed`, `terminal.desktop_notification`, and `terminal.progress_report` by consuming the existing managed parser callbacks, alongside bell, title, program-status changes and exit. These records are observations: a directory is not opened or resolved, and the server does not show desktop notifications. Notification title/body limits are 512/2,048 UTF-16 units; reported directories are limited to 4,096. Truncation does not split a surrogate pair. Byte and event limits both participate in history eviction.

In the browser command palette choose **Watch terminal activity** on a focused terminal. A new subscription first takes a start-at-now cursor, so old alerts are not mistaken for new ones. **Read recent terminal activity** shows up to 64 entries from this tab. **Stop terminal activity alerts** clears them. A revoked grant, missing history, malformed cursor, or changed workspace does not silently deliver a truncated or stale batch. Reported terminal text is handled as text, not markup.

**Enable private device alerts** is an explicit permission gesture. Device notifications use a fixed message without terminal titles, text, session IDs or credentials. Notifications are throttled; there is no service-worker fetch interception, remote push endpoint, stored credential, or closed-app background subscription. Browser alerts need a running page and platform support. Desktop Chromium with a mobile viewport is not physical iOS acceptance.

## Server-owned Lua actions

The existing local `lua`/`do` CLI runs trusted client scripts. Separately, a host operator can select an absolute local configuration path in `ROYALTERMINAL_MUX_LUA_INIT` and an absolute interpreter path in `ROYALTERMINAL_MUX_LUA_EXECUTABLE`. Both are read at startup. Without an init file the server does not register the `lua.*` methods. Put the program and configuration in directories not writable by untrusted users. Install Lua 5.1 and the system `dkjson` module; the child does not inherit custom `LUA_PATH`, `LUA_INIT`, or bearer credentials.

```lua
rex.action {
  name = 'work.rename',
  run = function(ctx, args)
    local old = assert(rex.session.view())
    local new = assert(rex.session.set_label {label=args.label, expected_revision=old.revision})
    return {session=ctx.session_id, before=old.label, after=new.label}
  end
}
```

After starting the server, authenticate as an administrator:

```sh
royalterminal-mux api call lua.actions '{}'
royalterminal-mux api call lua.invoke '{"name":"work.rename","sessionId":"SESSION-GUID","arguments":{"label":"Release"}}'
royalterminal-mux api call lua.reload '{}'
```

`lua.actions` returns the names and a SHA-256 content-generation ID. Startup validation and reload cannot invoke host methods. Reload reads the configured file, validates a complete new generation, and publishes it with one reference exchange; failed reload retains the previous generation. In-flight calls keep their immutable source. Remote requests never select a path or supply program text. `rex.call` dispatches through the same catalog under the caller's grant. Effects are sequential, not transactional, and a failed action does not replay or roll back completed effects. A local client script can call `rex.invoke` for a configured action; a failed **local** action is never retried remotely under the same name.

At most four invocations run simultaneously, with up to 32 host calls each. The validation deadline is 5 seconds and invocation deadline is 20 seconds. Source is limited to 128 KiB and a worker reply to 256 KiB. A worker is terminated on deadline, protocol failure or cancellation. Each invocation has a fresh Lua VM: no persistent globals, server-side background timer or general `os`/`io` subprocess surface. This is trusted operator configuration, **not a hostile-code sandbox or an OS memory limit**. Client-side scripts retain their separate action/timer/subprocess behavior.

## Archive and recovery procedure

```sh
# Online: any credential that can read the session; no control lease is needed.
royalterminal-mux archive save Work ./work.rtw
# Offline, including after the old server has stopped:
royalterminal-mux archive list ./work.rtw
royalterminal-mux archive view ./work.rtw BLOCK-GUID
royalterminal-mux restore-layout ./work.rtw --plan
# Online: administrator of the NEW host. Starts one new default PTY shell per archived block.
royalterminal-mux restore-layout ./work.rtw 'Recovered work' --fresh-shells
```

Capture reads the session revision, captures one binary checkpoint per block, then verifies that the structural revision is unchanged. Blocks have individual cut sequences: this is not a simultaneous snapshot of multiple processes. A new archive appears only after the whole capture succeeds; an existing file is left untouched unless `--replace` is explicit. Use a private trusted directory. File contents can include terminal secrets in past output. **Not encrypted**. POSIX creation mode is 0600; use a suitable directory ACL on Windows. No password, bearer, process command line or environment is intentionally serialized; sensitive content visible in a terminal is not redacted.

Format v1: eight-byte magic/version `RTMUX 00 01 0A`, little-endian 32-bit metadata length, UTF-8 session JSON, then one record per block in manifest order. Each 33-byte header contains the network-order block UUID, little-endian 64-bit event sequence, one-byte exit-present flag, 32-bit signed exit code, and 32-bit payload length. Each payload is a complete managed-compatible Ghostty v1 parser snapshot. A SHA-256 footer covers all preceding bytes. It detects corruption; it is not a signature. Readers reject mismatched identities, duplicate blocks, oversized payloads, truncation and trailing data. Bounds: 1 MiB manifest, 8 MiB per block, 128 blocks and 128 MiB total. No archive-supplied filename is extracted or executed.

`restore-layout --plan` validates before any network operation. `--fresh-shells` is deliberately required for execution. The command creates a **new session** with new IDs, preserves tab ordering, labels, split ratios, floating coordinates and shelved blocks, and emits the old-to-new ID mapping. It never copies the former commands, environment, working directories or keystrokes into a process. The host's own default shell initialization still applies. Historic screens remain accessible with `archive view`; they are **not** spliced into the fresh shells. Creation uses several API operations, not a distributed transaction. Interruption reports the known partial session ID; the client does not automatically retry mutations or terminate processes another administrator might have attached to.

## Account and network trust boundaries

The personal Linux service, per-OS-account login service, and private tailnet gateway are distinct deployment profiles. The account profile uses local kernel socket ACLs, distro PAM account/session policy, and one non-root broker per authorized OS account. The tailnet gateway accepts a trusted local root peer, checks an explicit Tailscale login allowlist and still requires the normal RoyalTerminal bearer for workspace API/stream access. An identity header is not a workspace controller token. Neither profile is enabled by default. See `deploy/account-login/README.md` and `tools/workspace-tailnet/README.md` for provisioning and revocation steps.

## Acceptance boundaries

The CI suites separate managed tests on three OSes, real Chromium/PTY flows, genuine Lua workers, cold two-user Linux activation/PAM denial, tailnet-gateway Unix peer tests, and packaging. Operator-owned production Tailscale identities, remote TLS policy, physical mobile notification delivery, signed/notarized distribution and native mobile apps are not established by these jobs. Documented Rex functionality beyond these enumerated interfaces should not be inferred from the feature names.
