---
title: Command History And Suggestions
---

# Command History And Suggestions

RoyalTerminal persists command history as structured shell-integration data, not
as raw input logging. This lets hosts offer command recall and suggestions while
preserving native shell completion and prediction behavior.

The APIs live in `RoyalApps.RoyalTerminal.Terminal`.

## History Document

`TerminalCommandHistoryDocument` stores:

- `FormatVersion`;
- `RetentionLimit`;
- `Entries`.

Each `TerminalCommandHistoryEntry` stores:

- stable `Id`;
- `CommandLine`;
- `StartedAtUtc`;
- optional `CompletedAtUtc`;
- optional `ExitCode`;
- optional `WorkingDirectory`;
- optional `ProfileId`;
- optional `TransportId`;
- optional `Host`;
- optional `ShellId`;
- optional `ApplicationId`.

The default retention limit is `5000` newest commands.

## Capturing Commands

`TerminalCommandHistoryCaptureService` converts shell integration events into
completed command entries.

```csharp
TerminalCommandHistoryCaptureService capture = new(
    new TerminalCommandHistoryCaptureContext(
        ProfileId: "default",
        TransportId: TerminalTransportIds.Pty,
        Host: "localhost"));

TerminalCommandHistoryEntry? completed = capture.Process(shellEvent);
if (completed is not null)
{
    document.Entries.Add(completed);
}
```

See [Shell Integration](/articles/shell-integration) for the OSC event contract
that feeds this service.

## Serialization

Use `TerminalCommandHistorySerializer` to load and save history:

```csharp
await using FileStream input = File.OpenRead("command-history.json");
TerminalCommandHistoryDocument loaded =
    await TerminalCommandHistorySerializer.LoadAsync(input);

string json = TerminalCommandHistorySerializer.ToJson(loaded);
```

The serializer normalizes documents during load and save:

- removes blank commands;
- trims optional text;
- validates required ids;
- normalizes transport identifiers;
- ensures stable entry ids;
- enforces the retention limit.

## Stores

`ITerminalCommandHistoryStore` abstracts persistence:

```csharp
ITerminalCommandHistoryStore store =
    TerminalCommandHistoryStoreFactory.CreateDefault();

TerminalCommandHistoryDocument history = await store.LoadAsync();
await store.SaveAsync(history);
```

`JsonFileTerminalCommandHistoryStore` persists JSON with atomic file writes.
`TerminalCommandHistoryStoreFactory.CreateDefault()` stores
`command-history.json` next to the default session profile file.

## Suggestions

`TerminalCommandSuggestionService` produces suggestions from persisted command
history and implements `ITerminalCommandSuggestionProvider`.

```csharp
TerminalCommandSuggestionService service = new();
IReadOnlyList<TerminalCommandSuggestion> suggestions =
    service.GetSuggestions(new TerminalCommandSuggestionRequest(document)
    {
        Query = "git",
        WorkingDirectory = "/repo",
        ProfileId = "default",
        TransportId = TerminalTransportIds.Pty,
        Snippets = TerminalCommandSnippets.GetDefaultSnippets(),
        Limit = 10,
    });
```

Suggestion behavior:

- filters by prefix, token-boundary match, or substring match when a query is
  provided;
- scopes history by profile id and transport id when supplied;
- deduplicates by command line;
- counts repeated use;
- scores working directory affinity, frequency, snippet aliases, and query match
  quality;
- then orders by score, most recent use, and command text for stable results.

`TerminalCommandSuggestion` records the suggestion `Kind`, optional
`Description`, and numeric `Score` in addition to command text, directory,
last-use timestamp, and use count.

## Snippets

Snippets are represented by `TerminalCommandSnippet`:

```csharp
new TerminalCommandSnippet("gs", "git status", "Show repository status")
```

RoyalTerminal ships default snippets through
`TerminalCommandSnippets.GetDefaultSnippets()`. Session profiles also expose
`CommandSnippets`, so hosts can offer profile-scoped snippets alongside the
built-in list. The serializer trims snippet trigger, command, and description
and drops blank or duplicate snippet entries.

## Demo Integration

The demo command-history overlay uses explicit shortcuts:

| Command | Shortcut |
| --- | --- |
| Open command-history overlay | `Ctrl+Space` |
| Accept selected suggestion | `Ctrl+Enter` |

Tab is not intercepted. Native shell completion keeps its default behavior.

Command history capture is registered per pane. Suggestions use the active pane
context, so profile, transport, working directory, shell id, and shell
integration metadata stay aligned with the pane the user is typing in. The demo
passes active profile snippets first, then built-in snippets, into the provider
request.

## Tests

Focused coverage:

- `TerminalCommandHistorySerializerTests` for normalization, retention, and
  round trips, scoped provider requests, and snippet ranking;
- `TerminalShellIntegrationContractTests` for event-to-history capture;
- `MainWindowViewModelFlowTests` for overlay command routing;
- `MainWindowControllerModeStartupTests` for active-pane command history wiring.
