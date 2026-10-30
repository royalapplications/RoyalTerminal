# Experimental durable workspaces inspired by Rex

Status: experimental implementation in progress; draft pull request only.
Research date: 2026-10-30. Baseline: RoyalTerminal 0.7.0 (`4f5d6689`).

## Purpose

Build an independently implemented, reusable durable terminal workspace on
RoyalTerminal's existing terminal and transport contracts. The experiment follows
publicly documented Rex workflows while keeping product claims, implementation
decisions, and unverified behavior explicitly separate.

Public sources include [Superlogical](https://www.superlogical.com/), the
[installation guide](https://www.superlogical.com/rex/docs/installation),
[CLI taxonomy](https://www.superlogical.com/rex/docs/automate/cli), and
[self-describing server API](https://www.superlogical.com/rex/docs/automate/server-api).
The complete research inventory and implementation contract are being developed
alongside the code in this draft.

## Architectural commitments

1. A server owns shell processes. Closing a client detaches it; it does not close
   a process. Explicit block/session close operations terminate owned processes.
2. A session contains windows and ordered terminal blocks. Stable IDs identify
   resources independently of editable labels and presentation order.
3. A reusable multiplexing library composes `ITerminalTransportFactory` and
   `ITerminalTransport`; no Avalonia dependency enters its domain model.
4. An ordered event stream carries output, terminal size changes, and process
   exit. Subscription and replay are atomic. Missing retained history is an
   explicit error, never silently presented as a complete terminal state.
5. Bounded buffers and attachment quotas prevent a disconnected or slow viewer
   from consuming unlimited memory or blocking a shell.
6. Shared sessions distinguish observation from input/control. Resize authority
   is explicit so two client viewports cannot continuously resize the same PTY.
7. The server API, command-line tool, browser client, and native transport adapter
   share one model. Wire compatibility with Rex is not claimed: its complete
   private wire protocol is not documented by the public sources.
8. Existing RoyalTerminal VT parsing, shell integration, OSC 7501 status support,
   rendering, and transport implementations are reused wherever applicable.

## Planned validation

Focused tests cover attach/detach/replay, event ordering, concurrent clients,
read-only enforcement, resize ownership, resource limits, start failure cleanup,
process exit, API validation, authentication, and actual process transport IO.
Browser and native-client integration are validated separately from the core.

## Scope accounting

The final specification will contain a feature-by-feature implementation matrix,
explicit supported run commands, test results, and remaining experimental gaps.
Surviving a client disconnect does not imply surviving a server crash or reboot.
No implementation stage may silently substitute saved layouts for live process
durability or claim native iOS/client parity without an actual tested client.
