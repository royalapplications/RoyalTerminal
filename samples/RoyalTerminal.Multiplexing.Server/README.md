# Experimental durable terminal workspaces

This opt-in sample implements a server-owned terminal workspace with a browser,
a native Avalonia client, an automation CLI, and reusable .NET components.
Processes continue when a client detaches, changes windows, or closes. **Keep the
server process running:** this experiment does not restore processes after a
server crash, shutdown, or reboot.

The design was informed by Rex's public documentation. It uses RoyalTerminal's
own implementation and experimental protocol, not Rex's private protocol or
proprietary implementation. See the [product research](../../docs/specs/rex-product-research.md)
and [detailed implementation specification](../../docs/specs/rex-experimental-implementation.md)
for evidence, contracts, acceptance requirements, and the remaining parity gaps.

## Start the server and browser

Prerequisites: the repository's .NET 10 SDK, Node.js 22 or later, and an ordinary
interactive shell supported by the host's PTY implementation. The workspace
sample uses the managed VT engine; it does not need a native Ghostty binary.

From the repository root:

```sh
npm --prefix samples/RoyalTerminal.Multiplexing.Server ci
npm --prefix samples/RoyalTerminal.Multiplexing.Server run build
dotnet run --project samples/RoyalTerminal.Multiplexing.Server -c Release
```

Open <http://127.0.0.1:7331/> and paste the generated administrator token printed
in the server console. That bootstrap token is shown once per server start when
no configured token is supplied. Treat the console output as a credential. For
unattended operation, supply `ROYALTERMINAL_MUX_TOKEN` through your process's
secret/environment configuration so no generated token is printed.

The npm build copies the pinned xterm packages and their licenses into the local
static assets. It downloads no code at browser runtime. Build those assets before
building or publishing the .NET host; the host copies `wwwroot` to its output.

1. Select **New workspace**, enter a name, and start its first shell.
2. Type normal commands. Use **Split** for another terminal and drag the divider
   to change the shared layout. Double-click a terminal or window name to rename.
3. Use **New window** for another layout in the same session.
4. Use **Zoom** or the command palette (`Ctrl+K` / `Cmd+K`) to focus one terminal.
5. Use the pane's **◇** button to detach that view. Reconnect with the same button.
6. Close the browser and connect again while the server remains running.

The browser also provides text/HTML/VT capture, program status inspection, custom
actions, share creation and revocation, and touch controls on narrow screens.
`Ctrl+B`, then `%`, `"`, `c`, or `d` invokes a split, new window, or detach command.
The **×** button explicitly terminates a process; closing a browser tab detaches.

Up to 32 browser terminal views remain cached with live subscriptions while
navigating between sessions and windows. They keep consuming ordered output even
when hidden. Above that bound the oldest inactive view is disposed. A completely
new or evicted view requires the server's complete retained event prefix.

## Native client and CLI

The [native desktop sample](../RoyalTerminal.Multiplexing.Desktop/README.md) reuses
RoyalTerminal's managed VT engine and Avalonia terminal control:

```sh
dotnet run --project samples/RoyalTerminal.Multiplexing.Desktop
```

Enter the server URL and token, select a session/block, and attach as an observer
or controller. Local window resizing does not change a shared terminal's grid;
the controller uses the explicit remote-resize control.

The [CLI](../RoyalTerminal.Multiplexing.Cli/README.md) reads its token from
`ROYALTERMINAL_MUX_TOKEN`. Set that variable to the server's credential in the
shell where you run these commands:

```sh
dotnet run --project samples/RoyalTerminal.Multiplexing.Cli -- sessions
dotnet run --project samples/RoyalTerminal.Multiplexing.Cli -- new Work
dotnet run --project samples/RoyalTerminal.Multiplexing.Cli -- run Work --label Build -- /bin/bash
dotnet run --project samples/RoyalTerminal.Multiplexing.Cli -- capture Work Build
dotnet run --project samples/RoyalTerminal.Multiplexing.Cli -- api describe block.create
```

Use an executable appropriate for the server OS. `run` preserves the argument
vector after `--`; add `--pipe` for redirected process streams. Selectors accept
exact IDs, case-insensitive labels, one-based positions, or unique ID suffixes of
at least four characters. Ambiguity prevents a mutation.

## Actions and key modes

Load the included configuration to add a build shell and capture action:

```sh
dotnet run --project samples/RoyalTerminal.Multiplexing.Cli -- config load samples/RoyalTerminal.Multiplexing.Server/automation.example.json
dotnet run --project samples/RoyalTerminal.Multiplexing.Cli -- actions
dotnet run --project samples/RoyalTerminal.Multiplexing.Cli -- do capture_focused Work Build
```

Reload actions from the browser command palette after an external configuration
change. The example binds `Ctrl+G` to an exclusive one-shot workspace mode; `b`
opens a build shell and `s` captures the focused terminal. The palette also offers
**Edit actions and key modes**, which validates and replaces the configuration.

Actions are versioned JSON sequences of named API methods. Exact string values
`${sessionId}`, `${blockId}`, and `${windowId}` use the invocation context. All
context substitution and advertised argument validation precede the first call.
Runtime ownership, revision, layout, and process checks still happen in each
handler. A later failure can leave earlier completed effects; the error reports
the failed step and completed results. Actions are never retried automatically.

