# Shell-driven workspace workflows

The experimental `royalterminal-mux` CLI now exposes the everyday shell workflows described in the [Rex shell guide](https://www.superlogical.com/rex/docs/automate/shell-scripting). The protocol and IDs are RoyalTerminal's, not a connection to a Rex server.

## Build a workspace from a script

```sh
mux=royalterminal-mux
receipt=$($mux new Work --json --label editor --cwd "$PWD" -- nvim)
s=$(printf '%s' "$receipt" | jq -r .session_id)
b=$(printf '%s' "$receipt" | jq -r '.block_ids[0]')
$mux split -s "$s" -b "$b" --split=below --ratio 30 --wait -- dotnet test
$mux run -s "$s" --label logs -- tail -f app.log
```

`new LABEL --json` creates a session and a terminal in its first window; legacy `new LABEL` still creates an empty session. `run -s SESSION` creates a new window; positional `run SESSION -- EXE …` keeps its historical append behavior. `split` starts a new process beside its anchor. Directions are `left`, `right`, `above`, `below`. The percentage is always the **new pane's** share, not necessarily the first child's ratio. Commands accept literal argv after `--`, including empty arguments. With no executable, the host chooses its default shell. A live workspace never gains keyboard control just because a script changed its layout.

## Targets and results

Use `-s`, `-w`, `-b` or their long forms to scope a command. UUID, case-insensitive label, one-based position and unique suffix are resolved within the authorized session. A window restriction applies before block matching, including when a full block UUID is supplied. A command launched inside a terminal can use `ROYALTERMINAL_MUX_SESSION` and `ROYALTERMINAL_MUX_BLOCK` for context; its launch window need not still be its home. Without an explicit or inherited block, a command that needs a target accepts exactly one eligible candidate. Multiple candidates are an error, not a guess at a remote viewer's focus. The operator must supply a credential separately; the parent does not inject its admin token into the child.

Target resolution precedes mutation. Split/new-window launch includes the observed revision and is not retried on conflict. With `--json` the launch receipt includes `session_id`, `window_id` and `block_ids`. Splits include `anchor_block_id`. There are no independent identities for split nodes. `--focus=false` never moves another client's focus; `--focus=true` is rejected rather than silently ignored. The broker retains exited terminals, so `--keep-open` selects its existing retention policy. Creating a session and its first terminal spans two requests; if the latter fails, stderr reports the known session ID instead of deleting a potentially concurrently populated workspace. A network failure during a mutation has an uncertain outcome: inspect before reissuing it.

```sh
printf 'make test\n' | royalterminal-mux send -s Work -b editor
royalterminal-mux send-key -s Work -b editor ctrl+c
royalterminal-mux capture -s Work -b logs --trim --unwrap
royalterminal-mux capture -s Work -b logs --format html > output.html
```

Flag-oriented `send` reads raw stdin through EOF, including zero and non-UTF-8 bytes. The 64 KiB limit applies **before** any write; data is sent once, never chunked/retried. A literal text operand is UTF-8 encoded; no newline is appended. Control authority is enforced by the server, even for exact IDs. `capture` writes the requested format without a JSON wrapper or an extra newline. `--json` returns the structured capture; legacy `capture SESSION BLOCK` remains JSON. Use `--` to escape a text operand beginning with a dash.

## Wait without controlling a terminal

`wait -s SESSION -b BLOCK` returns the child status, including nonzero. `wait -s SESSION -w WINDOW` waits for that window to disappear; `wait -s SESSION` waits for session removal. A bare `wait BLOCK` uses the current session. Use `--for 500ms`, `10s`, `5m` or `1h` for a bounded wait. A deadline returns 1 and never terminates or restarts the child. Interrupt returns 130. The waiter subscribes to metadata, not terminal output. It establishes a start-at-now cursor before taking a fresh descriptor, so an immediate exit is not missed. Window and session waits use structural long polls. Revoked access or a failed read must not be reported as a successful close. Status 2 identifies a CLI syntax error, 3 a missing or ambiguous target, and 4 a transport failure. A child can itself return one of these numbers: consult stderr and the launch receipt. Use `--json` to obtain the child's ID and exit status on stdout.

The acceptance script `scripts/workspace/smoke-shell-workflows.py` launches an isolated broker and actual PTYs/pipe subprocesses. It covers both insertion sides, argv integrity, resource ownership, deadline behavior, read-only tokens, byte-preserving stdin, and coexistence with the positional interface. This is a shell CLI compatibility layer, not a claim of complete Rex parity.
