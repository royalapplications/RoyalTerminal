# IME/layout and snapshot quota follow-up

Implementation follow-up to PR #116, requested September 24, 2026. This does not
reopen the earlier full managed-parity scope.

## Behavior and references

| Area | Implemented | Remaining boundary |
| --- | --- | --- |
| IME host | Avalonia text-input client; preedit overlay; UTF-16 caret/candidate rectangle; commit/cancel; focus/session reset; no terminal-output surrounding text | Interactive Japanese/Chinese/Korean platform sign-off; Avalonia macOS/IMM currently omit the preedit caret offset, so the host uses the end |
| Composition encoding | Shared native/managed `IsComposing`; suppress ordinary raw keys and late confirming-key releases; retain Kitty modifier reporting; separate committed text | The platform still owns event ordering; headless tests cannot prove every installed IME |
| Layout metadata | Injectable `ITerminalKeyboardLayout`; Windows non-mutating `ToUnicodeEx`; macOS TIS/`UCKeyTranslate`; unshifted scalar and consumed modifiers reach both encoders | Linux modified-key metadata requires host injection until Avalonia exposes its XKB/Wayland keymap; no fabricated US-layout fallback |
| Preedit rendering | UI-only immutable grapheme cells, font fallback/shaping, underline and block caret, whole-cluster clipping; cursor priority overrides hidden/password/blink | Cross-cluster complex-script joining and rich IME clause attributes are not implemented |
| Snapshot quota admission | Source byte/row limits restored and exported; host overrides; native page alignment/minimums; whole-page rejection and permanent gap; hard decode limits retained | Logical page charge is not CLR heap usage; existing host hard row cap remains an additional policy |
| Live snapshot accounting | Retained page identities survive COW and partial pruning; new rows and changed style/grapheme/link storage are measured against the live unpublished screen | Managed growth/reflow does not reproduce Ghostty allocator page-split/growth history exactly; content-derived conservative capacity is used for modified/new rows, not a claim of exact mutable allocator parity |

Reference decisions:

- Ghostty `src/input/key_encode.zig`: suppress non-modifier composition events;
  pass layout-derived unshifted codepoints and consumed modifiers to the encoder.
- Ghostty `src/renderer/State.zig` and `cursor.zig`: keep preedit local to the
  renderer, clamp it to the viewport, override password/hidden/blinking cursors.
  RoyalTerminal retains combining/ZWJ clusters and supports an IME caret rather
  than copying the scalar-only overlay verbatim.
- Windows Terminal `src/tsf/Implementation.cpp` (`OnStartComposition`,
  `OnEndComposition`, `FlushPendingComposition`, `GetTextExt`): composition and
  final text are distinct, and terminal key ordering matters. RoyalTerminal uses
  Avalonia's platform IME rather than installing another TSF context.
- xterm.js `src/browser/input/CompositionHelper.ts`: preedit is an overlay,
  positioned at the cursor, and must not send each intermediate edit to the PTY.
- Avalonia `TextInputMethodClient`, Win32 `Imm32InputMethod`, native
  `AvnView.mm`/`KeyTransform.mm`, and FreeDesktop IBus/Fcitx adapters: use the
  framework's client contract. Empty IBus HidePreedit and null IMM/macOS preedit
  both end composition. Platform candidate coordinates remain logical Avalonia
  coordinates, avoiding double application of display scaling.
  Avalonia.Native gives modified key-down events to the control before its IME,
  so composing/dead keys remain unhandled on macOS. Unmodified printable keys
  can arrive only as TextInput: a synchronous, character-matched current NSEvent
  bridge preserves Kitty press/repeat/release and application-keypad encoding.
  It reads an existing NSApp only; no Cocoa application is created in headless
  hosts, and asynchronous commits/paste are not fabricated into key events.
- Ghostty `PageList.Limits` and snapshot decoder: page-size-aligned byte charge,
  one-page minimum history, viewport minimum bytes, full-page history admission,
  and no reopening gaps. Quotas are independent of parser resource bounds.

No PowerShell/PTY startup or shell behavior is changed by this follow-up.

## Validation

Focused xUnit/headless cases cover composition lifecycle, UTF-16/wide/combining
preedit geometry, rendering without snapshot mutation, layout consumption,
byte boundaries, source-limit round trips, COW/pruning/live growth, and native
allocation differentials. Execution results are recorded after implementation
is committed, per the requested implementation-first workflow.
