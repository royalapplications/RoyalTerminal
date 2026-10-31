# Rex product research and behavioral inventory

Research date: **2026-10-30**. Status: **experimental design input**.

This document records publicly documented Rex behavior and the corresponding
questions an independent RoyalTerminal implementation must answer. Rex is a
Superlogical product. This research does not claim access to its implementation,
private beta, unpublished API schemas, or private release notes. Product names
identify the reference product; RoyalTerminal remains its own implementation.

## 1. Evidence and scope

The research examined the public homepage, the October 5 beta announcement,
the official product screenshot, and 24 public documentation pages. The docs
publish Markdown alternatives by appending `.md` to their canonical URLs.
The public `/rex` entry point redirects to the installation documentation.

The sources support three distinct categories:

| Category | Meaning in this document |
| --- | --- |
| Documented | Explicitly described in publicly readable official documentation. |
| Observed | Visible in an official screenshot; this establishes appearance, not hidden behavior. |
| Proposed | An independent RoyalTerminal design decision or acceptance criterion. |

No private Discord membership, download entitlement, authentication, account
creation, or beta access was used. Public docs can change independently from
the installed beta. Consequently, a documented command is evidence of the
public contract, not proof that every beta build implements it identically.

### 1.1 Corrections to an overly broad reading of the homepage

- Rex currently centers on a terminal multiplexer. The broader composition and
  production-work platform is a company roadmap, not evidence of delivered CI,
  deployment, sandbox, or incident-management products. [R01]
- Session persistence means work remains alive independently of the GUI.
  Stopping a session-owning server ends its processes. It does not establish
  checkpointing of arbitrary process memory across reboot or server failure.
  [R18, R22]
- The October 5 announcement limited testing to macOS. The subsequently read
  installation documentation also exposes Linux and FreeBSD server testing.
  Windows and Linux desktop clients and iOS are described as work in progress;
  the privacy page discusses iOS behavior without establishing general release
  availability. [R02, R03, R16, R17, R24]
- Agent support means controlling terminal work through CLI, scripts, and an
  installed agent skill. These docs do not establish a bundled model service or
  an autonomous coding-agent implementation. [R10]
- The community MCP indexes documentation and beta community material. It is
  not documented as a terminal execution MCP server. [R25]
- The product being free and self-hostable does not establish that Rex's source
  code is open source, or that its private wire protocol is licensed for reuse.
  [R02]

## 2. Product model and principal workflows

### 2.1 Four persistent identities

The documented hierarchy is a server containing sessions; a session contains
ordered windows; a window contains a layout of blocks. A connected client is
a separate identity. A block currently represents an application hosted in a
terminal. Windows are the session's tabs. Sessions outlive attached viewers.
IDs are stable and never reused; labels are mutable and need not be unique.
The CLI addresses all four identity types. [R09]

**RoyalTerminal consequence:** model sessions and terminal processes outside
Avalonia controls. A tab closing, a transport disconnecting, a view disposing,
and a session terminating must be distinct operations. Do not use a UI
collection index, process ID, or label as a durable object identifier.

### 2.2 Workspace operations

The public Lua API describes binary horizontal/vertical layouts with ratios;
new windows, splits, unplaced blocks, and floating layers; block movement,
swapping, detaching, floating, zooming, and directional focus; layer movement,
resizing, and stacking; window reordering; and mutable labels. Detaching a
block removes its placement without closing it. Closing a window or layer
closes contained blocks. Terminal launch options include command argument
arrays, shell selection, working directory, initial input, and retaining the
terminal after process exit. [R23]

**RoyalTerminal consequence:** placement is not lifetime. A layout leaf should
reference a block identity; it should not own the block object. Server-side
layout mutations require invariant checks and a revision that allows every
client to identify stale structural state.

### 2.3 A normal development session

