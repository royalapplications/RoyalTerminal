# Experimental native multiplexer workspace

This runnable Avalonia sample attaches RoyalTerminal's native control and managed
VT engine to the experimental durable multiplexer host. It is separate from the
stable product shell and requires no libghostty binary.

## Run

Start `samples/RoyalTerminal.Multiplexing.Server` in a separate process. It listens
on `http://127.0.0.1:7331/` by default and uses `ROYALTERMINAL_MUX_TOKEN` for the
administrator credential. Then run:

```sh
dotnet run --project samples/RoyalTerminal.Multiplexing.Desktop
```

The desktop sample reads optional `ROYALTERMINAL_MUX_URL` and
`ROYALTERMINAL_MUX_TOKEN` values at startup. You can instead enter the URL and
token in the window. Credentials remain in memory and are never persisted by the
sample. Use HTTPS or a loopback SSH tunnel when connecting to a remote host.

1. Click **Refresh sessions** to list sessions visible to the supplied capability.
2. Select a session and terminal block, or enter their GUIDs directly. Manual
   identities also work with a scoped sharing token.
3. Leave **Observe only** selected for read-only viewing, or clear it to request
   the exclusive input/control lease. The host remains the permission authority.
4. Click **Attach**. Historical dimensions and bytes replay before live work.
5. Type into the terminal when controlling. Selection and scrolling are available
   to observers without granting them input rights.
6. Click **Detach** or close the window. The process continues on the server.

An administrator can create a named session with **+** and start its default
server shell with **+ Shell**. These actions have no local process-launch side
effects. Arbitrary commands and argument vectors remain available through the
CLI and introspected API.

## Shared terminal dimensions

The terminal's parser grid belongs to the server. Resizing the local application
window pans/clips/scrolls the surface; it does not resize the remote process. A
controller can explicitly enter a column/row count and select **Resize remote**.
The local parser changes only when the server publishes that resize in stream
order. Observers cannot issue resize requests.

`RemoteTerminalControl` reuses the base control's atomic layout update rather
than assigning `Columns` and `Rows` separately. It uses an exact fixed grid based
on the current renderer's cell metrics. This avoids transient mixed-size reflows
and prevents a viewer with a smaller monitor from altering another user's TUI.

The native sample rejects grids larger than 4,096 on either axis or 1,048,576
viewport cells. Its history limit is adjusted by column count, with at most
10,000 retained rows, so an unusually wide remote grid cannot multiply the local
history allocation without a bound. These are client resource limits, separate
from the host's replay journal budget.

Every authoritative resize first flushes preceding queued output, then applies
the new grid on the UI thread, and finally applies server pixel dimensions to
the VT processor. The WebSocket reader waits for that ordered operation before
delivering later bytes. Native Headless tests assert actual resulting cell text
across a width change, not just the final dimension properties.

## One owner of terminal protocol replies

The host's inspection engine owns DSR, DA, and other terminal-query responses.
`RemoteTerminalSessionService` suppresses local VT response callbacks for both
controller and observer clients, during replay and live output. It separately
forwards controller keystrokes and paste through the existing session input path.
This prevents every viewer from answering the same query while preserving normal
user input. Automatic local layout resize requests are similarly suppressed.

## Lifecycle and architecture

- `MainWindow` contains only compiled XAML and `InitializeComponent()` code-behind.
- `WorkspaceViewModel` uses `ReactiveObject`, `ReactiveCommand`, observable state,
  and UI-independent service interfaces.
- `WorkspaceClient` uses the source-generated management API shapes.
- `WorkspaceTerminalController` owns attachment subscriptions and UI dispatch.
- `RemoteTerminalSessionService` decorates the existing service; it does not
  replace transport or parser implementations.
- The app startup is the lifetime/composition root. Window closure awaits network
  detach before synchronous control cleanup, avoiding dispatcher/read-loop
  deadlocks. Neither path deletes a server session or block.

Each new attachment performs a fresh ordered replay from cursor zero into a
cleared local terminal. A server retention gap is shown as an error; the sample
does not display an arbitrary byte tail as a reconstructed screen. Server restart
does not preserve live OS processes in this experiment. More extensive desktop
workspace/window composition, automatic preserved-state reconnect, and native
mobile clients remain distinct work beyond this sample.

## Validation

```sh
dotnet test tests/RoyalTerminal.Multiplexing.DesktopTests -c Release
```

Avalonia Headless tests cover compiled view loading, session selection and manual
scoped identities, visible management errors, controller/observer commands,
geometry/output ordering, local-window isolation, suppression of live VT query
replies, and preservation of explicit user input. The sample also builds its
compiled XAML bindings with the repository's warnings-as-errors configuration.
