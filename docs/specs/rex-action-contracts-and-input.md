# Action arguments, keyboard layouts and process exit receipts

## Action declaration and discovery

The [Rex action reference](https://www.superlogical.com/rex/docs/customize/actions) distinguishes an absent `args` member (open arguments), shorthand (exact named parameters with optional `?`), and an explicit JSON Schema. RoyalTerminal now makes that distinction in both local scripts and trusted operator configuration:

```lua
rex.action{
  name='mark_build', title='Mark build', category='Build', keywords='release annotate',
  description='Rename the selected workspace with the build number.',
  args={number='integer', prefix='string?'},
  run=function(ctx,a)
    return assert(rex.session.set_label{label=(a.prefix or 'Build')..' '..a.number})
  end,
}
```

`actions --describe mark_build` returns metadata and the input schema. `actions --lua` returns all operator descriptors. Bare `actions` retains its declarative JSON catalog. `lua.actions` retains the old `actions` string array and adds `actionDetails` and `bindingWarnings`. `config check` has no publication side effects. Misspelled or mistyped binding arguments produce diagnostics, and the action cannot be executed with those arguments. Invalid declarations prevent generation replacement. Titles and search text are plain text and do not become markup or executable commands. Existing RoyalTerminal actions without a title use their name as a fallback; this is deliberately more permissive than Rex's documented title requirement.

A missing `args` is not `args={}`: the latter rejects every unexpected field. Shorthand supports `string`, `integer`, `number`, `boolean`, `object`, `array`, and `any`. Omission is distinct from JSON null. Nested object, array, union, enum, const and numeric/string/cardinality constraints use the existing bounded `MultiplexerArgumentValidator` subset. Unsupported validation keywords cause a load error, not a silently weakened predicate. The schema is copied on declaration. Outer HTTP requests are validated against their exact generation before allocating an execution worker; nested and offline action invocations validate in their worker before opening the callback's execution scope. Workers have independent, bounded registries and no validation operation can perform IO on behalf of the script. An invalid invocation cannot rename a session, start an action process, queue a client action or send terminal input **via that callback**. Effects a script already performed before invoking a rejected action are not rolled back.

## Input identity and ordering

An opted-in physical `[KeyA]` binding uses the browser code, not the letter printed on the key. An ordinary logical binding uses the layout text. In one mode frame an explicit physical match (including a chord prefix) wins over its logical alias; both are tested before the next frame. IME, dead keys and AltGraph bypass command matching. Browser layout maps are optional. A declined anonymous binding resubmits the actual press or repeat, with its text and physical identity, to the server's canonical VT encoder. The encoder is a read-only operation; only the existing controller socket may send the encoded bytes. A suspended callback never acquires another input lease.

While a callback or key encoder is pending the original socket has a bounded FIFO. Subsequent typing, paste and binary input cannot overtake it. No IO queue or promise chain is allocated for an ordinary press beyond the per-lease lane; no asynchronous scheduling is needed when the lane is empty. A queue holds at most 128 items and 256 KiB of buffered encoded input; overflow is visible and rejected, not retried. Detach, revocation, reconnect or an invalidated original focus cannot retarget a declined press. Resize frames do not go through the keyboard queue. The command palette searches the current administrator-visible action descriptors by title, keywords, category, name and description; it confirms the generation and context before invoking an action and displays its input contract. Only explicitly repeatable named actions respond to keyboard autorepeat.

## PTY end-of-output is not process exit

On Linux and Darwin, closing the last slave descriptor is not a process status. `waitpid(pid, ..., WNOHANG)` returning zero means no status is available. The reader drains output and waits for the exact spawned PID, releasing the lifetime lock between probes. Explicit disposal wakes the waiter. A normal child exit yields its status and a signal exit uses the shell convention `128 + signal`; `-1` is reserved for an actual wait failure. This concerns the PTY process exit receipt, not the separate local `rex.exec` result. Disposal arranges a final reap after forced termination without blocking the caller on that reap. Plain output pipes and Windows ConPTY do not use this code path.

References: [POSIX wait semantics](https://man7.org/linux/man-pages/man3/wait.3p.html); [Ghostty subprocess watcher versus PTY reader](https://github.com/ghostty-org/ghostty/blob/main/src/termio/Exec.zig); [Windows Terminal reads the process handle for exit status](https://github.com/microsoft/terminal/blob/main/src/cascadia/TerminalConnection/ConptyConnection.cpp).

Coverage: `smoke-action-contracts.sh` uses a real interpreter and broker and exercises rollback, zero/false, nested validation and unchanged workspace labels. `UnixExitFenceTests` closes all TTY descriptors behind a file barrier and then exits normally or under SIGTERM. Browser and Node suites exercise remapped keys, held callbacks, input budgets, search metadata and focus changes. Browser layout-map support and headless synthetic layout events are not acceptance on a physical international keyboard.