The documented shell workflow creates a project session, launches a program,
splits a neighboring terminal, adds a separate log window, and retains output
when a command finishes. The CLI can start work without stealing focus and
can wait for a process to exit, returning that program's exit code. Screen
capture supports plain text, HTML, and VT output, trimming, and unwrapping.
Scrollback is approximately 64 MiB with oldest history discarded; alternate
screen applications expose their current screen rather than hidden application
history. [R13]

**RoyalTerminal consequence:** output history must be bounded and presentation
independent. A raw output replay and a formatted current terminal screen are
different contracts. An implementation should label each correctly rather than
pretend that stripping ANSI sequences reconstructs a screen.

### 2.4 Visual evidence

The official beta screenshot shows a native macOS window with a session/host
identity, a tab strip, and three terminal panes: one tall pane on the right and
two stacked panes on the left. Each pane has a title; the active pane exposes
split, expand, and close affordances. The content includes an agent terminal,
a git TUI, and a running game. This demonstrates terminal composition and
ordinary TUI coexistence; it does not prove that any of those applications is
implemented by Rex itself. [R26]

**RoyalTerminal consequence:** reuse terminal controls with native selection,
scrolling, copy/paste, and accessibility. Render workspace furniture as ordinary
UI, not characters inside a parent terminal.

## 3. Server ownership, hosting, and remote connections

### 3.1 Personal and shared processes

Personal mode runs as the current OS account, starts automatically on ordinary
CLI use, and normally exits after roughly ten idle seconds with neither
sessions nor connections. Explicit startup keeps it resident. Shared mode
runs as a dedicated account; admitted users share that account's authority and
can interact with all its sessions. Servers listen on Unix sockets by default.
Socket and state-directory locations follow platform conventions and XDG
overrides. Shared and login services conflict, while personal servers can
coexist. [R18]

Shared Linux services can use systemd socket activation. Group membership
controls local access. Every launched command runs as the service account;
there is no claim that a shared session creates a process sandbox. OpenRC and
FreeBSD use services without systemd socket activation. Service overrides are
stored separately from package-owned unit files. [R19]

**RoyalTerminal consequence:** an opt-in daemon should own transport/process
instances and remain running after an app disconnect. Initially supporting one
account is a valid experimental boundary, but it must be explicit. An isolated
process is not an authorization boundary unless its credentials differ.

### 3.2 Login broker

The login service runs as root, identifies a connecting principal, selects an
authorized OS account from a JSONC policy, opens a PAM login session, and hands
the connection to a server running as that account. The account server survives
broker restart. Policy supports exact Tailscale identities, tagged devices,
local peer UIDs, and defined wildcards; defaults select an account without
granting access. Anonymous callers cannot select root. Policy reload retains
the previous policy on failure and disconnects clients whose grants were
removed while preserving account servers. [R20]

**RoyalTerminal consequence:** this is a separate privileged product component,
not a flag on a desktop terminal. Independent implementation needs OS-specific
credential switching, PAM lifecycle, policy matching tests, handle ownership,
audit logging, and revocation semantics before claiming equivalent support.

### 3.3 Remote access

Documented listeners include an embedded Tailscale node, Tailscale Serve to a
Unix socket, direct tailnet HTTP, and explicit anonymous HTTP. Transport identity
currently determines access. The docs explicitly call richer authentication
and authorization, including SSH key support, future work. An accepted
connection has the server account's authority. Host definitions contain
ordered endpoints; CLI host settings do not currently synchronize to the GUI.
The macOS app has an Add Remote Host command. [R21]

**RoyalTerminal consequence:** do not infer SSH wire compatibility. A secure
RoyalTerminal experiment can deliberately use a different authenticated
transport. Bind locally by default, reject unauthenticated control traffic,
and identify that behavior as RoyalTerminal's design. Existing SSH transports
can provide remote terminals without implying a Rex server connection.

### 3.4 Upgrades and durability boundaries

