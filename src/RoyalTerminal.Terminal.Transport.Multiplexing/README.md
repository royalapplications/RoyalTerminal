# Experimental multiplexer client

This package adds an opt-in `ITerminalTransport` provider for an existing block
owned by the experimental RoyalTerminal multiplexer host. It does not create a
second PTY, embed server policy in a control, or claim compatibility with Rex's
private transport protocol.

## Native host integration

Register `MultiplexingTerminalTransportProvider` in the host's
`CompositeTerminalTransportFactory`. Start through the existing
`TerminalControl.StartSessionAsync` / `ITerminalSessionService` boundary using
`MultiplexingTransportOptions`. The provider is intentionally opt-in and is not
added to stable default factories.

The options identify an HTTP(S) host, bearer token, session GUID, block GUID,
desired dimensions, observer mode, and optional replay cursor. The token is sent
in `Authorization: Bearer`; it is excluded from the WebSocket URI and options
`ToString()` output. Use HTTPS for remote endpoints, or a loopback SSH tunnel.

### Geometry is part of the stream

`AuthoritativeDimensionsChanged` is a synchronous, ordered callback. A native
integration must apply those dimensions to its VT processor before the callback
returns, so subsequent bytes are parsed at the correct grid size. Do not marshal
only this callback asynchronously while processing output synchronously.

The transport deliberately does not request the local viewport size during
attachment. Historical output must first replay at its historical dimensions.
Once `StartAsync` completes, a controller may explicitly call `Resize` to request
its current viewport. Observers may pan or scroll their view, but must not resize
the shared grid or send input. An integration must prevent feedback from applying
server geometry back into an observer resize request.

This package supplies the network adapter and management client. It does not by
itself add a remote-session selector or geometry adapter to the stable desktop
app shell. Those composition/UI responsibilities remain explicit.

## Lifecycle and recovery

- `StartAsync` connects, authenticates, and waits for the ordered replay fence.
- Server events are applied in exact sequence: initial dimensions, output,
  resizes, and process exit. UTF-8 fragments and VT control bytes remain bytes.
- `StopAsync`, `Dispose`, and `DisposeAsync` detach this client. They do not call
  the block/session deletion API and do not report an invented process exit.
- `ProcessExited` reports only an explicit server process-exit event, once.
- `ConnectionLost` reports unexpected socket loss separately. A disconnected
  block can still be running on the server.
- `LastSequence` advances only after an event has been delivered successfully.
  Reattach from this cursor only if the corresponding VT parser, modes, buffers,
  and geometry are still present. A new terminal must request cursor zero.
- A retention gap or sequence gap is an explicit failure. The client never feeds
  an arbitrary retained byte tail into a fresh VT and presents it as a valid
  screen. There is no blind input retry, which could duplicate shell commands.

## Wire contract

The WebSocket path is
`api/sessions/{sessionId}/blocks/{blockId}/stream?after=N&role=controller|observer`.
The host sends:

1. `{"type":"attached","attachmentId":"...","hasControl":true}`.
2. Ordered event JSON with `sequence`, `kind`, and the payload. Event kinds are
   `resize`, `output`, `exit`, and `closed`; output `data` is base64.
3. `{"type":"ready","sequence":N}` after the captured replay prefix has been
   sent, before live events. The sequence must match the client's applied cursor.
4. Further ordered live events. Errors use `type: "error"`, `code`, and `message`.

Input frames use `{"type":"input","data":"base64"}`. Resize frames use
`type: "resize"`, `columns`, `rows`, `widthPixels`, and `heightPixels`.

The default inbound message budget is 1 MiB, including all WebSocket fragments.
The outbound queue holds at most 128 messages, and individual input calls are
limited to 64 KiB before base64 encoding. Queue overflow throws and accepts no
input; it is never silently dropped. `FlushAsync` waits for prior socket sends,
not for shell execution or an application-level acknowledgement.

## HTTP and CLI

`MultiplexingApiClient` uses a caller-owned `HttpClient`, request-local bearer
headers, source-generated serialization, and bounded JSON response reads. The
generic API call method accepts explicit `JsonElement` arguments. It exposes
structured HTTP status and host error codes without copying response bodies
into exception messages.

The executable in `samples/RoyalTerminal.Multiplexing.Cli` uses
`ROYALTERMINAL_MUX_TOKEN` and optional `ROYALTERMINAL_MUX_URL`. Run `--help` for
management, process creation, input, capture, and introspected API commands.
Command execution preserves executable/argument boundaries; no local shell
string interpolation is performed. This CLI is an automation client, not a
replacement VT renderer.

## Reference behavior

The design follows the relevant documented behavior of the repository's
required references:

- [xterm.js attach addon](https://github.com/xtermjs/xterm.js/blob/master/addons/addon-attach/src/AttachAddon.ts):
  explicit bidirectional input policy and connection listener cleanup.
- [xterm.js serialize addon](https://github.com/xtermjs/xterm.js/blob/master/addons/addon-serialize/src/SerializeAddon.ts):
  terminal state includes buffers and modes; raw retained byte tails are not
  general snapshots.
- [Windows Terminal ConPTY connection](https://github.com/microsoft/terminal/blob/main/src/cascadia/TerminalConnection/ConptyConnection.cpp):
  closing a PTY connection affects process lifetime, so client detach must not
  invoke the server-owned transport's stop operation.
- [Ghostty terminal](https://github.com/ghostty-org/ghostty/blob/main/src/terminal/Terminal.zig):
  dimensions and primary/alternate screen resize behavior belong to VT state.

Focused client tests cover ordered geometry and byte replay, preserved cursors,
duplicate suppression, retention/sequence failures, observer input denial,
bounded fragmented messages, send overflow, detach/exit distinction, cancellation,
HTTP authentication/path constraints, and CLI argument preservation.
