---
title: Shell Integration
---

# Shell Integration

RoyalTerminal observes structured shell metadata through OSC sequences. Shell
integration is separate from command history: it is the event stream that other
features can consume.

The APIs live in `RoyalApps.RoyalTerminal.Terminal`.

## Supported Events

`TerminalShellIntegrationParser` parses:

- OSC 7 current working directory updates;
- OSC 133 prompt lifecycle events;
- command input and output boundaries;
- command finish events with exit status when supplied;
- command line, host, and application id metadata when supplied by the shell.

Events are represented by `TerminalShellIntegrationEvent`.

| Kind | Source |
| --- | --- |
| `WorkingDirectoryChanged` | OSC 7 |
| `FreshLine` | OSC 133 `L` |
| `FreshLineNewPrompt` | OSC 133 `A` |
| `NewCommand` | OSC 133 `N` |
| `PromptStarted` | OSC 133 `P` |
| `InputStarted` | OSC 133 `B` |
| `InputStartedAndTerminatedEndOfLine` | OSC 133 `I` |
| `OutputStarted` | OSC 133 `C` |
| `CommandFinished` | OSC 133 `D` |

## Parser Usage

```csharp
TerminalShellIntegrationParser parser = new();
parser.EventReceived += (_, args) =>
{
    TerminalShellIntegrationEvent value = args.Value;
    // Update host state or pass the event to command history capture.
};

parser.TryHandleOsc(7, "file://localhost/Users/example/project");
parser.TryHandleOsc(133, "D;0");
```

The parser returns `true` only when the OSC payload is recognized and emitted as
a shell integration event.

## VT Processor Contract

VT processors that can emit structured shell integration events implement
`ITerminalShellIntegrationEventSource`.

```csharp
if (processor is ITerminalShellIntegrationEventSource source)
{
    source.ShellIntegrationEventReceived += OnShellIntegrationEvent;
}
```

`TerminalControl` relays processor events through
`ShellIntegrationEventReceived`, so Avalonia hosts can subscribe at the control
boundary without depending on a concrete processor:

```csharp
terminal.ShellIntegrationEventReceived += (_, args) =>
{
    if (args.Value.Kind == TerminalShellIntegrationEventKind.WorkingDirectoryChanged)
    {
        // Update tab, pane, or workspace context.
    }
};
```

## Design Rules

Shell integration is observational. The shell remains the source of truth for
command execution, completion, aliases, prompt state, and predictions.

RoyalTerminal therefore:

- preserves native Tab passthrough by default;
- does not scrape raw keystrokes to infer commands;
- builds command history only from structured command lifecycle events;
- keeps shell-specific bootstrap optional and host-owned.

## Bootstrap Scripts

`TerminalShellIntegrationBootstrapBuilder` generates opt-in shell snippets for:

- bash;
- zsh;
- fish;
- PowerShell.

The builder emits shell-local functions and hooks that report OSC 7 and OSC 133
metadata. It does not edit user dotfiles and it does not bind or intercept Tab,
so native shell completion remains unchanged.

```csharp
string? script = TerminalShellIntegrationBootstrapBuilder.Build(
    new TerminalShellIntegrationBootstrapOptions(
        TerminalShellIntegrationBootstrapShell.Zsh));
```

`SshShellBootstrapCommandBuilder` also has an overload that can compose
environment exports, an optional shell-integration script, and the initial
command in that order. Existing SSH bootstrap callers keep their current
behavior unless they pass `TerminalShellIntegrationBootstrapOptions`.

## Demo Integration

The demo registers shell-integration capture per `TerminalControl` pane. Working
directory updates and command lifecycle events feed command history capture, and
the active pane remains the context for command suggestions.

The managed VT processor parses OSC 7 and OSC 133 directly. The native Ghostty
VT wrapper does not currently expose dedicated shell-event callbacks, so
`GhosttyVtProcessor` implements `ITerminalShellIntegrationEventSource` by
pre-parsing OSC 7 and OSC 133 before forwarding bytes unchanged to libghostty-vt.
If future libghostty-vt bindings expose native shell-event callbacks, they can
be relayed through the same contract.

## Tests

Focused coverage:

- `TerminalShellIntegrationContractTests` for OSC 7 and OSC 133 parsing;
- `TerminalShellIntegrationContractTests` for shell bootstrap script generation
  and Ghostty pre-parser shell events;
- `TerminalControlTests` for control-level event relay;
- `MainWindowControllerModeStartupTests` for per-pane capture registration.