Set `ROYALTERMINAL_MUX_CONFIG` to a file path to persist configuration through
validated atomic replacement. Without that variable, configuration lives only in
memory. This persistence covers actions and key bindings, not sessions, terminal
history, or operating-system processes. The implementation does **not** embed Lua.

## Configuration

| Variable | Default | Purpose |
|---|---|---|
| `ROYALTERMINAL_MUX_URL` | `http://127.0.0.1:7331` | One HTTP(S) listening endpoint; clients use the same variable for their endpoint. |
| `ROYALTERMINAL_MUX_TOKEN` | Random at startup | Administrator bearer token, at least 32 characters. |
| `ROYALTERMINAL_MUX_ALLOW_REMOTE` | Unset | Set to `1` to explicitly permit a non-loopback listener. |
| `ROYALTERMINAL_MUX_CONFIG` | Unset | Optional persistent JSON actions/keymap path. |

Keep the default loopback endpoint for local experiments. For remote use, an SSH
port-forward preserves the tested same-origin loopback deployment. Direct remote
listeners require a trusted TLS configuration and deliberate remote opt-in.
TLS-terminating proxies must preserve the host's same-origin checks; proxy trust
and forwarded-header configuration are not automatically enabled by this sample.

The host starts processes as its own OS user. It supports PTYs and process pipes;
the reusable broker can wrap other registered `ITerminalTransportFactory`
implementations, but the sample does not expose an unrestricted transport plug-in
loader or SSH/Tailscale discovery service.

## Sharing and trust

An administrator can create a session-scoped observer or controller capability
with a lifetime of up to 24 hours. Observer capabilities may list/read their
session, inspect its terminals, and receive output. They cannot send input,
resize, launch/close processes, change configuration, or administer sharing.

Controller capabilities can acquire the single input/resize lease for a block.
**Controller access permits commands as the server's OS account, including access
to its files and other processes.** Session scoping is an API permission boundary,
not process isolation or a multi-tenant sandbox. Give control only to people you
trust with that account. The host clears its administrator token from the direct
child environment as defense in depth; that does not isolate same-account users.

Tokens stay out of URL query strings. Share links use a fragment, which the
browser immediately removes from history. The browser holds credentials only in
memory. Native clients use bearer headers; browser WebSockets authenticate in
their first frame. Cross-origin requests are rejected. Revoking a capability
also disconnects its registered clients; expiry closes its active stream.

## Protocol and correctness boundaries

- The broker owns process lifetime, IDs, session revisions, layouts, and a bounded
  event journal. A view disconnect releases an attachment, not a process.
- A server-side managed VT engine owns terminal query replies, canonical
  geometry, title, working directory metadata, OSC 7501 status, and captures.
- A block has at most one controller and additional read-only observers. Input
  and resize commands are checked server-side; the UI cannot grant itself rights.
- Resize, output, exit, and close events have a strict per-block sequence. A new
  subscription records an atomic replay fence; `ready` follows applied history.
- Replay defaults to 8 MiB or 4,096 events per block. A missing prefix fails with
  `HistoryGap`; a byte suffix is not treated as a reconstructed terminal screen.
- The browser preserves its last applied cursor across a connection interruption.
  The native sample performs a fresh replay on each explicit attachment.
- Server captures do not form a checkpoint-plus-live recovery protocol. Long
  histories can therefore outlive the ability of a new client to reconstruct them.
- Input is never automatically retried. A disconnect during a write can leave an
  uncertain outcome; inspect the process before deciding to resend.

The browser's input-origin adapter uses one pinned xterm 6.0.0 internal signal to
distinguish real keyboard/paste/IME input from parser-generated VT replies. The
adapter fails closed when that contract is absent. Its real-xterm IME/query test
must pass when upgrading xterm. Native clients suppress VT replies through their
session-service adapter. All clients receive the server's authoritative resize
sequence before subsequent output.

Discovery is available at authenticated `GET /api/methods`, and named methods run
through `POST /api/call`. The implementation spec contains route contracts,
schema projection rules, frame examples, limits, failure semantics, and the full
feature comparison. This protocol is experimental and is not Rex wire compatible.

## Build and verify

```sh
dotnet test tests/RoyalTerminal.Multiplexing.ServerTests -c Release
npm --prefix samples/RoyalTerminal.Multiplexing.Server test
dotnet build samples/RoyalTerminal.Multiplexing.Server -c Release
cd samples/RoyalTerminal.Multiplexing.Server
npx playwright install chromium
npx playwright test
```

Playwright starts a separate server with a test token on loopback port 7331;
stop any existing sample server on that port first. It exercises actual PTY
processes and browser input, layout changes, detach/reconnect, sharing, and IME.
Use `PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH` only when intentionally supplying a
compatible local Chromium executable. The dedicated experimental workflow runs
the .NET projects on Linux/macOS/Windows and browser flows on Linux.

The broker, inspection, automation, client/CLI, and native UI also have separate
focused test projects. Stable package release workflows are unchanged. The sample
is experimental: Lua, floating/detached-layout blocks, cross-window move/swap,
system login/PAM servers, snapshot resynchronization, and native mobile UX remain
explicitly outside this implementation.
