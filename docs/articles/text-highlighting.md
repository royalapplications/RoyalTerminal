---
title: Regex Text Highlighting
---

# Regex Text Highlighting

RoyalTerminal can highlight terminal text by matching user-defined regular expressions against rendered terminal rows. A rule can set foreground color, background color, both, or neither color independently. Unset colors preserve the cell's current terminal color, so a rule can safely mark text without destroying colors emitted by the application running inside the terminal.

The feature is available in the default Skia renderer and can be configured either directly on `TerminalControl` or through persisted session profiles and the reusable settings panel.

## Runtime API

The host-facing properties live on `TerminalControl`:

| Member | Purpose |
| --- | --- |
| `TextHighlightRules` | Ordered rule set applied by the renderer. |
| `TextHighlightingMode` | Evaluation mode: `Static`, `Realtime`, or `Disabled`. |

Rules are represented by `TerminalTextHighlightRule`:

| Property | Purpose |
| --- | --- |
| `Name` | Human-readable rule name. |
| `Pattern` | Regular expression matched against one rendered terminal row at a time. |
| `IsEnabled` | Skips the rule without removing it from the rule list. |
| `Foreground` | Optional light/default foreground color as packed ARGB. |
| `Background` | Optional light/default background color as packed ARGB. |
| `DarkForeground` | Optional dark-theme foreground override. |
| `DarkBackground` | Optional dark-theme background override. |

```csharp
using RoyalTerminal.Avalonia.Controls;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;

TerminalControl terminal = new()
{
    TextHighlightingMode = TerminalTextHighlightingMode.Static,
    TextHighlightRules =
    [
        new TerminalTextHighlightRule
        {
            Name = "Errors",
            Pattern = @"\b(ERROR|FAIL|FATAL)\b",
            Foreground = 0xFFFFE6E6,
            Background = 0xFF7F1D1D,
            DarkForeground = 0xFFFFB4B4,
            DarkBackground = 0xFF450A0A,
        },
        new TerminalTextHighlightRule
        {
            Name = "IPv4 addresses",
            Pattern = @"\b(?:25[0-5]|2[0-4]\d|1?\d?\d)(?:\.(?:25[0-5]|2[0-4]\d|1?\d?\d)){3}\b",
            Foreground = 0xFF93C5FD,
        },
    ],
};
```

The rule collection is copied when assigned. Reassigning an equivalent rule set is treated as a no-op so hosts can bind or refresh settings without forcing a full redraw or regex recompilation.

## Direct renderer API

Most hosts should configure highlighting through `TerminalControl`. If you use `SkiaTerminalRenderer` directly, set the mode property and replace rules through `SetTextHighlightRules(...)`:

```csharp
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;

using SkiaTerminalRenderer renderer = new("Consolas", 14f);

renderer.TextHighlightingMode = TerminalTextHighlightingMode.Static;
renderer.SetTextHighlightRules(
[
    new TerminalTextHighlightRule
    {
        Name = "Prompt",
        Pattern = @"^\w+@\w+:[^$#]+[$#]",
        Foreground = 0xFF38BDF8,
    },
]);
```

When using the renderer directly, the caller owns frame invalidation. `TerminalControl` handles that automatically when `TextHighlightRules` or `TextHighlightingMode` changes.

## Evaluation modes

`TerminalTextHighlightingMode` controls how much work the renderer does:

| Mode | Behavior | Use when |
| --- | --- | --- |
| `Static` | Caches matched cells for unchanged row text, theme darkness, and rule revision. | Most terminals, logs, shells, and long-running sessions. |
| `Realtime` | Recomputes matching rows whenever they are rendered. | You want immediate recomputation without relying on row cache reuse. |
| `Disabled` | Keeps configured rules but skips matching and rendering overrides. | The user temporarily disables highlighting. |

`Static` is the default because terminal rows are usually redrawn far more often than their text changes. The cache is keyed by row object, row text hash, rule revision, column count, and light/dark theme state. Changing the rule set, changing the mode, or invalidating terminal rows causes the renderer to recalculate as needed.

`TerminalControl` suspends regex text highlighting while an alternate-screen application is active. Full-screen TUIs own that buffer and often render dense pseudo-random text; host-side regex overlays can otherwise look like rendering artifacts. The configured `TextHighlightingMode` is restored when the processor leaves alternate screen.

## Color semantics

Foreground and background are independent:

- If `Foreground` is set and `Background` is unset, matching cells use the configured foreground and keep their original background.
- If `Background` is set and `Foreground` is unset, matching cells use the configured background and keep their original foreground.
- If both are set, both colors are applied.
- If neither is set, the rule is retained but has no visible effect.

Dark-theme colors are optional overrides:

- `DarkForeground` overrides `Foreground` when the terminal theme is detected as dark.
- `DarkBackground` overrides `Background` when the terminal theme is detected as dark.
- If a dark override is unset, the normal color is reused for dark themes.
- If both the normal color and the dark override are unset, the original cell color is preserved.

In the settings UI, the main `Foreground` and `Background` checkboxes control whether a rule changes those colors at all. The `Dark foreground` and `Dark background` checkboxes only control dark-theme overrides. Leaving a dark override unchecked does not disable the normal foreground or background color in dark themes; it means the normal color remains the dark-theme value too.

The renderer detects the active color variant from the terminal theme's default background luminance.

## Rule ordering and overlapping matches

Rules are evaluated in list order. If multiple rules match the same cell, later rules can replace the foreground and/or background set by earlier rules. A later rule only changes the colors it explicitly configures; an unset foreground or background does not clear a color already applied by an earlier matching rule.