Updating the Linux shared service restarts it and ends its sessions. Personal
and per-account servers continue with their old executable until they stop;
the login broker may restart without ending their sessions. FreeBSD package
updates do not restart services automatically. Client and server versions need
not match exactly; unsupported features and incompatible protocols must produce
actionable errors. A version encountering newer stored data refuses to overwrite
it. [R22]

**RoyalTerminal consequence:** use a versioned protocol and explicit capability
discovery. Preserve safe old-state behavior on unknown persistence versions.
If the experiment can restore layout metadata but cannot resurrect live
processes, describe that as workspace restoration rather than process durability.

## 4. Automation contracts

### 4.1 CLI targeting and machine output

Environment context identifies the session, block, server, client, and binary
directory. Explicit flags override defaults. Target matching proceeds through
full ID, case-insensitive label, one-based position, then ID suffix of at least
four characters. Ambiguity must fail before changing anything. JSON output
includes full identities. Exit statuses distinguish ordinary failure, invalid
command line, target resolution, unreachable server, and interruption; waiting
for a command returns its exit status instead. [R09]

**RoyalTerminal consequence:** target resolution belongs in a reusable service
with deterministic matching and typed errors. A CLI should never pick an
arbitrary duplicate label or depend on current GUI focus unless focus is the
explicit target.

### 4.2 Discoverable server API

The server lists and describes methods with input/output JSON Schemas,
descriptions, examples, and optional validation notes. Discovery reflects the
connected server version. CLI and Lua surfaces derive from it. Session
mutation examples return revisions. Blocks supply their own typed methods;
terminal methods cover screen formatting, process information, working
directory, title, size, input, reset, resize, theme, status, and directory
listing. Unknown or invalid arguments produce structured failures. [R14, R23]

**RoyalTerminal consequence:** build one method registry used by the CLI,
remote transport, and future agent integration. Discovery should describe the
methods actually supported by a running build. Public examples do not reveal
Rex's complete network framing, HTTP routes, resumption, or handshake protocol;
RoyalTerminal must define and version its own.

### 4.3 One-shot scripts

The CLI runs Lua from a file, inline text, or stdin and accepts typed arguments
or a JSON argument object. Scripts execute on the CLI machine. They can invoke
server methods and local processes; standard Lua file/environment APIs are
available, with dedicated process execution replacing `io.popen` and
`os.execute`. Nested layout builders construct data without performing calls.
Scripts return JSON-compatible values and may stay alive for events or timers.
Command arguments are arrays, so shell parsing requires explicitly launching
a shell. [R11]

### 4.4 Events and long-running scripts

Scripts register handlers during startup. Handlers run sequentially. Scope may
be one session or the entire server, including future sessions. Handler API
context follows the event's session. Timer context follows the script's original
session unless explicit IDs are passed. Handler failures normally report and
continue; fail-fast is optional. Scripts stop through cancellation, a selected
deadline, explicit exit, or destruction of their scoped session. Event waits
and sleeps block other handlers in that script. [R12]

Events describe structural session changes, client lifecycle, terminal title
and directory, process exit, foreground process, bell, clipboard, notifications,
progress, and program status. Structural changes expose a view revision. The
CLI streams newline-delimited JSON with server/session/block envelopes; Lua
flattens those envelopes for handlers. Consumers should tolerate future fields
and event names. [R15]

**RoyalTerminal consequence:** distinguish the immutable state snapshot from an
event stream. Bound queues, disconnect behavior, replay cursors, ordering, and
slow-client policy require explicit contracts. Event names alone do not prove
Rex's delivery guarantees; these must be independent design choices.

### 4.5 Timers and process execution

The documented runtime is Lua 5.1. API availability differs between
configuration load, configuration callbacks, script startup, and script
callbacks. Timers serialize with the script and coalesce missed interval ticks.
The documented concurrent timer limit is 1,024. Process execution supports
environment overrides, working directory, stdin, output capture or callbacks,
timeouts, cancellation, and explicit detach. Commands ordinarily belong to the
callback or script scope that created them. [R23]

