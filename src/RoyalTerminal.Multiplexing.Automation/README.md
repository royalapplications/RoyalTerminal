# Experimental JSON automation and key modes

This dependency-free package adds declarative API actions, deterministic target
lookup, configuration layering, and a client-side key-mode engine. It is an
independent experimental implementation of documented workspace interactions.
**It does not execute Lua, accept Rex Lua configuration, or claim Rex API/wire
compatibility.** The public method registry of the host supplies the available API
methods and their argument schemas.

## Host integration

Implement one authorization-preserving boundary:

```csharp
using System.Text.Json;
using RoyalTerminal.Multiplexing.Automation;

public sealed class BoundMethodInvoker(
    Func<string, JsonElement, CancellationToken, ValueTask<JsonElement>> invoke)
    : IMultiplexerMethodInvoker
{
    public ValueTask<JsonElement> InvokeAsync(
        string method,
        JsonElement arguments,
        CancellationToken cancellationToken = default)
        => invoke(method, arguments, cancellationToken);
}
```

The delegate should bind the authenticated caller and call the same permission-
checked method registry used by ordinary HTTP/CLI calls. Configuration validation
does not grant permissions. Never adapt the interface directly to unrestricted
process launch or bypass the host's authorization checks.

Construct `MultiplexerActionRunner` with that invoker and the registry's
`MultiplexerActionMethod(Name, InputSchema)` descriptions. `Validate(action)` checks
all steps without invoking a method. `RunAsync(action, cancellationToken)` takes
an owned snapshot and executes the steps in order.

## Configuration example

```json
{
  "version": 1,
  "actions": [
    {
      "id": "inspect-server",
      "timeoutMilliseconds": 5000,
      "steps": [
        {
          "method": "server.version",
          "arguments": {},
          "timeoutMilliseconds": 2000
        },
        {
          "method": "session.list",
          "arguments": {},
          "timeoutMilliseconds": 2000
        }
      ]
    }
  ],
  "modes": [
    { "name": "commands", "exclusive": true }
  ],
  "bindings": [
    { "sequence": "ctrl+b>s", "actionId": "inspect-server" },
    { "sequence": "ctrl+b>m", "enterMode": "commands", "once": true },
    { "mode": "commands", "sequence": "s", "actionId": "inspect-server" },
    { "mode": "commands", "sequence": "enter", "exitMode": true }
  ]
}
```

JSON property names are camelCase. Argument objects retain the API registry's
exact property names. Unknown properties, duplicate JSON members, invalid modes,
unsupported format versions, invalid action calls, and quota violations are
errors. Source-generated serialization is used, including when reflection-based
JSON serialization is disabled.

`MultiplexerConfigurationLoader.Parse(bytes, runner)` checks one configuration
layer and validates its action arguments. A layer may refer to action/mode names
supplied by another layer. `Merge(defaults, server, client, runner)` validates
those cross-layer references and returns the effective configuration plus
override diagnostics. Only replace an active configuration after parsing and
merging succeed. These operations never execute actions.

Configuration DTOs are editable. Merge copies definitions, the key engine
compiles its own key tables, and each action run copies every step and JSON
argument value before calling the host. Editing a draft configuration does not
change an already-compiled key engine or an already-running action.

## Layer precedence and shortcut conflicts

Layers are applied in this order:

1. Application defaults.
2. Server configuration.
3. Client overrides.

Later definitions replace earlier action IDs and mode options. Shortcut conflicts
are handled in definition order within a layer as well as across layers. A new
binding replaces the same sequence and any older immediate/sequence conflict in
that mode. For example, a newer `ctrl+k>s` hides an older `ctrl+k`; a newer
`ctrl+k` hides all older sequences beginning with that stroke. Sibling sequences
such as `ctrl+b>c` and `ctrl+b>n` coexist. Each hidden binding produces a
`binding_shadowed` diagnostic identifying the old/new layers and sequences.

A binding has exactly one operation:

| Field | Operation |
| --- | --- |
| `actionId` | Return a named action to the host for explicit dispatch. |
| `enterMode` | Push a named key table; optional `once` applies to this frame. |
| `exitMode` | Pop the top frame; the permanent default table remains. |
| `exitAllModes` | Return to the default frame. |
| `block` | Consume this shortcut without fallback or mutation. |

This format has no implicit expression language, variable interpolation, result
substitution, filesystem access, or process execution. API calls only occur
through the injected invoker.

## Key processing

```csharp
MultiplexerResolvedConfiguration resolved = MultiplexerConfigurationLoader.Merge(
    defaults, serverConfiguration, clientConfiguration, runner);
MultiplexerKeyEngine keys = new(resolved);

MultiplexerKeyDispatch decision = keys.ProcessKey("ctrl+b");
// decision.Kind == Pending: do not send this captured prefix to the terminal.

decision = keys.ProcessKey("s");
if (decision.Kind == MultiplexerKeyDispatchKind.Action)
{
    MultiplexerAction action = resolved.Configuration.Actions.Single(
        candidate => candidate.Id == decision.ActionId);
    MultiplexerActionResult result = await runner.RunAsync(action, cancellationToken);
}
else if (!decision.Consumed)
{
    // Forward only the current key through the normal terminal input encoder.
}
```

