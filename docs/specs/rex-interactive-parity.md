# Interactive recovery, local Lua and agent integration

This update supersedes the corresponding limitations in the [initial inventory](./rex-product-research.md), [continuation](./rex-parity-continuation.md) and [checkpoint design](./rex-managed-checkpoints.md). RoyalTerminal implements independent workspace workflows; this is not a Rex client or wire protocol.

## Choosing a recovery client

| Client | What happens when the replay prefix is no longer retained | What it does not do |
| --- | --- | --- |
| Avalonia desktop | Imports the managed screen and parser together, restores display and input-mode bindings, then reattaches at the checkpoint cursor. Handles both a new view and an existing view that lost its connection. | No resend of queued user input, no process restart, and no implication that the desktop shell implements the browser's entire multi-pane workspace. |
| Browser | Normally uses xterm.js alone. After `HistoryGap`, the page obtains a binary checkpoint and a lazy same-origin Web Worker imports it in the managed Wasm VT engine. All subsequent raw bytes and resizes go into that engine. xterm.js presents the recovered live viewport. | Does not import a binary Ghostty snapshot into xterm.js, reconstruct evicted scrollback inside xterm.js, or grant input to an observer. No background push when the page is closed. |
| CLI mirror | Restores a fresh managed parser without needing a display. `mirror SESSION BLOCK --wait` observes suffix events until process exit. | Does not acquire a controller lease or keep processes alive across server shutdown. |

From the root, install the .NET 10 `wasm-tools` workload, run `npm --prefix samples/RoyalTerminal.Multiplexing.Server ci` and `npm --prefix samples/RoyalTerminal.Multiplexing.Server run build`, then start the server. `build:stream-only` builds only xterm assets; a deployment without the managed worker assets cannot recover a browser after an expired prefix. The browser receives the credential in the page, not in the worker or a URL. Do not lower the whole document's CSP to enable the worker.

## CLI scripts and subprocesses

```sh
# No host token is required for entirely local calculations or local subprocesses.
royalterminal-mux do -e 'return {squared=rex.args.n^2}' n=7
printf '%s\n' 'return rex.args' | royalterminal-mux do - --args '{"n":7,"label":"07"}'
royalterminal-mux do ./tasks.lua --action verify --args @arguments.json
# Bare names retain the earlier server-configured action behavior.
royalterminal-mux do capture_focused Work Build
```

The `do` command prints the returned JSON value to stdout; script logs go to stderr. Use `--file NAME` or `./NAME` to distinguish a local file from a server-side action, and `--args` for literal strings that look numeric. The original `lua FILE [ARGS...]` mode remains available. Local scripts use an external Lua 5.1 interpreter and `dkjson`. They are **trusted code**, not an isolation boundary. They are not uploaded to the server.

```lua
local child = assert(rex.exec.start({'/bin/cat'}, {stdin=true, text=false}))
assert(child:write('A' .. string.char(0,255) .. 'Z'))
assert(child:close_stdin())
local result = assert(child:wait(2))
assert(result.ok and #result.stdout == 4)
return {exit=result.code, bytes=#result.stdout}
```

`rex.exec` and `rex.exec.start` launch argv directly on **the machine running the CLI**. Use a server workspace block for durable or remote processes. Stdin can be closed separately. Stdout/stderr are tracked separately and delivered as bounded pages; callbacks are dispatched while the handle is polled by `wait`. A wait timeout does not silently relaunch the child. Per-invocation child ownership, deadlines, a 16-handle limit and a 256 KiB per-stream capture budget are intentional. Detached local children are rejected. Signal semantics vary by OS. Workspace calls still require an explicit capability.

The Lua adapter reads supported block events from `terminal.events`, not by consuming the raw stream. Bell, title, child exit and program status are supported; other event families are not implied. The live smoke checks a listener registered before a BEL is emitted, preserving read-only observation independently of control ownership.

## Installing the local agent guide

```sh
royalterminal-mux skill setup --project --agent codex --dry-run --json
royalterminal-mux skill setup --project --agent codex
royalterminal-mux skill status --project --agent codex --json
royalterminal-mux skill remove --project --agent codex
```

Supported setup targets are `claude`, `codex`, `opencode` and `pi`. Claude has its own guide; the other three share the same `.agents/skills` entry. Removing the shared guide affects those three consumers. Startup hooks are only emitted for Claude and Codex; `--no-hooks` installs only the guide. The generated hook reads the invoking process's session and block GUIDs and its **launch** window GUID, never its bearer. Look up the current placement before moving a block: processes do not get a new environment when their block moves. Setup is an explicit file operation, not agent enrollment, a model integration or proof of behavior in every external agent version.

Use `--dry-run` to inspect changes. Setup refuses symlink targets and unrelated skill files unless the latter are explicitly overridden with `--force`. Removal preserves unrelated configuration and does not delete a user's surrounding directory. Back up hand-edited agent settings normally. Hook commands point to the installed CLI: rerun setup after relocating it.

## Distribution and remaining work

The desktop preview workflow cross-publishes **unsigned, framework-dependent** launchers and dependencies for `linux-x64`, `win-x64`, `osx-x64` and `osx-arm64`, plus the corresponding source and checksums. These are preview archives, not signed/notarized installers. The Linux personal host is a separate bundle with a private Unix socket and a user service. No release is published by the PR workflows.

Open areas include native/mobile workspace composition and notification UX, full server-side Lua configuration and event coverage, multi-account login and PAM isolation, Tailscale discovery and identity, and signed distribution. Recovering a live terminal is distinct from recovering arbitrary child processes after host shutdown. Neither the test suite nor the public Rex inventory justifies claiming full product parity.
