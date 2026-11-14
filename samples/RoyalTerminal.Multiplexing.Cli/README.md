# Experimental workspace CLI

`royalterminal-mux` manages the opt-in multiplexer host over authenticated HTTP.
It can create independent terminal blocks, capture the current screen, discover
the host's API, and invoke configured multi-step actions. Its commands emit JSON
for scripts. Terminal processes belong to the host and continue when this CLI
exits.

## Run

Start `samples/RoyalTerminal.Multiplexing.Server` first. Set
`ROYALTERMINAL_MUX_TOKEN` to a host credential in the current shell; the CLI never
accepts credentials in command arguments or URLs. The endpoint defaults to
`http://127.0.0.1:7331/`, with `ROYALTERMINAL_MUX_URL` and then `--endpoint URL`
overriding that default.

```sh
dotnet run --project samples/RoyalTerminal.Multiplexing.Cli -- sessions
dotnet run --project samples/RoyalTerminal.Multiplexing.Cli -- new Work
dotnet run --project samples/RoyalTerminal.Multiplexing.Cli -- run Work --label Build -- /bin/bash
dotnet run --project samples/RoyalTerminal.Multiplexing.Cli -- capture Work Build
dotnet run --project samples/RoyalTerminal.Multiplexing.Cli -- api list
dotnet run --project samples/RoyalTerminal.Multiplexing.Cli -- api describe terminal.format
```

Use a platform-appropriate executable. `run` defaults to a PTY. Add `--pipe`
for an ordinary process with redirected standard streams, or `--cwd PATH` for
an existing directory on the **host**. Executable arguments after `--` stay
separate; the CLI does not interpolate a shell command. `send SESSION BLOCK TEXT`
sends the supplied UTF-8 text directly, so include a newline if the remote shell
should execute it.

## Target selectors

Session, block, and window arguments accept these selectors in order:

1. A complete nonempty GUID. This goes directly to the host so scoped credentials
   can act on known identities without a listing request.
2. A case-insensitive label from the authorized listing.
3. A one-based index in that listing's current order.
4. A unique GUID suffix of at least four characters.

An ambiguous label or suffix is an error; no mutation is sent. A numeric label
wins over an index. Blocks and windows resolve within the selected session.
The CLI bounds candidate lists and validates returned identities before
constructing request paths. Use complete GUIDs for repeatable automation when
other clients may change names or listing order.

## Configuration and actions

```sh
dotnet run --project samples/RoyalTerminal.Multiplexing.Cli -- config show
dotnet run --project samples/RoyalTerminal.Multiplexing.Cli -- config load samples/RoyalTerminal.Multiplexing.Server/automation.example.json
dotnet run --project samples/RoyalTerminal.Multiplexing.Cli -- actions
dotnet run --project samples/RoyalTerminal.Multiplexing.Cli -- do capture_focused Work Build
```

`config show` returns the effective `configuration` and override `diagnostics`.
`config load FILE` replaces the server configuration with a JSON document from
that local file. Use `config load -` to read standard input. Input is limited to
one MiB of UTF-8 JSON. The host validates the complete document, including method
contracts and keybinding references, before replacing its current configuration.
Loading configuration does not invoke actions. These management operations
require the host's administrator credential.

`actions` returns `{"actions":["action_id",...]}` in configuration order.
`do ACTION [SESSION [BLOCK [WINDOW]]]` invokes an action once, expanding its
`${sessionId}`, `${blockId}`, and `${windowId}` context on the server. IDs may use
ASCII letters, numbers, periods, underscores, and hyphens up to 128 characters;
the CLI excludes `.` and any `..` sequence so the path cannot traverse endpoints.

When an explicit session is omitted, action context comes from
`ROYALTERMINAL_MUX_SESSION`, `ROYALTERMINAL_MUX_BLOCK`, and
`ROYALTERMINAL_MUX_WINDOW`. The host injects the session variable into launched
blocks; block/window variables may be supplied by the caller. An explicit session
replaces the entire environment context so a block from another session is never
silently reused. An action may omit all context when its steps do not need it.

The CLI allows 130 seconds per HTTP request to accommodate the host's maximum
120-second action deadline. Ctrl+C cancels the invocation. Neither the CLI nor
the action runner retries an action automatically. Earlier completed steps may
have side effects if a later step fails, times out, or the connection is lost.
Check the host state before deciding whether to run that action again.

## Direct API calls

`api list` and `api describe METHOD` read the running host's method catalog.
`api call METHOD JSON` submits an argument object; use `-` in place of JSON to
read standard input. This path does not substitute target labels inside arbitrary
JSON. Supply the exact identities expected by the advertised schema.

`capture SESSION BLOCK` returns the current screen's text and metadata. It is
not a durable terminal recording. Closing a block or session requires explicit
`stop SESSION BLOCK` or `close SESSION` commands.

Exit codes: `0` success, `1` network/file/host failure, `2` invalid arguments or
ambiguous/missing target, `130` cancellation. Errors include stable host codes
when available, without echoing raw terminal response bodies or credentials.
