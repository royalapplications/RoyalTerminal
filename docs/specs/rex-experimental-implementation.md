# Experimental durable workspaces inspired by Rex

Status: experimental implementation; review and merge through a draft pull request. Research date:
2026-10-30. Baseline: RoyalTerminal 0.7.0 (`4f5d6689`). Protocol identity: **RoyalTerminal Multiplexer,
version 1**. This document specifies the implementation in this branch, its invariants, validation gates,
and the work required before broader product claims.

## 1. Purpose, evidence, and limits

Build a reusable terminal workspace in which a separate server owns processes, multiple clients can
observe their output, and one attachment controls input and shared terminal dimensions. Reuse
RoyalTerminal's transport contracts, managed VT engine, terminal control, snapshot formatters, and
existing OSC 7501 support.

The companion [Rex product research](rex-product-research.md) distinguishes documented Rex behavior from
product roadmap and unknown internals. Its R01–R27 source register and RX-001–RX-025 requirements are the
evidence baseline. Relevant primary sources include Rex's [server
API](https://www.superlogical.com/rex/docs/automate/server-api), [server
modes](https://www.superlogical.com/rex/docs/linux/server-modes), [remote
access](https://www.superlogical.com/rex/docs/linux/remote-access), [key
modes](https://www.superlogical.com/rex/docs/customize/key-modes), [Lua
API](https://www.superlogical.com/rex/docs/reference/lua), and [OSC 7501
specification](https://www.superlogical.com/rex/docs/build/program-status). These sources motivate
workflows; they do not disclose Rex's private wire format.

**Durable means independent of client lifetime while this server process lives.** A browser reload,
desktop window close, CLI exit, or network detach does not stop the server-owned terminal. Server
shutdown, crash, restart, OS reboot, and host failure do not preserve running processes or replay journals
in this experiment.

The following are explicitly outside the implemented compatibility claim:

- Rex network, configuration, or private plugin compatibility.
- An embedded Lua runtime, Rex Lua scopes, timers, event handlers, or Lua scripts.
- Process recovery, persisted session layouts, or a disk-backed replay journal.
- A complete terminal snapshot protocol that repairs an evicted replay prefix.
- Floating blocks, unplaced blocks, cross-window moves, window reorder/close.
- Root/PAM login brokering, per-account subprocess servers, Unix socket hosting.
- Embedded Tailscale, Tailscale Serve integration, managed cloud, or native iOS.
- Production security isolation, a production deployment service, or benchmarks.

A controller share authorizes commands in a terminal running as the **server's OS account**. Session
scoping is an API boundary; it does not confine shell commands to that session, its directory, its files,
or its process tree.

## 2. Implemented user journeys

1. Start the host separately, obtain its administrator token, and open the browser
   workspace or native sample. Create a session and one or more terminal blocks.
2. Split a window into independently running terminals. Rename sessions, windows,
   and blocks while preserving identity. Drag or keyboard-adjust split ratios.
3. Detach a view, continue producing server output, and attach another view to the
   same block. Replay restores state only when the necessary history is retained.
4. Share one session using an expiring observer or controller capability. Multiple
   observers receive the same bytes; one active controller owns input and resize.
5. Capture server-side terminal text, HTML, or styled VT, including retained
   history. Inspect title, reported directory, and OSC 7501 program-status records.
6. Use the JSON CLI/API to create processes with explicit argument vectors, inspect
   methods, load validated automation, and invoke bounded sequential actions.
7. Explicitly close a block or session to terminate its process ownership. Closing
   a view and closing a process are separate operations in every client.

## 3. Packages and dependency direction

| Location | Responsibility | Dependencies and boundary |
| --- | --- | --- |
| [Multiplexing](../../src/RoyalTerminal.Multiplexing/) | Sessions, windows, layouts, transport lifetime, event journal, attachments, leases | Existing Terminal contracts only; no UI, HTTP, or VT processor dependency |
| [Inspection](../../src/RoyalTerminal.Multiplexing.Inspection/) | One authoritative managed terminal per block; query responses, metadata, capture | Multiplexing and existing managed VT implementation |
| [Automation](../../src/RoyalTerminal.Multiplexing.Automation/) | Target resolution, typed argument checks, actions, layered configuration, key modes | BCL/System.Text.Json; no HTTP, UI, reflection, or Lua dependency |
| [Network transport](../../src/RoyalTerminal.Terminal.Transport.Multiplexing/) | Opt-in ITerminalTransport provider, ordered WebSocket reader, bounded sender, HTTP client | Existing Terminal contracts; caller supplies endpoint and credentials |
| [Server sample](../../samples/RoyalTerminal.Multiplexing.Server/) | Composition, HTTP/WebSocket protocol, grants, method registry, browser assets | Broker, inspection, automation, existing PTY and pipe providers |
| [CLI sample](../../samples/RoyalTerminal.Multiplexing.Cli/) | JSON management, selectors, explicit argv, capture, configuration, actions | Network management client and automation resolver |
| [Desktop sample](../../samples/RoyalTerminal.Multiplexing.Desktop/) | Avalonia remote viewer with session/block selection and explicit geometry | Existing control/services plus network transport; managed VT, no libghostty requirement |

~~~mermaid
flowchart TD
    Clients["Browser, CLI, native sample"] --> Host["HTTP and WebSocket host"]
    Host --> Broker["Broker: sessions and attachments"]
    Host --> Actions["Automation and method registry"]
    Broker --> Inspect["Inspection: canonical VT state"]
    Inspect --> Transport["Existing PTY or pipe transport"]
    Transport --> Process["Server-owned process"]
~~~

The broker can use any registered transport factory. The sample host deliberately exposes only PTY and
pipe process creation. Existing SSH and other transports are not automatically exposed through this host.
The network provider is opt-in and does not change stable application default factories.

Public APIs have XML documentation; HTTP and reusable automation serialization use explicit
source-generated type information. Views stay passive in the native sample: compiled XAML binds a
ReactiveUI ViewModel; services own networking and terminal coordination. The browser is a separate
presentation implementation.

## 4. Domain model and identity contracts

The source of truth is
[MultiplexerContracts.cs](../../src/RoyalTerminal.Multiplexing/MultiplexerContracts.cs).

**D-01 — Identity.** Every session, window, block, attachment, and sharing grant has its own GUID. Labels
are editable display text, not keys. Duplicate labels are permitted; selector ambiguity is an error rather
than a reason to pick one.

**D-02 — Session.** A descriptor contains id, label, revision, creation timestamp, ordered windows, and
ordered blocks. Session creation adds one empty window named Main. Block order is explicit creation order,
not dictionary bucket order.

**D-03 — Window.** A window contains id, label, and a nullable binary layout. Null means empty. A block
belongs to exactly one session and one window. Windows are presentation containers; the block owns the
underlying transport.

**D-04 — Block.** A descriptor includes id, sessionId, windowId, label, state, dimensions, exitCode,
lastSequence, firstRetainedSequence, attachmentCount, and controllerId. Dimensions include columns, rows,
widthPixels, and heightPixels.

**D-05 — Revision.** Session revision begins at one. Structural and label changes increment it; output,
attachment changes, and terminal geometry do not. Optional expectedRevision is checked under the same
broker lock as the mutation. A stale revision returns Conflict without committing that mutation.

**D-06 — Snapshots.** Descriptors are read snapshots, not live objects or a global transaction spanning
future output. LastSequence obtained through a descriptor must never be used as the attachment's replay
fence.

**D-07 — Labels.** The broker rejects empty/whitespace labels and control characters, and enforces the
configured UTF-16 length limit; it preserves accepted text. Titles emitted by a terminal are separate
untrusted metadata and do not rename a block automatically.

The principal service surface is intentionally independent of the HTTP host:

~~~csharp
MultiplexerSessionDescriptor CreateSession(string label);
IReadOnlyList<MultiplexerSessionDescriptor> ListSessions();
MultiplexerSessionDescriptor GetSession(Guid sessionId);
MultiplexerSessionDescriptor RenameSession(Guid sessionId, string label,
    long? expectedRevision = null);
MultiplexerWindowDescriptor CreateWindow(Guid sessionId, string label,
    long? expectedRevision = null);
ValueTask<MultiplexerBlockDescriptor> AddBlockAsync(Guid sessionId,
    string label, ITerminalTransportOptions options, CancellationToken token = default);
MultiplexerAttachment Attach(Guid sessionId, Guid blockId,
    MultiplexerRole role = MultiplexerRole.Observer, long lastSequence = 0);
ValueTask CloseBlockAsync(Guid sessionId, Guid blockId);
ValueTask CloseSessionAsync(Guid sessionId);
~~~

Additional overloads target a window or split an existing block. RenameWindow, RenameBlock, and
SetWindowLayout accept expectedRevision. Broker disposal awaits owned transport cleanup. See
[MultiplexerBroker.cs](../../src/RoyalTerminal.Multiplexing/MultiplexerBroker.cs) for exact signatures
rather than inferring a transport API from a sample route.

## 5. Layout contracts

**L-01.** A leaf has a nonempty blockId and no split children/orientation. A split has an orientation,
finite ratio strictly between zero and one, and two children. Horizontal means side by side; Vertical
means above and below.

**L-02.** A valid window tree contains every block assigned to that window exactly once. Missing blocks,
duplicates, foreign blocks, malformed nodes, invalid ratios, and excessive node counts are rejected before
replacing the tree.

**L-03.** AddBlock appends to an empty window as a leaf; otherwise it creates a horizontal split with the
existing tree. SplitBlock replaces the selected leaf with a new split containing the old block and the
newly created block.

**L-04.** Closing a block removes its leaf and collapses a now-unary split. Empty windows retain their
identity and null layout. Failed process start removes the provisional block and restores a valid layout;
it must not leave a phantom leaf.

**L-05.** Replacing a tree can change ratios and arrange existing same-window leaves. It cannot change
block ownership, detach a block from layout, or implement a cross-window move. Browser focus and zoom are
local client state.

~~~json
{
  "orientation": "Horizontal",
  "ratio": 0.6,
  "first": { "blockId": "11111111-1111-4111-8111-111111111111" },
  "second": { "blockId": "22222222-2222-4222-8222-222222222222" }
}
~~~

The generic API takes this tree in session.set_layout with sessionId, windowId, and optional
expectedRevision. The browser uses a narrower interactive ratio range of 0.1–0.9. JSON object property
names are camelCase; serialized domain enums use their declared names. WebSocket kind and requested role
strings are lowercase.

## 6. Process and attachment lifecycle

**P-01.** A block begins Starting. Successful transport startup makes it Running unless an exit or close
has already been observed. An early exit must not be overwritten with Running merely because StartAsync
subsequently completes.

**P-02.** The broker registers ownership before asynchronous start, coordinates startup cancellation with
close/disposal, and disposes a failed start. A close racing with startup cannot leak a later-created
transport.

**P-03.** ProcessExited publishes one Exit event and records the exit code. Exited blocks remain listed,
retain their journal and inspection state, and count toward the block quota until explicitly closed. Late
observers can replay retained exit history and then observe stream completion.

**P-04.** Attachment.Dispose and a disconnected WebSocket release only that attachment and its control
lease. They never call StopAsync on the process. Network loss is not evidence that the process exited.

**P-05.** CloseBlock removes the block and layout leaf, stops/disposes ownership once, and completes
attachments. CloseSession does the same for all owned blocks. Broker disposal also waits for
already-retiring blocks, not just currently listed ones. Teardown failures must not prevent unrelated
owned blocks from retiring.

**P-06.** Successful HTTP close means the requested ownership operation completed; it does not promise
application-specific graceful shutdown, descendant process containment, or filesystem rollback. Those
depend on the existing provider/OS.

~~~mermaid
stateDiagram-v2
    [*] --> Starting: create
    Starting --> Running: start completes
    Starting --> Exited: early process exit
    Running --> Exited: process exit
    Starting --> Closed: cancel or close
    Running --> Closed: explicit close
    Exited --> Closed: explicit close
    Closed --> [*]
~~~

## 7. Canonical terminal state and operation ordering

The [inspection wrapper](../../src/RoyalTerminal.Multiplexing.Inspection/InspectingTerminalTransport.cs)
decorates the transport. Its BasicVtProcessor and TerminalScreen continue processing output when no viewer
exists. Capture and status do not depend on an arbitrary GUI being connected, visible, or using a
particular renderer.

**O-01 — One response owner.** Only the server's canonical VT processor answers device attributes,
cursor-position, status, and capability queries. Native and browser presentation parsers suppress
automatic replies during replay and live output while forwarding genuine controller input.

**O-02 — Parser order.** Output parsing, canonical geometry changes, metadata reads, capture, and
publication are serialized by the inspection lock. Output is published only after the corresponding
canonical parsing operation.

**O-03 — Geometry order.** Inspection implements IMultiplexerResizeSource. DimensionsChanged is emitted
after canonical dimensions commit, under the same lock as output publication. The broker journals geometry
from this notification. Already-parsed old-geometry output therefore precedes the new geometry event.

**O-04 — Rejected resize.** Inspection applies a requested grid, publishes it, releases its parser lock,
and calls the underlying resize. If that call fails, the restored grid is published too. Any synchronous
redraw between those events keeps its real place in the sequence. A client must apply every resize event,
including rollback, rather than coalescing across intervening output.

**O-05 — No native call under the parser/event lock.** Transport operations can cause synchronous or
cross-thread output callbacks. The broker separates its operation semaphore from its event gate;
inspection drains query replies and invokes native resize outside its parser lock. Lock ordering is parser
then broker event publication, never a blocking native call inside that chain.

**O-06 — Plain transport fallback.** Without IMultiplexerResizeSource, the broker announces requested
geometry before native resize and publishes rollback on failure. The sample's strict canonical ordering
relies on its inspection decorator; the generic broker does not invent a parser for arbitrary transports.

**O-07 — Reply bounds.** Startup replies queue until transport start. Reply queue overflow or write
failure is recorded in inspection.ResponseError and stops that reply path; terminal output remains
inspectable. Providers must serialize their actual input writes, including user input and query replies.

Factory inspection lookup uses options object reference identity. The host retains one unique options
instance per block, avoiding aliases between equal command configurations. Disposal unregisters that
instance.

## 8. Event journal, replay, and readiness

**R-01.** Each block has an independent monotonic sequence starting at one. The initial event is Resize.
Output, subsequent Resize, Exit, and Closed share that sequence. No global ordering across blocks or
session metadata is promised.

**R-02.** Output remains bytes. Transport buffers are copied before retention, large chunks split into
bounded events, and recipients receive owned payloads. UTF-8 fragments, escape sequences, and binary
terminal content are not converted to strings or normalized by the broker.

**R-03.** Replay is bounded by both payload bytes and event count. Old events are evicted from the front.
Inspection scrollback and journal retention are separate budgets; an existing capture does not make an
evicted journal prefix recoverable.

**R-04.** Under one block gate, Attach validates the cursor, selects events after it, subscribes to live
events, and captures ReplayThroughSequence. A concurrent output event is either in that prefix or after
it; it cannot be lost between history enumeration and subscription.

**R-05.** after=0 means a fresh terminal parser and requires the complete initial stream. A positive
cursor means the caller already retains exactly the state through that sequence, including buffers, modes,
decoder state, and geometry. Supplying a descriptor's latest sequence to a fresh parser is invalid.

**R-06.** A cursor ahead of the stream is Conflict. A cursor requiring evicted events is HistoryGap.
Clients display this failure; they do not reset to an arbitrary retained tail and call it a reconstructed
screen. No snapshot recovery handshake is implemented, including when server capture is still available.

**R-07.** After attached, the host sends all events through the captured fence, then
ready(sequence=fence), then later live events. If cursor already equals the fence, ready is immediate.
Live arrivals cannot move the fence. The host delays input processing until the replay prefix has been
sent.

**R-08.** Presentation clients advance their applied cursor only after successful application. Browser
writes await xterm's write callback; native geometry waits for the ordered parser/UI operation. Ready must
equal that applied cursor. Duplicate already-applied events may be ignored; gaps must fail explicitly.

**R-09.** An attachment has bounded pending event and output-byte queues. Overflow disconnects that reader
with CapacityExceeded and releases its control lease. The producer and other readers continue. Existing
readers can outlive journal eviction because they consume the live stream.

Server-sent readiness is a stream boundary, not an acknowledgement that a shell executed earlier input. A
disconnect can make input execution uncertain. Neither the transport nor action runner blindly resends
input or mutating requests.

## 9. Observation, control, and shared geometry

Observer attachments receive events and can inspect/capture authorized sessions. They cannot send input or
resize. Exactly one Controller attachment can hold the block's control lease; a second controller request
fails Conflict.

Detaching or forcibly disconnecting a controller releases the lease. Observers are never automatically
promoted. A newly authorized controller must explicitly attach. Administrator takeover is
client.disconnect followed by a new attach, not simultaneous writers or an invisible priority override.

All viewers parse one server-owned grid. A small observer viewport scrolls or clips that grid. It must not
resize the PTY to its own local window. Controller resize requests enter the ordered canonical path before
later output is parsed.

HTTP block.input obtains a temporary controller attachment, writes once, and detaches. It conflicts with
an existing controller, even if the same bearer token owns that other attachment. Input permission is
necessary but does not bypass exclusive ownership. No process-execution acknowledgement accompanies that
write.

## 10. HTTP routes and registry

The host uses UTF-8 JSON, source-generated DTOs, strict unknown-member handling, and request-local bearer
authorization. API responses use no-store. See
[Program.cs](../../samples/RoyalTerminal.Multiplexing.Server/Program.cs),
[ServerProtocol.cs](../../samples/RoyalTerminal.Multiplexing.Server/ServerProtocol.cs), and
[ApiCatalog.cs](../../samples/RoyalTerminal.Multiplexing.Server/ApiCatalog.cs).

| Route | Authorization | Contract |
| --- | --- | --- |
| GET /healthz | Public | Liveness only; no process or identity data |
| GET /api/context | Any valid token | Administrator, scoped sessionId, canControl |
| GET /api/methods | Any valid token | Method descriptions and input/output schemas |
| POST /api/call | Per method | {method, arguments}; registry validation and dispatch |
| GET /api/sessions | Any valid token | Only readable sessions |
| POST /api/sessions | Administrator | {label}; returns new session, HTTP 201 |
| GET /api/sessions/{s} | Session read | Descriptor, revision, windows, blocks |
| DELETE /api/sessions/{s} | Administrator | Explicitly close all owned blocks |
| POST /api/sessions/{s}/blocks | Administrator | NewBlock; returns started block, HTTP 201 |
| DELETE /api/sessions/{s}/blocks/{b} | Administrator | Explicit block close |
| POST /api/sessions/{s}/blocks/{b}/input | Session control | {data: base64}; temporary exclusive lease |
| GET /api/sessions/{s}/blocks/{b}/capture?format=text | Session read | text, html, or vt capture with metadata |
| GET /api/sessions/{s}/blocks/{b}/stream | Session role | WebSocket protocol below |
| GET /api/config | Administrator | Effective configuration and override diagnostics |
| PUT /api/config | Administrator | Validate and atomically replace JSON automation |
| POST /api/actions/{id}/invoke | Administrator | Optional sessionId/blockId/windowId context |

The registry exposes these groups; names are case-sensitive:

| Group | Methods |
| --- | --- |
| Server | server.version |
| Sessions | session.list, session.create, session.view, session.set_label, session.close |
| Windows/layout | session.new_window, session.set_window_label, session.set_layout |
| Blocks | session.set_block_label, block.create, block.close, block.input |
| Inspection | terminal.format, terminal.status, terminal.title, terminal.pwd |
| Clients | client.list, client.disconnect |
| Sharing | share.create, share.list, share.revoke |

Mutation requires administrator except block.input. Reading methods still enforce session scope. Unknown
methods and arguments fail structurally. Domain validation remains authoritative: a schema does not prove
that an ID exists, that a lease is available, or that a layout contains exactly its window's current
blocks. The current catalog is runtime-discoverable; it is not Rex's installed registry.

Discovery output schemas come from the actual source-generated response DTOs. Nested block.create.block
and session.set_layout.layout inputs likewise use generated request contracts with correctly rebased local
references, plus process/dimension bounds and layout descriptions. Other method fields use explicit
primitive schemas; expectedRevision accepts a nonnegative integer or null. RequestExample is currently
optional and unpopulated; the examples in this document are not a claim of complete catalog examples.

Example independent API call with explicit argv and host-side working directory:

~~~json
{
  "method": "block.create",
  "arguments": {
    "sessionId": "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa",
    "block": {
      "label": "Build", "transport": "pty",
      "command": { "fileName": "/bin/bash", "arguments": ["-lc", "printf 'ready\\n'; exec bash"] },
      "workingDirectory": "/home/example/work",
      "dimensions": { "columns": 100, "rows": 28, "widthPixels": 0, "heightPixels": 0 }
    }
  }
}
~~~

Here the caller explicitly selected a shell and its -lc argument. The host does not construct that shell
string from labels, paths, or other arguments. Prefer direct executable arguments when shell evaluation is
unnecessary.

## 11. WebSocket protocol version 1

Connect to /api/sessions/{s}/blocks/{b}/stream?after=N&role=observer|controller. Native clients use
Authorization: Bearer. Browsers authenticate using the first text frame within five seconds; tokens never
belong in the stream query string. All messages are one UTF-8 JSON text message, possibly
WebSocket-fragmented. Output data is standard base64; no binary WebSocket message protocol is defined.

An illustrative fresh connection, with application data abbreviated:

~~~json
{"type":"authenticate","token":"<bearer capability>"}
{"type":"attached","attachmentId":"33333333-3333-4333-8333-333333333333","hasControl":true}
{"sequence":1,"kind":"resize","dimensions":{"columns":100,"rows":28,"widthPixels":0,"heightPixels":0}}
{"sequence":2,"kind":"output","data":"cmVhZHkNCg=="}
{"type":"ready","sequence":2}
{"sequence":3,"kind":"output","data":"JCA="}
~~~

The authenticate frame is client-to-server; subsequent lines above are server-to-client. Null properties
are omitted. Some control/event messages also contain default sequence=0 or hasControl=false fields;
clients use type/kind discriminators and must not mistake a control message for event sequence zero.

Controller frames after readiness:

~~~json
{"type":"input","data":"bHMNCg=="}
{"type":"resize","columns":120,"rows":32,"widthPixels":0,"heightPixels":0}
{"type":"detach"}
~~~

Terminal completion is an event such as {"sequence":4,"kind":"exit","exitCode":0}. Explicit close uses
kind=closed. Errors use {"type":"error","code":"HistoryGap","message":"..."} before detachment when the
connection can still carry one. An HTTP error can also precede upgrade. There is no per-input message ID,
command acknowledgement, exactly-once execution protocol, compression contract, or terminal-state snapshot
frame.

The native client validates sequence and readiness, bounds fragmented message assembly, and separates
ProcessExited from ConnectionLost. The browser preserves its parser/cursor when retrying a dropped socket
and uses bounded exponential reconnect delay. Neither client recovers state after losing its local parser.

## 12. Authentication, authorization, and host boundaries

The default bind address is http://127.0.0.1:7331. A non-loopback URL requires
ROYALTERMINAL_MUX_ALLOW_REMOTE=1. This opt-in does not install TLS certificates, configure a proxy,
authenticate an OS user, or isolate launched processes.

ROYALTERMINAL_MUX_TOKEN supplies an administrator bearer token of 32–512 characters. If absent, the host
generates a 32-byte cryptographic random token (encoded as 64 hex characters) and prints it once for local
startup. Configured tokens are not printed. Runtime comparison uses SHA-256 digests and fixed-time
comparison; the grant store does not retain sharing tokens in plaintext.

An administrator can create an observer or controller share scoped to one session. The token is returned
only at creation. share.list returns metadata, including grant ID, role, and expiry. Lifetimes are 1–1440
minutes, default 60. Revocation removes the grant and disconnects its active clients; expiry limits active
connection lifetime and is checked again for input/API calls.

**A controller share can execute commands as the server OS account.** It can therefore access anything
that account can reach through the shell. The role restricts API methods and control acquisition; it is
not an OS sandbox, tenancy boundary, directory jail, or protection for other processes owned by that
account. Observers can read potentially sensitive terminal output and captures.

The browser retains credentials in memory. A generated share URL carries its token in the fragment; the
browser removes that fragment after reading it. Fragments are not sent in HTTP request targets, but the
link remains a credential while visible, copied, or retained by the recipient. No cookies or localStorage
are used for credentials. Native sample credentials likewise remain in memory.

Same-origin checking rejects a present Origin with a different scheme/authority; non-browser clients
without Origin remain supported. CSP limits scripts and connections to self, disables framing/objects, and
serves pinned local terminal assets. This is not a substitute for bearer authorization or deployment TLS.
Use HTTPS or a trusted loopback tunnel for remote access. No CORS, identity provider, password login,
rate-limit service, or reverse-proxy trust policy is included. Runtime sessions and grants disappear on
host restart.

## 13. Resource budgets

These are implementation defaults, not measured capacity or performance claims. Full definitions are in
[MultiplexerOptions](../../src/RoyalTerminal.Multiplexing/MultiplexerOptions.cs),
[TerminalInspectionOptions](../../src/RoyalTerminal.Multiplexing.Inspection/TerminalInspectionOptions.cs),
and package READMEs.

| Resource | Default or host limit | Behavior at limit |
| --- | --- | --- |
| Sessions / blocks per session / windows per session | 32 / 32 / 16 | Reject creation; exited blocks still count |
| Attachments per block | 8 | Reject attach |
| Journal payload / events per block | 8 MiB / 4096 | Evict oldest events; later replay may gap |
| Output bytes per event | 16 KiB | Split transport chunks without decoding |
| Pending attachment output / events | 16 MiB / 8192 | Disconnect slow attachment |
| Input bytes per operation | 64 KiB | Reject oversized input |
| Label length | 256 UTF-16 code units | Reject label |
| Broker dimensions | Generic 4096 per axis; host 512 | Reject invalid geometry |
| Host process creation dimensions | Columns 2–512; rows 1–256; pixels 0–32768 | Validate before process launch |
| Inspection viewport | At most 1,048,576 cells | Reject before allocating screen |
| Inspection scrollback | 10,000 rows; 16 MiB logical page budget | Retain bounded history; active viewport not evicted |
| Pending server query replies | 64 KiB | Record ResponseError; stop reply path |
| HTTP request body / headers | 256 KiB generally; config PUT 1 MiB; 64 headers | Bound known-length and chunked reads; header timeout 10 seconds |
| Client-to-host WebSocket JSON | 128 KiB per assembled message | Reject oversized/binary/invalid frames |
| Native received WebSocket JSON | 1 MiB per assembled message | Fail connection |
| Native outgoing queue | 128 messages | Reject overflow without silently dropping input |
| Sharing grants | 256 live grants | Reject additional grants; expired entries purged |
| Capture string | 4 × 1024 × 1024 UTF-16 code units | Reject after formatting with capture_limit |
| Browser live parser cache | 32 panes; inactive least-recently-visible eviction | Dispose evicted view; process continues |

Budgets are per block/attachment unless stated otherwise. They do not bound process memory, child count,
CPU, disk usage, or aggregate workload to a safe production total. The capture bound is a string-length
check, not an exact UTF-8 response-byte limit; inspection history has a logical page budget, not a
measured CLR heap cap. The browser scrollback is currently 15,000 rows per cached pane.

## 14. Command creation, capture, and OSC 7501

PTY creation uses the existing platform provider and its default shell when no command is supplied. Pipe
creation requires an explicit command and merges stderr into output. A supplied executable must be
nonempty and contain no NUL; at most 256 argument strings are accepted, each at most 32,768 characters
without NUL. A supplied working directory must be an existing absolute path on the host.

The host injects ROYALTERMINAL_MUX_SESSION, TERM=xterm-256color, and COLORTERM=truecolor. It does not
currently inject a complete Rex context or claim automatic ROYALTERMINAL_MUX_BLOCK /
ROYALTERMINAL_MUX_WINDOW values. The child environment receives an empty ROYALTERMINAL_MUX_TOKEN override
so ordinary inheritance does not disclose the administrator token; this is defense in depth, not isolation
from a process running under the same OS account.

Capture exports the canonical active screen and retained history through existing PlainText, Html, and
StyledVt formatters. It is neither the raw journal nor a complete resumable parser snapshot.
Alternate-screen export follows the managed engine's existing semantics. HTML is an export artifact, not
trusted UI markup.

Title and working-directory reads return reported terminal metadata as plain text. The inspection layer
never resolves the reported directory, opens its path, or uses it as authorization. It installs no
clipboard, notification, file/shared memory image reader, or host window-resize capability. Graphics
decoding is disabled in this server inspection plane.

OSC 7501 parsing/store behavior is reused from the existing terminal engine:

- States are idle, working, done, blocked, error; clear removes a record subtree.
- Records have hierarchical IDs, optional progress and blocked kind, and
  application/title/message text. Nearest declared application ancestry is dynamic.
- A terminal stores at most 256 records; oldest updates are evicted at capacity.
- The managed parser bounds a status body to 4,087 bytes and handles the
  capability query through the single server response owner.
- Prompt/process-exit transitions clear working/blocked records; done/error
  survive until protocol clear/reset or block disposal.
- terminal.status returns an owned record snapshot. Browser inspection displays
  JSON; full attention aggregation, status badges, and notifications are deferred.

## 15. Declarative automation and configuration

The experiment uses **versioned JSON actions**, not Lua syntax or compatibility. The reusable [automation
package](../../src/RoyalTerminal.Multiplexing.Automation/README.md) defines IMultiplexerMethodInvoker,
MultiplexerActionRunner, configuration loading, target resolution, and a framework-independent key engine.

An action is an ID plus ordered steps. Each step names a registered method and has a JsonElement argument
object, with optional step timeout. The action may also define its overall timeout. The runner snapshots
and validates the full action against its registered schema vocabulary before invoking the first step.

The host expands only whole string values equal to ${sessionId}, ${blockId}, or ${windowId}, recursively
through argument objects/arrays. Missing required context fails before invocation. This is a narrow host
adapter, not interpolation, expression evaluation, shell substitution, variables, or references to prior
step results. Every invoked method rechecks authorization and domain state.

~~~json
{
  "version": 1,
  "actions": [{
    "id": "capture_focused",
    "steps": [{
      "method": "terminal.format",
      "arguments": {
        "sessionId": "${sessionId}", "blockId": "${blockId}", "format": "text"
      }
    }]
  }],
  "modes": [{ "name": "workspace", "exclusive": true }],
  "bindings": [
    { "sequence": "ctrl+g", "enterMode": "workspace", "once": true },
    { "mode": "workspace", "sequence": "s", "actionId": "capture_focused" }
  ]
}
~~~

The runner executes steps sequentially and returns indexed method/results. Failure reports the action,
zero-based failing stepIndex, method, and completedSteps through both the library exception and the HTTP
error envelope's optional action object. Earlier side effects remain if a later step fails: actions are
not transactions.

Cancellation and action/step deadlines bound asynchronous waiting. A method implementation must return its
ValueTask promptly; arbitrary synchronous work cannot be preempted safely. A timeout does not prove that a
remote side effect was undone. No retries or automatic compensation occur.

Default limits: 32 steps; action timeout 30 seconds (maximum 120); step timeout 10 seconds (maximum 60);
64 KiB argument/schema/result JSON; 256 actions; 2048 bindings; 128 modes; 8 keys per chord; 16 mode
frames; 1 MiB configuration. General host requests are limited to 256 KiB; configuration PUT explicitly
permits 1 MiB, including chunked reads.

The schema validator intentionally supports a bounded subset: boolean schemas; type/unions; properties,
required, additionalProperties; item/property/string bounds; numeric bounds; enum and const; selected
documentation annotations. It rejects unsupported constraints rather than silently treating them as valid.
Numeric comparisons preserve decimal/exponent precision; string lengths count Unicode scalar values.
Domain checks such as live IDs, permission, revisions, and full recursive layout membership still occur in
the host/broker.

The host maintains a separate DescribeActionMethods projection for the bounded validator. It removes
format annotations so GUID context placeholders can be checked before expansion, inlines nonrecursive
local schema references, and replaces recursive cycles with a documented object/null boundary. Full
descendant deserialization and layout invariants are deferred to method invocation. A deeply malformed
layout can therefore pass preflight and fail after earlier action steps committed; tests explicitly
preserve this distinction.

Configuration parsing rejects unknown members and validates references. HTTP action IDs also reject a lone
dot and any double-dot sequence, matching CLI path restrictions. The reusable merge order is defaults,
server, then client; later definitions replace earlier action/mode IDs. Later conflicting exact or prefix
bindings remove older bindings with binding_shadowed diagnostics; nonconflicting sibling chords remain.

The sample host uses an empty default layer, its server configuration, and an empty client layer. It does
not expose a persistent per-client override editor. PUT /api/config validates before replacing anything.
If ROYALTERMINAL_MUX_CONFIG is set, successful updates write a same-directory temporary file and
atomically replace the configured file before publishing the in-memory state; the temporary file is
created with Unix owner-only read/write permissions. A failed validation/write preserves the previous
configuration. Startup loads that file if present. External edits are not watched automatically.

## 16. Key modes and target selection

The key engine starts with a nonexclusive default frame. Higher stacked modes take precedence. An
exclusive mode consumes unbound keys; a nonexclusive mode falls through to lower frames. A binding
performs exactly one action, mode entry, mode exit, all-mode exit, or explicit block. Blocking consumes
without dispatch.

A once mode is removed when a shortcut belonging to that frame resolves, including a blocked shortcut.
Unrelated unbound keys do not consume a once mode. Escape cancels a pending chord first; otherwise it pops
the top mode unless that mode explicitly binds Escape. Chords expire after 1500 ms. After a mismatch in a
nonexclusive mode, only the last key is retried; consumed prefix keys are not re-injected into the shell.
Exclusive mismatches stay consumed.

Normalized modifier order is ctrl, alt, shift, meta. Documented aliases include control, option,
cmd/command/super/win. The plus key is named plus, and key sequences use > between strokes. Configuration
and engine state are owned snapshots, so caller mutation cannot change a running action or key resolution.

Target resolution uses authorized ordered candidates and these exact stages:

| Priority | Match | Failure behavior |
| --- | --- | --- |
| 1 | Exact ordinal full ID | A matching ID wins over a label |
| 2 | Case-insensitive whole label | Multiple matches fail immediately |
| 3 | Positive one-based index | Numeric labels already matched at stage 2 |
| 4 | Case-insensitive ID suffix of at least four characters | Multiple matches fail |
| Final | No match | NotFound; no mutation |

An out-of-range numeric selector may still match a suffix. The resolver is pure and bounds candidate count
(default 4096). The CLI additionally accepts a complete nonempty GUID directly, allowing scoped
credentials to address known resources without requiring a listing. Blocks/windows resolve within the
selected session. Use stable complete GUIDs in automation sensitive to concurrent rename/reorder.

## 17. Browser workspace behavior

The browser uses locally copied, lockfile-pinned xterm.js 6.0.0 and fit addon 0.11.0. The host's managed
VT remains canonical; xterm is a presentation parser. The workspace has sessions, window tabs, nested
splits, local zoom/focus, rename, explicit close/detach, capture downloads, sharing/revocation, and a
command palette. Metadata refresh is polling at 2.5 seconds; it is not a session-mutation event feed.

Each pane owns its parser, cursor, socket, and serialized event-application chain. Navigation among
sessions/windows and zoom preserve those objects. Hidden panes continue parsing, retain their leases, and
do not fit geometry while disconnected from the visible DOM. Up to 32 panes are cached; inactive
least-recently-visible panes are evicted above the bound. A new browser or evicted pane starts at cursor
zero and may encounter HistoryGap. Cache retention is not durable client storage.

Only controlling, ready, visible panes fit to viewport; observers retain the shared grid. Controller
conflict falls back to observation. Administrator takeover uses the explicit disconnect API. Reconnect
keeps the same parser and applied cursor; terminal exit stops reconnection and retains the displayed
result.

The input-origin adapter depends on xterm 6.0.0's internal coreService.onUserInput signal immediately
preceding user onData. Parser-generated query replies omit that marker. The adapter forwards marked
keyboard/paste/IME data and the mouse-reporting binary path, and suppresses automatic query replies. This
private API dependency is isolated in terminal-input.js, pinned, and requires real xterm tests when
upgraded. Focus reports come from actual textarea focus/blur when the terminal enables focus reporting.

Configured key modes run before the built-in Ctrl+B prefix shortcuts. Ctrl/Cmd+K opens the palette.
Dialog/palette editing does not send keys into the terminal. Composition/dead-key events do not become
custom key chords. The browser keymap has its own tests against documented C# engine behavior; it is not
shared C# code. Responsive/touch controls exist, but native mobile and full accessibility audits are not
claimed by the presence of a responsive page.

## 18. Native adapter and desktop sample

Register MultiplexingTerminalTransportProvider in an explicit CompositeTerminalTransportFactory and start
using MultiplexingTransportOptions. Options contain HTTP(S) endpoint, bearer token, session/block IDs,
dimensions, observer mode, and optional cursor. Tokens are excluded from URI and ToString.

StartAsync connects and waits for replay readiness. Stop/Dispose detach only. ProcessExited reports an
explicit server event once; ConnectionLost is distinct. LastSequence advances after delivery. FlushAsync
means prior socket sends completed, not that a remote application consumed the bytes or executed a
command.

AuthoritativeDimensionsChanged is synchronous and ordered. An integration must apply geometry before
returning and before parsing later bytes. The native sample flushes queued preceding output, dispatches
the atomic grid update on the UI thread, applies pixel dimensions, and only then releases the reader.
Applying Columns and Rows separately would permit an invalid intermediate reflow.

RemoteTerminalSessionService suppresses local VT query replies and automatic local layout resize requests
while preserving explicit controller keystrokes and paste. Local window resize clips/pans/scrolls the
remote grid. Resize remote is a separate controller command, so an observer's monitor cannot resize
another TUI.

The sample offers host/token entry, session/block selection or manual GUIDs, session/shell creation for
administrators, Observe only, attach/detach, and explicit columns/rows. Every new attachment clears the
local terminal and starts from zero. Automatic reconnect with preserved native parser state and a full
native split-workspace shell are deferred. It rejects grids over 4096 per axis or 1,048,576 cells and
adjusts bounded history rows for very wide grids.

## 19. CLI behavior and operations

The CLI reads ROYALTERMINAL_MUX_TOKEN and optional ROYALTERMINAL_MUX_URL; --endpoint can override the
endpoint. Tokens are not accepted as command arguments or URL components. Commands emit JSON and preserve
executable/argument boundaries.

~~~sh
dotnet run --project samples/RoyalTerminal.Multiplexing.Cli -- sessions
dotnet run --project samples/RoyalTerminal.Multiplexing.Cli -- new Work
dotnet run --project samples/RoyalTerminal.Multiplexing.Cli -- run Work --label Build -- /bin/bash
dotnet run --project samples/RoyalTerminal.Multiplexing.Cli -- capture Work Build
dotnet run --project samples/RoyalTerminal.Multiplexing.Cli -- api describe terminal.format
dotnet run --project samples/RoyalTerminal.Multiplexing.Cli -- config load samples/RoyalTerminal.Multiplexing.Server/automation.example.json
dotnet run --project samples/RoyalTerminal.Multiplexing.Cli -- do capture_focused Work Build
~~~

run defaults to PTY; --pipe requests redirected streams, --cwd is a host path. send passes supplied UTF-8
text once; include a newline when the remote shell should execute it. stop SESSION BLOCK and close SESSION
explicitly terminate. api call METHOD JSON accepts - for stdin and does not resolve labels inside JSON.

config load validates on the host without invoking actions. do ACTION accepts optional
session/block/window selectors; omitted context may come from the corresponding ROYALTERMINAL_MUX_*
environment variables. An explicit session replaces the entire environment context to avoid reusing a
foreign block/window.

CLI request timeout is 130 seconds to accommodate a maximum 120-second action; Ctrl+C cancels. Exit codes
are 0 success, 1 network/file/host failure, 2 invalid arguments or target resolution, and 130
cancellation. No interactive CLI renderer, wait-for-process command, or installer for agent settings is
included.

## 20. Failure semantics and recovery decisions

| Condition | API/event outcome | Caller action |
| --- | --- | --- |
| Unknown identity | NotFound / HTTP 404 | Refresh authorized descriptors |
| Stale revision, existing controller, cursor ahead | Conflict / HTTP 409 | Inspect current state; do not blindly repeat |
| Evicted required history | HistoryGap / HTTP 409 or stream error | Preserve existing view if available; capture separately; no automatic snapshot repair |
| Observer input/resize | Forbidden / HTTP 403 | Obtain an authorized explicit control attachment |
| Broker quota/slow reader | CapacityExceeded / HTTP 429 or stream error | Reduce workload; detach unused readers; preserve process |
| Closed/disconnected attachment | Disconnected / HTTP 410 | Reattach only with valid retained state/cursor |
| Malformed JSON/arguments | bad_request / HTTP 400 | Correct request; no schema coercion |
| Unknown method | method_not_found / HTTP 404 | Use discovery for this server version |
| Expired/revoked capability | Authorization error; live detach | Obtain a new grant from an administrator |
| Action timeout/cancellation | HTTP 408 where a response is possible | Inspect prior effects before deciding to retry |
| Internal host failure | internal_error / HTTP 500 | Inspect host log; no fabricated success |

JSON errors use code and message, with an optional action failure object containing completed steps.
Broker codes retain their enum names; host codes such as bad_request are lowercase. Client implementations
must preserve the server code rather than flatten all failures into a disconnection.

A missing process-exit event cannot be inferred from a socket close. A canceled request may have already
committed a mutation. Session revision is an optimistic edit guard, not a distributed transaction or a
command deduplication key.

## 21. Mapping to the 25 research requirements

Implemented means the stated experimental subset exists; Partial identifies material remaining acceptance
work. The evidence column points to the focused suite or implementation boundary, not a claim that all
platforms have passed CI.

| ID | Status | Implementation and remaining boundary |
| --- | --- | --- |
| RX-001 | Implemented | Broker owns transports; real process/server tests detach and reattach while process continues. Server lifetime only. |
| RX-002 | Implemented | GUIDs survive labels/layout changes; ordered candidates and ambiguity-before-mutation tests. |
| RX-003 | Partial | Sessions/windows/blocks, creation and labels implemented; no separate window close/reorder API. |
| RX-004 | Implemented | Nested binary split validation, ratios, exact membership, rollback and leaf collapse tests. |
| RX-005 | Implemented | Detach versus close, startup/close races, once-only cleanup and retained exit state. |
| RX-006 | Partial | Browser focus/zoom/ratio editing and same-window tree replacement; no floats or cross-window moves. |
| RX-007 | Implemented | Existing transport/VT/control contracts reused; host currently exposes PTY/pipe only. |
| RX-008 | Implemented | Bounded independent attachments, observer shares, multi-reader byte ownership and detach tests. |
| RX-009 | Implemented | One controller, read-only enforcement, explicit takeover, canonical resize ordering. |
| RX-010 | Partial | Ordered replay/cursors and explicit gaps; browser state-preserving reconnect; no snapshot repair or native automatic reconnect. |
| RX-011 | Partial | Canonical text/HTML/VT export and retained history; raw recording download and resumable snapshots deferred. |
| RX-012 | Partial | Retained exit codes and explicit Exit event; CLI wait/exit-code propagation command deferred. |
| RX-013 | Partial | Registry, generated nested input/output schemas and bounded action projection; RequestExample values and complete release-versioned semantic contracts remain deferred. |
| RX-014 | Partial | Ordered bounded per-block output/geometry/exit streams; no general session/layout/status event subscription API. |
| RX-015 | Implemented | JSON CLI, scoped targeting, argv, errors, cancellation, actions; not an interactive terminal renderer. |
| RX-016 | Partial | Loopback default, bearer capabilities, origin policy, protocol version; production TLS/proxy/service deployment deferred. |
| RX-017 | Partial | Validated atomic server config replacement and file persistence; no watcher or per-client persistent editor. |
| RX-018 | Implemented subset | Reusable layers/chords/modes/once/exclusive/validation plus browser integration; no Lua callbacks. |
| RX-019 | Deferred | No embedded Lua runtime or Rex configuration compatibility. |
| RX-020 | Partial | Existing bounded OSC 7501 parser/store/query and inspection exposed; full workspace status aggregation/notifications deferred. |
| RX-021 | Partial | Discovery, JSON actions and session context; no agent setup/removal installer or owned-settings migration. |
| RX-022 | Deferred | No root/PAM broker, account mapping, privilege separation, or broker/account-server upgrade continuity. |
| RX-023 | Partial | Bounded in-memory state and atomic config file only; no persisted sessions/journal/snapshots/process recovery. |
| RX-024 | Partial | Browser workspace and runnable native viewer; full native workspace, accessibility audit and mobile clients deferred. |
| RX-025 | Partial | Dedicated OS/build/test workflow and runnable samples; packaging/service install/signing/release support deferred. |

## 22. Validation and release gates

Use [the validation guide](rex-experimental-validation.md) for exact local commands. Counts are
intentionally not embedded here because integration suites can grow during review. Passing library tests
alone is insufficient evidence for a working browser input path, platform PTY lifecycle, or native grid
ordering.

| Suite | Required behavior |
| --- | --- |
| Multiplexing.Tests | Replay fence, attach races, output copies, quotas, revision/layout invariants, ownership, startup/close/disposal concurrency |
| Multiplexing.InspectionTests | Canonical capture/status, one query owner, bounded response path, no host effects, concurrent output/resize/rollback |
| Multiplexing.AutomationTests | Preflight prevents early mutation on invalid known contracts, exact numeric/schema checks, bounded cancellation, modes/layers, selector ambiguity |
| Multiplexing.ClientTests | Fragmented frames, byte ordering, ready fence, sequence/gap failures, observer denial, send queue bounds, HTTP/CLI contracts |
| Multiplexing.DesktopTests | Compiled view, service/ViewModel behavior, actual cell text across geometry changes, query suppression with user input retained |
| Multiplexing.ServerTests | Real HTTP/WebSocket host, scopes/origins, strict JSON, method dispatch, grants, real process IO and detach persistence |
| Browser unit tests | Sequences, layout helpers, key normalization/modes, untrusted text/file naming |
| Playwright with real xterm and PTY | Browser creation/splits/sharing/replay, actual terminal input, query suppression, IME/paste and navigation preservation |

**Gate V-01:** Build the changed projects in Release with repository warnings as errors and
reflection-disabled JSON paths. Do not disable existing gates to make an experiment appear successful.

**Gate V-02:** Run focused managed suites on Linux, macOS, and Windows in the experiment workflow. A
workflow definition is not evidence that those jobs passed; the draft PR must report observed local
results and later remote CI separately.

**Gate V-03:** Run browser unit tests and actual Chromium flows after asset build. Test keyboard, deferred
IME composition, paste, mouse/focus reporting, server query reply suppression, and retained parsers across
window switches using pinned xterm.

**Gate V-04:** Exercise real PTY/pipe processes with time bounds. Confirm output continues with no
clients, late replay preserves geometry, explicit close releases ownership, and early process completion
does not become a permanently running block.

**Gate V-05:** Review token handling, scoped reads, controller execution disclosure, same-origin policy,
unknown arguments, request/message bounds, and slow-reader behavior. Do not equate session capabilities
with OS isolation.

**Gate V-06:** Record unsupported environments and failed/unrun checks in the PR. No measured throughput,
memory, latency, native iOS support, or service durability claim can be derived from these functional
tests.

## 23. Follow-on implementation decisions

Before snapshot recovery, define a versioned state-transfer contract including normal/alternate buffers,
modes, cursor, partial decoder/parser state, geometry, history, and a journal fence. A styled VT capture
alone is insufficient. Add cross-renderer compatibility tests or constrain the supported parser version.

Before process restart recovery, choose a separate supervisor/PTY ownership model and explicit OS process
semantics. Saving layouts cannot reconnect an ordinary process to a destroyed PTY. Persisted state needs
versioning, atomic writes, quotas, corruption handling, access permissions, and upgrade tests.

Before Lua parity, choose a runtime and ownership model with cancellation, memory/instruction limits,
package/file/network policy, scopes, timers, events, async method bridging, and script-process cleanup. Do
not translate JSON action support into a Lua compatibility claim.

Before multi-account hosting, specify identity-to-account policy, privilege separation, credential
handling, PAM interactions, per-account servers, revocation, and broker upgrade continuity. Controller
capabilities currently run with one host account and are unsuitable as isolation between mutually
untrusted users.

Before release packaging, validate native PTY assets and signing on every advertised platform, service
lifecycle and upgrades, opt-in installation/removal, backups where applicable, support diagnostics, and
dependency/license provenance.

## 24. Terminal reference decisions

The repository requires comparison with established terminal implementations. The implementation READMEs
record the concrete upstream review:

- [Windows Terminal ConPTY connection](https://github.com/microsoft/terminal/blob/main/src/cascadia/TerminalConnection/ConptyConnection.cpp):
  transport close affects process ownership, supporting explicit detach/close separation.
- [Ghostty terminal state](https://github.com/ghostty-org/ghostty/blob/main/src/terminal/Terminal.zig):
  dimensions and normal/alternate screen behavior belong to VT state.
- [xterm.js attach addon](https://github.com/xtermjs/xterm.js/blob/master/addons/addon-attach/src/AttachAddon.ts):
  explicit input policy and connection listener cleanup.
- [xterm.js serialize addon](https://github.com/xtermjs/xterm.js/blob/master/addons/addon-serialize/src/SerializeAddon.ts):
  buffers and modes explain why arbitrary retained byte tails are not full snapshots.
- [xterm.js input handler](https://github.com/xtermjs/xterm.js/blob/master/src/common/InputHandler.ts)
  and [Windows Terminal dispatch](https://github.com/microsoft/terminal/blob/main/src/terminal/adapter/adaptDispatch.cpp):
  terminal-state queries require a defined response owner.

These references inform independent design and tests. They neither establish Rex internals nor guarantee
renderer parity. Existing PowerShell invocation and platform shell selection remain in the reused
transport providers.