**RoyalTerminal consequence:** script cancellation should be structured, with
owned child processes stopped and handles disposed. Adding a scripting engine
is a distinct dependency decision; a declarative action engine must not be
advertised as Lua source compatibility.

## 5. Configuration, keyboard system, and actions

### 5.1 Configuration transaction

Rex loads an optional `init.lua` from platform config directories or an
environment override. The server reads it. It supports modular `require` files,
bindings, actions, and host declarations. Check validates without applying;
reload updates connected clients. Reload is local-only and explicit, not
file-watched. Invalid individual calls are diagnosed while other declarations
load; unrecoverable Lua errors retain the previously working configuration.
Live API calls are unavailable during initial declaration because there is no
client context. [R04]

**RoyalTerminal consequence:** configuration evaluation should produce an
immutable candidate snapshot, diagnostics, and a generation identifier. Apply
only a valid candidate atomically and keep the last known good configuration.

### 5.2 Keybindings

Bindings use normalized modifier names, unshifted logical keys, or explicit
physical W3C key codes. The layers are client defaults, server configuration,
then client overrides. The newest definition wins within a layer. Action
metadata belongs to the client and can be enumerated. Anonymous Lua bindings
may decline a key, allowing older handlers or terminal input. Unbinding removes
the relevant shortcut instead of sending a special command to the process.
Diagnostics retain source file and line information. [R05]

### 5.3 Sequences

Sequential key chords are arbitrary length and use a separator between strokes.
Escape cancels a pending chord. A prefix key is consumed, so users can bind a
double prefix to send the original control character. When an immediate binding
conflicts with a sequence prefix, the newer/higher-priority binding wins without
an ambiguity delay; diagnostics identify the conflict. Removing a standalone
prefix does not remove longer sequences. [R06]

### 5.4 Modes

Modes create independent binding tables and form a stack, with the newest mode
checked first. Modes may be one-shot or persistent. Ordinary modes allow
unmatched keys to fall through; exclusive modes consume them. Escape normally
pops a mode unless rebound. A blocked key is different from an absent binding.
Modes can contain sequences but call named actions rather than anonymous Lua
functions. Removing an entire mode removes its options, bindings, and blocked
keys together. [R07]

### 5.5 Custom actions

Actions have a name, display metadata, an argument contract, and an executable
callback. They appear in the command palette and can run from CLI or keyboard.
Arguments support simple types or JSON Schema. Context identifies session,
block, client, and invocation origin. Client actions queued by a callback run
after success and are discarded on callback failure. App control requires its
Remote Control preference. Action execution has a thirty-second limit;
explicitly detached child processes may outlive it. [R08]

**RoyalTerminal consequence:** keyboard commands, command palette items, remote
actions, and scripts should share action definitions. App-local actions must
remain separate from server mutations. The user's authorization to launch an
experimental app does not automatically grant every remote client control of
local UI or clipboard.

## 6. Program Status Protocol: OSC 7501

The public protocol revision is **0.4, dated 2026-10-10**. This is the most
precisely documented interoperable surface and can be implemented independently
of Rex's private server. [R27]

### 6.1 Behavior to implement

| Area | Public protocol contract |
| --- | --- |
| Envelope | OSC number 7501; BEL or ESC-backslash terminator. |
| Body | Colon-separated key/value pairs; unknown keys ignored; repeated keys use the last valid pair. |
| State | `idle`, `working`, `done`, `blocked`, `error`; `clear` removes state. |
| Record identity | Missing ID addresses the root; valid slash-separated IDs express parent/child relationships. |
| Replacement | A report replaces one whole record, so omitted metadata is removed. |
| Clearing | Clear removes the addressed record and descendants, including descendants whose parent record is absent. |
| Inheritance | An absent application identity inherits from the nearest ancestor that supplies one. |
| State lifetime | Process exit and OSC 133 prompt-start discard working/blocked; done/error survive. |
| Screen lifetime | Alternate screen switches do not clear records; RIS does; DECSTR does not. |
| Blocking reason | Permission, question, or authentication; relevant only to blocked records. |
| Progress | Integer percentage 0–100 for working/blocked; absence means indeterminate. |
| Human text | Base64 UTF-8 for title/message; optional padding; controls forbidden; never markup. |
| Query | A `?` query receives a constant capability response; no user-supplied payload is echoed. |