The engine is intended for one client's event loop. It does not access Avalonia,
perform network requests, or run an action itself. Modifiers normalize to the
fixed order `ctrl+alt+shift+meta`; `control`, `option`, `cmd`, `command`, `super`,
and `win` aliases are supported. Use `plus` to name the plus key. Mode names
contain lowercase ASCII letters, digits, underscores, and hyphens.

The highest stacked frame is consulted first. Nonexclusive modes allow unbound
keys to fall through to lower frames or ordinary terminal input. Exclusive modes
consume unbound keys. An explicit blocked binding suppresses fallback even in a
nonexclusive mode.

An incomplete sequence consumes its prefixes. Escape cancels that sequence
before any mode exit. Without a pending sequence, Escape pops the top mode unless
that mode explicitly binds Escape. An incomplete chord expires after an idle
deadline; `ExpirePending()` can be called by the host's timer, and every new key
also checks expiration. On a nonexclusive sequence mismatch, only the last key
is reconsidered as a fresh key; earlier captured keys are never replayed into
the terminal. Exclusive mismatches are consumed.

A once frame remains active through an incomplete chord and is removed after
its own first resolved shortcut, including a blocked binding. Unrelated unbound
keys do not remove it. Entering or leaving modes cancels pending sequences.

## Target lookup

`MultiplexerTargetResolver.Resolve(selector, orderedCandidates)` is a pure
operation with this precedence:

| Stage | Matching rule |
| --- | --- |
| 1 | Exact full ID, ordinal and case-sensitive. |
| 2 | Full label, ordinal and case-insensitive. |
| 3 | Positive one-based decimal index into the supplied display order. |
| 4 | Case-insensitive ID suffix containing at least four characters. |

An ambiguous earlier stage fails immediately; it does not fall through to a
later index or suffix. An out-of-range numeric index may still resolve as a
four-character ID suffix. Zero, negative indexes, and signs are not indexes.
The host should resolve every supplied target before beginning a mutation.
Return exact identities in subsequent requests to avoid relabeling/reordering
races. Revision checks remain the broker's responsibility.

## Typed arguments and supported schema vocabulary

The validator implements this bounded subset of JSON Schema:

- Boolean schemas; `type` and unions of supported JSON types.
- `properties`, `required`, `additionalProperties`, `minProperties`,
  `maxProperties`.
- `items`, `minItems`, `maxItems`.
- `minLength`, `maxLength`, counting Unicode scalar values.
- `minimum`, `maximum`, `exclusiveMinimum`, `exclusiveMaximum`.
- `enum`, `const`.
- Descriptive annotations: `$schema`, `$id`, `title`, `description`, `default`,
  `examples`, and `deprecated`; these do not execute anything.

Unknown validation keywords are rejected, including `$ref`, combinators,
`pattern`, and remote schema loading. This is an explicit subset, not a claim
of complete JSON Schema implementation. Numeric comparisons use normalized
decimal digits and symbolic exponents, preserving integer IDs beyond IEEE 754
precision and ordering very small numbers without expanding powers of ten.
Values that overflow the supported finite numeric range are rejected.

All schemas and argument values are bounded by byte, depth, and node limits.
An invalid later step prevents the first step from running. Validation does not
provide transactionality: a successful earlier API call remains effective if
a later call fails.

## Cancellation, deadlines, and errors

Actions run sequentially. The runner passes linked cancellation tokens to each
host invocation and bounds asynchronous waiting independently, so an invoker
that ignores cancellation cannot indefinitely block the runner. Invokers must
return their `ValueTask` promptly; arbitrary synchronous blocking code cannot be
preempted safely by this in-process library.

The action deadline covers all steps. Each step may also specify its own tighter
deadline. A timeout or failed invocation prevents every later step from starting.
`MultiplexerActionExecutionException` reports the action ID, zero-based step
index, method, error category, and successful earlier results. Caller cancellation
propagates `OperationCanceledException` with the caller's token. Arguments and
result values are not embedded in generated error messages.

Timeout cancellation cannot undo an already-started side effect. An uncooperative
method may complete after the runner has stopped waiting. Input and API calls
are never retried automatically; retrying could duplicate a command that the
remote process already consumed.

## Source decisions

The following public Rex documentation informed these interaction contracts:

- [Server API](https://www.superlogical.com/rex/docs/automate/server-api): discoverable
  methods, JSON schemas, and method calls are the common automation boundary.
- [Key sequences](https://www.superlogical.com/rex/docs/customize/key-sequences):
  captured prefixes, Escape cancellation, and newer immediate/sequence bindings
  taking precedence.
- [Key modes](https://www.superlogical.com/rex/docs/customize/key-modes): stacked
  mode tables, exclusive fallback suppression, once entry, and blocked keys.
- [Configuration](https://www.superlogical.com/rex/docs/customize/config): separate
  validation/reload from execution; keep active configuration on a failed reload.

Rex uses Lua for these facilities. This package intentionally exposes a bounded
JSON action model as its current experimental surface and does not interpret the
documented Lua API.

## Validation

```sh
dotnet test tests/RoyalTerminal.Multiplexing.AutomationTests -c Release
```

The suite exercises target ambiguity/precedence, complete preflight validation,
sequential execution, cancellation, deadlines, partial failures, snapshot
ownership, exact numeric constraints, Unicode lengths, schema limits,
reflection-disabled JSON, layer precedence, prefix shadowing, stacked modes,
exclusive/once behavior, timeout, Escape, and blocked bindings.