Selection and search match backgrounds remain higher priority than text-highlight backgrounds. This keeps interactive selection and search feedback visible even when a regex rule also matches the same cells.

## Regex matching behavior

Rules are matched against one rendered terminal row at a time. A pattern cannot match across line boundaries or across separate terminal rows.

The renderer builds row text from visible cells:

- hidden cells are skipped
- wide-cell continuations are skipped
- grapheme cells are mapped back to their terminal column
- Unicode scalar values are encoded without allocating per-cell strings
- matches that cover wide cells are expanded to cover the full visual cell width

Invalid regex patterns are ignored by the renderer so a user-authored rule cannot break terminal rendering. The invalid rule remains in the configured rule list, which lets a settings UI preserve it for correction.

## Performance details

The matching path is designed for interactive terminal rendering:

- regexes are compiled once per rule-set revision
- `RegexOptions.CultureInvariant` is always used
- `RegexOptions.Compiled` is used when dynamic code compilation is available
- `RegexOptions.NonBacktracking` is attempted first for high-throughput matching
- unsupported non-backtracking constructs, such as backreferences, fall back to the regular .NET regex engine
- each regex match has a short timeout so one expensive pattern cannot stall rendering indefinitely
- matching uses span-based row text and `Regex.EnumerateMatches(...)`
- row text and column maps are reused between rows to avoid hot-path allocations
- static mode caches row highlight overrides and clears the bounded cache when needed

For best performance, prefer row-local patterns that avoid excessive backtracking. Anchor rules when possible, avoid nested unbounded quantifiers such as `(.*)+`, and use explicit character classes for common tokens like IP addresses, timestamps, and log levels.

## Persisted profile settings

Session profiles persist the same feature under `TerminalSessionAppearanceSettings`:

| Type or property | Purpose |
| --- | --- |
| `TerminalSessionAppearanceSettings.TextHighlightingMode` | Stored evaluation mode. |
| `TerminalSessionAppearanceSettings.TextHighlightRules` | Stored rule list. |
| `TerminalSessionTextHighlightRule` | Serializable rule model. |
| `TerminalSessionProfileSerializer` | Normalizes mode, rules, and color strings during load/save. |

Persisted colors accept `#RRGGBB`, `#AARRGGBB`, `RRGGBB`, or `AARRGGBB`. The serializer normalizes valid colors to `#AARRGGBB`. Invalid or unchecked colors are stored as `null`.

```json
{
  "appearance": {
    "fontSource": "System",
    "fontFamilyName": "Menlo",
    "fontSize": 14,
    "textHighlightingMode": "Static",
    "textHighlightRules": [
      {
        "name": "Errors",
        "pattern": "\\b(ERROR|FAIL|FATAL)\\b",
        "isEnabled": true,
        "foregroundColor": "#FFFFE6E6",
        "backgroundColor": "#FF7F1D1D",
        "darkForegroundColor": "#FFFFB4B4",
        "darkBackgroundColor": "#FF450A0A"
      },
      {
        "name": "IPv4 addresses",
        "pattern": "\\b(?:25[0-5]|2[0-4]\\d|1?\\d?\\d)(?:\\.(?:25[0-5]|2[0-4]\\d|1?\\d?\\d)){3}\\b",
        "isEnabled": true,
        "foregroundColor": "#FF93C5FD",
        "backgroundColor": null,
        "darkForegroundColor": null,
        "darkBackgroundColor": null
      }
    ]
  }
}
```

The serializer skips rules with empty patterns and normalizes unknown `TextHighlightingMode` values back to `Static`.

## Settings UI

`RoyalTerminal.Avalonia.Settings` exposes the feature through the Appearance category.

The settings state surface includes:

| Type or member | Purpose |
| --- | --- |
| `TerminalSettingsAppearanceState.TextHighlightRules` | Editable rule list for bindings. |
| `TerminalSettingsAppearanceState.TextHighlightingModes` | Mode options for a combo box. |
| `TerminalSettingsAppearanceState.SelectedTextHighlightingMode` | Selected evaluation mode. |
| `TerminalSettingsAppearanceState.AddTextHighlightRuleCommand` | Adds a new editable rule. |
| `TerminalSettingsHighlightRuleState` | Editable rule item with color checkboxes and text fields. |
| `TerminalSettingsTextHighlightingModeOption` | Display model for the mode picker. |

In the built-in panel, open `Appearance`, then use `Text Highlighting` to:

1. choose `Static (cached)`, `Realtime`, or `Disabled`
2. add one or more rules
3. edit name and regex pattern
4. enable foreground and/or background colors independently
5. optionally enable dark-theme foreground and/or background overrides
6. apply or save the profile

The color checkboxes control whether a color is persisted. A disabled color field means the corresponding persisted value is `null`.

## Demo application behavior

The Avalonia demo maps persisted settings into runtime `TerminalTextHighlightRule` instances when settings are applied. Existing standalone terminal tabs receive the updated `TextHighlightingMode` and `TextHighlightRules`, and newly created standalone tabs inherit the current runtime highlighting settings.

This mirrors how the demo already applies font, opacity, terminal behavior, logging, and transport profile settings.

## Limitations

- Matching is row-local; multiline regex matches are not supported.
- Regex rules run on rendered row text, not on raw terminal byte streams.
- Invalid regex patterns are ignored at render time instead of surfacing as render exceptions.
- Color strings are normalized by profile serialization, while runtime rules use packed ARGB `uint` values.
- Text highlighting is part of the Skia cell renderer path. Hosts using a custom renderer must apply equivalent behavior themselves.