### 6.2 Bounds and validation

The protocol defines hard storage and parsing limits: 4,096 bytes per complete
sequence, 16 bytes per key, 2,048 decoded message bytes, 192 decoded title
bytes, 32 application-ID bytes, 128 total record-ID bytes, 32 per ID segment,
and eight ID levels. Implementations support at least 64 records and may cap
at 256, evicting the least recently updated record. Check the entire report
before mutation; rejected base64, prohibited text, or a limit violation must
leave state unchanged. [R27]

### 6.3 Product presentation and independent design

Rex documents session indicators for completed or blocked work, progress and
blocking indicators on unfocused tabs, and Lua events for status updates.
Mobile push notifications and a priority inbox are described as future
expansion. [R27]

**RoyalTerminal acceptance requirements:**

1. Recognize sequences split at every possible input boundary without retaining
   unbounded buffers.
2. Preserve ordinary VT output, existing terminal hooks, and input reply routing.
3. Keep parsing independent of Avalonia and the selected VT backend.
4. Validate rejected reports atomically, including metadata preceding the error.
5. Test hierarchical clear, inheritance, LRU eviction, replacement, prompts,
   exit, reset, alternate screens, query, and malformed UTF-8/base64.
6. Present externally supplied text plainly with origin visible; do not infer
   workflow semantics from a status message or execute it.
7. Coalesce UI changes and rate-limit disruptive notifications independently of
   record correctness.

## 7. Agent integration

Rex's setup command discovers supported agents, installs an owned skill and
where supported a startup hook, and supports status, dry run, per-project
installation, selection, removal, and explicit replacement of foreign files.
The public guide lists Claude Code, Codex, OpenCode, and Pi. The hook supplies
session/block context; the skill reads the guide from the installed CLI, so
binary upgrades update behavior. Requests distinguish the agent's own pane,
the user's focused pane, and explicitly named targets. [R10]

**RoyalTerminal consequence:** publish a machine-readable discovery endpoint
and a CLI usage guide before adding agent-specific installers. Installers must
preserve unrelated settings, identify owned files, and avoid writing credentials
into instructions. An opt-in MCP adapter can wrap the same registry, but it
would be RoyalTerminal functionality rather than a copy of the documented
Rex community MCP.

## 8. Packaging and privacy

Linux documentation lists x86-64 and ARM64, glibc and musl builds, package-manager
integration, Nix, personal CLI use without an init system, and shared/login
services with systemd or OpenRC. Stable, preview, and snapshot channels are
defined; the page says no stable release exists yet. Packages include terminfo
and an SSH terminal-type fallback. [R16]

FreeBSD documentation lists version 14 or newer, x86-64 and ARM64, base-system
dependencies, rc.d services, and package signing fingerprints. Its paths and
upgrade behavior differ from Linux. [R17]

The privacy document says Rex has no application usage telemetry, its own crash
reporter, or account/license checks. macOS performs update checks; remote server
connections are user configured. Tailscale is opt-in and has its own telemetry
and network behavior. iOS analytics depend on Apple's distribution and sharing
settings. The server and CLI make no outbound connections by default. [R24]

**RoyalTerminal consequence:** experiments should remain opt-in, avoid automatic
network discovery, and expose listener and history-storage settings. Packaging
or release availability in one OS must not imply that the others were tested.

## 9. Independent implementation requirements matrix

The following is a specification checklist, not a statement that every item
is already implemented in RoyalTerminal. Each implementation PR must report
which rows it completes and how they were verified.

