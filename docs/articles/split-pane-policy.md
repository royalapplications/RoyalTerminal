---
title: Pane Split Policy
---

# Pane Split Policy

The reusable app shell can create split panes, but the decision to clone a
session is application-owned. That matters for SSH providers, MFA prompts,
credential scopes, and any transport that cannot be duplicated without host
logic.

`RoyalApps.RoyalTerminal.Avalonia.App` exposes `ITerminalPaneSplitPolicy` so
hosts can allow PTY splits, deny SSH splits, or provide a custom cloned launch
profile.

## Public API

| Type | Purpose |
| --- | --- |
| `ITerminalPaneSplitPolicy` | Evaluates every split request before the shell creates the new pane. |
| `TerminalPaneSplitContext` | Immutable context describing the source pane and default cloned launch profile. |
| `TerminalPaneSplitDecision` | Allows or denies the split and can provide a replacement launch profile. |
| `AllowAllTerminalPaneSplitPolicy` | Built-in policy that allows every transport. |
| `DelegateTerminalPaneSplitPolicy` | Built-in adapter for host callback logic. |
| `TransportAllowListTerminalPaneSplitPolicy` | Built-in policy that allows only selected transport ids. |
| `TerminalPaneSplitPolicies` | Factory and shared instances for common policies. |

The split direction enum used by the policy is
`TerminalPaneSplitRequest` from `RoyalTerminal.Avalonia.Controls`.

## Built-In Policies

Allow everything:

```csharp
MainWindow window = new()
{
    PaneSplitPolicy = TerminalPaneSplitPolicies.AllowAll,
};
```

Allow only PTY splits:

```csharp
MainWindow window = new()
{
    PaneSplitPolicy = TerminalPaneSplitPolicies.PtyOnly,
};
```

Allow selected transport ids:

```csharp
MainWindow window = new()
{
    PaneSplitPolicy = TerminalPaneSplitPolicies.AllowTransports(
        TerminalTransportIds.Pty,
        TerminalTransportIds.Pipe),
};
```

## Custom Clone Strategy

Use `DelegateTerminalPaneSplitPolicy` when the host must inspect or replace the
launch profile.

```csharp
MainWindow window = new()
{
    PaneSplitPolicy = new DelegateTerminalPaneSplitPolicy(context =>
    {
        if (context.SourceTransportId == TerminalTransportIds.Ssh)
        {
            return TerminalPaneSplitDecision.Deny(
                "Split panes are disabled for SSH sessions in this application.");
        }

        return TerminalPaneSplitDecision.Allow();
    }),
};
```

For a custom SSH provider such as Rebex, the callback can build a replacement
`TerminalSessionProfile` that scopes credential prompts, MFA state, or provider
objects to the new pane.

```csharp
PaneSplitPolicy = new DelegateTerminalPaneSplitPolicy(context =>
{
    if (context.SourceTransportId != TerminalTransportIds.Ssh)
    {
        return TerminalPaneSplitDecision.Allow();
    }

    TerminalSessionProfile clone = context.DefaultLaunchProfile with
    {
        DisplayName = $"{context.TabTitle} SSH pane",
    };

    return TerminalPaneSplitDecision.Allow(
        clone,
        "Created a new SSH pane with app-owned authentication policy.");
});
```

## Context Fields

`TerminalPaneSplitContext` includes:

- requested split direction;
- tab title;
- source pane id and optional title;
- source profile id;
- source transport id and transport profile id;
- source working directory;
- whether the source pane currently has an active session;
- the source launch profile captured from the pane;
- the default launch profile the shell would use.

The shell creates a new independent `TerminalControl` session when the decision
is allowed. It does not share a PTY, SSH channel, or transport instance between
panes.

## Relationship To Pane Layout

`ITerminalPaneSplitPolicy` lives in the app-shell package because it depends on
session profiles and product workflow decisions. The lower-level pane layout
helpers live in `RoyalApps.RoyalTerminal.Avalonia` and can be used without this
policy layer.

See [Terminal Pane Layout API](/articles/terminal-pane-layout-api) for the
control-level pane tree and split grid helpers.