| ID | Capability | Minimum acceptance evidence |
| --- | --- | --- |
| RX-001 | Durable server ownership | A process keeps producing output after all GUI clients detach; a new client observes the same process. |
| RX-002 | Stable identities | Rename/reorder operations preserve IDs; duplicate names fail ambiguous targets. |
| RX-003 | Session/window/block model | Create, enumerate, rename, close, and inspect through the same service/API. |
| RX-004 | Split layouts | Arbitrarily nested horizontal/vertical splits preserve ratios and cannot reference missing or duplicate leaves. |
| RX-005 | Block lifetime | Detach and reattach preserve a process; close terminates and releases it once. |
| RX-006 | View operations | Focus, zoom, split resizing, swapping, moving, floating, and window ordering have deterministic semantics. |
| RX-007 | Terminal transport reuse | Existing PTY/ConPTY and other transports remain behind contracts; no separate terminal emulator in the app. |
| RX-008 | Shared viewing | Two clients receive the same output; disconnecting one does not stop the other. |
| RX-009 | Input ownership | Simultaneous writers, read-only mode if supplied, and resize arbitration are explicitly defined and tested. |
| RX-010 | Reconnection | Resuming after a gap either replays bounded history or returns an explicit reset/gap response. |
| RX-011 | Capture | Current screen, scrollback, and raw replay are distinguishable; alternate screen behavior is correct. |
| RX-012 | Process completion | Exit state is preserved after completion, and wait commands return the process result. |
| RX-013 | API discovery | Supported methods expose validated schemas and examples; unsupported methods fail structurally. |
| RX-014 | Event stream | Changes are ordered, scoped, bounded, and cancelable; subscriber failure does not block PTY ingestion. |
| RX-015 | CLI | JSON, stable targeting, typed errors, cancellation, and explicit command argv are tested. |
| RX-016 | Remote hosting | Local-only defaults, authenticated access, no credentials in logs, explicit protocol version. |
| RX-017 | Configuration | Check/reload is atomic; errors retain last known good state; source diagnostics remain inspectable. |
| RX-018 | Keyboard/action system | Layers, chords, conflicts, mode stack, one-shot/exclusive modes, argument validation. |
| RX-019 | Lua runtime | Only claim Lua compatibility after documented runtime, scopes, timers, events, and process ownership work. |
| RX-020 | Program status | OSC 7501 parsing, record lifecycle, capability query, UI aggregation, bounded malformed-input handling. |
| RX-021 | Agent support | Discovery plus reliable context; setup/removal touches only owned settings and supports dry run. |
| RX-022 | Multi-account broker | Separate credentials, PAM/policy tests, revocation, and broker restart do not kill account servers. |
| RX-023 | Persistence | Versioned snapshots, bounded history, atomic writes, explicit limits on process recovery. |
| RX-024 | Product UI | Keyboard-accessible workspace, session picker, host selection, palette, block status, native terminal interaction. |
| RX-025 | Packaging | Every advertised OS/runtime is built and tested; service-install scripts are opt-in and reversible. |

## 10. RoyalTerminal architectural fit

The existing repository already provides backend-neutral terminal endpoints,
separate terminal session services, PTY/ConPTY and pipe transports, SSH and
other remote transports, managed and Ghostty VT processors, capture/replay,
history, an Avalonia terminal control, and sample applications. These are the
foundation; the missing product layer must compose them.

Recommended boundaries are independent domain models and layout algorithms;
a terminal-session host owning transport lifetime; a bounded output/event
store; a versioned remote server/client pair; a thin CLI; framework-independent
ReactiveUI ViewModels; and Avalonia views bound to those ViewModels. A separate
sample app can expose the experimental product without making every library
consumer depend on a daemon, scripting engine, or network stack.

The experimental namespace, protocol names, command name, application title,
branding, and package identity should be RoyalTerminal's. Interoperation with
the public OSC 7501 terminal sequence is an intentional compatibility point.
Compatibility with undocumented Rex server routes or binary framing is not
assumed.

## 11. Evidence gaps and research limits

The public material does **not** establish the following:

- Complete server method schemas for a specific installed release, beyond the
  examples and method descriptions published in the docs.
- Exact HTTP routes, upgrade protocol, wire framing, compression, flow control,
  resumption tokens, output encoding, or client snapshot synchronization.
- Internal programming language, terminal state replication algorithm, storage
  database, process supervisor, or renderer implementation.
- A complete released block-plugin ABI. The navigation mentions block plugins,
  but the examined public docs do not provide a plugin specification.
- Arbitrary process recovery after server termination, reboot, or host failure.
- Per-session role permissions or an application authentication system beyond
  the documented transport-identity model.
- Benchmarks proving latency, memory, bandwidth, or scale claims.
- A generally available web client, production control plane, managed cloud,
  mobile notifications, or commercial tier feature set.
- All beta changelog entries and team/community discussions. The documented
  community MCP requires beta Discord OAuth and was not accessed.

These gaps are design work for an independent implementation. They must not be
filled with claims about Rex internals, nor marked complete merely because a
prototype has a similarly named API.

## 12. Source register

All sources below are official Superlogical pages retrieved on 2026-10-30.
The linked canonical HTML pages are suitable for human review; raw `.md`
alternatives were used for precise documentation inspection.

| ID | Source |
| --- | --- |
| R01 | [Superlogical homepage](https://www.superlogical.com/) |
| R02 | [Public testing is beginning, 2026-10-05](https://www.superlogical.com/updates/public-testing-beginning) |
| R03 | [Installation](https://www.superlogical.com/rex/docs/installation) |
| R04 | [Configuration file](https://www.superlogical.com/rex/docs/customize/config) |
| R05 | [Keyboard shortcuts](https://www.superlogical.com/rex/docs/customize/keyboard-shortcuts) |
| R06 | [Key sequences](https://www.superlogical.com/rex/docs/customize/key-sequences) |
| R07 | [Key modes](https://www.superlogical.com/rex/docs/customize/key-modes) |
| R08 | [Custom actions](https://www.superlogical.com/rex/docs/customize/actions) |
| R09 | [The CLI](https://www.superlogical.com/rex/docs/automate/cli) |
| R10 | [Agents](https://www.superlogical.com/rex/docs/automate/agent-skill) |
| R11 | [Lua scripts](https://www.superlogical.com/rex/docs/automate/lua-scripts) |
| R12 | [Lua events](https://www.superlogical.com/rex/docs/automate/lua-events) |
| R13 | [Shell scripting](https://www.superlogical.com/rex/docs/automate/shell-scripting) |
| R14 | [Server API](https://www.superlogical.com/rex/docs/automate/server-api) |
| R15 | [Events](https://www.superlogical.com/rex/docs/reference/events) |
| R16 | [Installing on Linux](https://www.superlogical.com/rex/docs/linux/installation) |
| R17 | [Installing on FreeBSD](https://www.superlogical.com/rex/docs/freebsd/installation) |
| R18 | [Server modes](https://www.superlogical.com/rex/docs/linux/server-modes) |
| R19 | [Shared server](https://www.superlogical.com/rex/docs/linux/shared-server) |
| R20 | [Login server](https://www.superlogical.com/rex/docs/linux/login-server) |
| R21 | [Remote access](https://www.superlogical.com/rex/docs/linux/remote-access) |
| R22 | [Upgrading](https://www.superlogical.com/rex/docs/linux/upgrading) |
| R23 | [Lua API](https://www.superlogical.com/rex/docs/reference/lua) |
| R24 | [Privacy and telemetry](https://www.superlogical.com/rex/docs/reference/privacy) |
| R25 | [Community MCP](https://www.superlogical.com/rex/docs/beta/community-mcp) |
| R26 | [Official beta screenshot](https://www.superlogical.com/rex-screenshot@2x.webp) |
| R27 | [Program Status Protocol, OSC 7501](https://www.superlogical.com/rex/docs/build/program-status) |
