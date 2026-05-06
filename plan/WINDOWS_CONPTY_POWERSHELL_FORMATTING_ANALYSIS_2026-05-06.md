# Windows ConPTY and PowerShell Formatting Analysis

Date: 2026-05-06

## Problem

Running the demo through the Windows PTY transport shows broken PowerShell and WSL
directory listings after resize/reflow. Rows that should be logical lines are
reflowed with PowerShell table padding, so colored trailing spaces become wide
background bands and later text appears on the wrong visual rows.

The current local change partially addressed this by trimming raw output bytes
before `\r`/`\n` in the Ghostty Windows sanitizer. That is not the right layer:
it mutates PTY output before VT parsing and can corrupt legitimate trailing
spaces produced by applications.

## Windows Terminal Reference

The comparison target was `C:\Users\wiesl\GitHub\terminal`.

Relevant files:

- `src/cascadia/TerminalConnection/ConptyConnection.cpp`
- `src/inc/til/env.h`
- `src/types/utils.cpp`
- `src/buffer/out/textBuffer.cpp`
- `src/buffer/out/ut_textbuffer/ReflowTests.cpp`
- `src/cascadia/TerminalSettingsModel/defaults.json`
- `src/cascadia/TerminalSettingsModel/PowershellCoreProfileGenerator.cpp`
- `src/cascadia/TerminalSettingsModel/WslDistroGenerator.cpp`

## Windows Terminal Behavior

### ConPTY launch

Windows Terminal launches ConPTY sessions with `STARTUPINFOEX` and
`PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE`. It sets `STARTF_USESTDHANDLES` in its
internal ConPTY bridge, expands environment strings in the command line, and
calls `CreateProcessW` with `lpApplicationName = nullptr` so Windows parses the
mutable command line the same way it does for normal terminal launches.

It uses `EXTENDED_STARTUPINFO_PRESENT | CREATE_UNICODE_ENVIRONMENT`.

### Pipe handling

Windows Terminal creates one overlapped duplex pipe with a 128 KiB buffer and
passes the same ConPTY client handle for input and output. Its output loop reads
128 KiB chunks, converts UTF-8 to UTF-16 with a persistent decoder state, and
raises terminal output unchanged.

RoyalTerminal uses the public ConPTY API with separate synchronous pipes. That
is valid for public ConPTY and does not explain the formatting bug. In this
path, setting `STARTF_USESTDHANDLES` with empty standard handles caused
short-lived `cmd.exe /c` sessions to stop reporting exit, so the public-API
implementation keeps the pseudo-console attribute but does not carry that
internal Windows Terminal flag. Increasing read/write chunks to 128 KiB reduces
fragmentation and better matches Windows Terminal behavior.

### Environment

Windows Terminal does not set `TERM`, `COLORTERM`, or `TERM_PROGRAM` for Windows
ConPTY sessions.

It does set:

- `WT_SESSION`: a per-connection GUID without braces.
- `WT_PROFILE_ID`: the profile GUID with braces.
- `WSLENV`: prepended with `WT_SESSION` and `WT_PROFILE_ID` so those values flow
  into WSL.

It also prepends custom environment variable names to `WSLENV`, while never
adding `PATH` because that would override WSL's Linux-side path computation.

The local RoyalTerminal change that forced `TERM=xterm-256color`,
`COLORTERM=truecolor`, and `TERM_PROGRAM=RoyalTerminal` is therefore not
Windows Terminal-compatible and should be removed from Windows ConPTY.

### WSL starting directory

Windows Terminal promotes a WSL starting directory into the command line as
`wsl --cd "<directory>"` and passes no Windows current directory in that case.
It only applies this to `wsl`/`wsl.exe` from System32 or an unqualified `wsl`.
It also avoids adding `--cd` if the command already contains it or uses bare
`~`, and maps `//wsl$` / `//wsl.localhost` prefixes back to backslash form.

RoyalTerminal currently only passes `lpCurrentDirectory`, which cannot express
Linux-side WSL paths.

### PowerShell handling

Windows Terminal does not special-case PowerShell output in the ConPTY bridge.
PowerShell profile detection is only profile generation:

- default Windows PowerShell is `%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe`
- PowerShell Core profiles prefer installed `pwsh.exe` instances

No output sanitizer exists for PowerShell table formatting.

### Reflow handling

Windows Terminal fixes the formatting symptom in the text buffer. In
`TextBuffer::Reflow`, rows do not store original input length, so it calls
`MeasureRight()` and explicitly truncates trailing whitespace before wrapping
logical lines into the new width. Its own tests include padded rows that keep
internal spaces but drop non-logical trailing padding.

That means RoyalTerminal should trim trailing whitespace during buffer reflow,
not in raw PTY output.

## RoyalTerminal Findings

1. `WindowsPty` injects `TERM`, `COLORTERM`, and `TERM_PROGRAM`. This diverges
   from Windows Terminal and can change application behavior under PowerShell,
   cmd, and WSL.

2. `WindowsPty` does not add `WT_SESSION`, `WT_PROFILE_ID`, or `WSLENV` entries.
   WSL sessions miss the terminal identity variables Windows Terminal provides.

3. `WindowsPty` does not expand command-line environment variables before
   `CreateProcessW`. Profiles using `%SystemRoot%` can fail or behave
   differently from Windows Terminal.

4. `WindowsPty` does not promote WSL starting directories to `wsl --cd`.

5. The Ghostty Windows sanitizer trims raw line-ending padding. This is not how
   Windows Terminal's ConPTY bridge works. However, native Ghostty owns its
   resize reflow before RoyalTerminal can apply managed `TerminalScreen` reflow,
   so the native Ghostty path still needs a constrained compatibility
   normalizer until the native library exposes a Windows Terminal-style reflow
   hook.

6. The managed `TerminalScreen` reflow change that ignores trailing spaces and
   style-only cells is correct. It matches Windows Terminal's reflow model.

7. Native Ghostty resize still needs protection because native Ghostty performs
   its own reflow before RoyalTerminal mirrors the grid into `TerminalScreen`.
   The managed path handles this at reflow time. The native path uses a
   Windows-only compatibility normalizer for styled trailing line padding, plus
   a post-resize mirror cleanup for rows that can still be trimmed safely.

## Implementation Plan

1. Replace Windows PTY environment construction with a Windows Terminal-style
   helper:
   - copy the current environment into a case-insensitive sorted block
   - add `WT_SESSION`
   - add `WT_PROFILE_ID`
   - prepend `WT_SESSION`, `WT_PROFILE_ID`, and non-`PATH` user env names to
     `WSLENV`
   - apply user environment overrides with environment-string expansion
   - do not inject `TERM`, `COLORTERM`, or `TERM_PROGRAM`

2. Update `WindowsPty` launch behavior:
   - expand environment strings in the command line
   - use `lpApplicationName = null`
   - increase read/write chunks to 128 KiB
   - apply a Windows Terminal-style WSL `--cd` mangle when launching `wsl.exe`

3. Keep Ghostty's unsupported Windows sequence stripping and constrain its
   trailing-padding normalizer to styled Windows native Ghostty line endings,
   where it compensates for the missing Windows Terminal-style native reflow
   hook without altering plain intentional trailing spaces.

4. Keep the managed `TerminalScreen` reflow trimming.

5. Add native Ghostty post-resize screen cleanup that clears trailing whitespace
   on non-wrapped rows during the next resize sync, matching Windows Terminal
   reflow semantics without altering raw PTY data.

6. Update tests:
   - replace the `TERM`/`COLORTERM` PTY contract test with
     `WT_SESSION`/`WT_PROFILE_ID`/`WSLENV` coverage
   - add focused unit coverage for the Windows Terminal environment helper
   - remove raw sanitizer trimming tests
   - keep managed and Ghostty resize trimming tests, now covered at the buffer
     and post-resize screen-sync layers

## Performance Review Addendum

The follow-up performance pass keeps the Windows Terminal-inspired 128 KiB pipe
read/write size but avoids translating that directly into repeated large managed
allocations. In .NET, byte arrays at and above the large-object-heap threshold
create avoidable GC pressure during terminal floods and large paste operations.

Applied optimizations:

- `WindowsPty` rents the 128 KiB read buffer from `ArrayPool<byte>` and returns
  it when the read loop exits.
- `WindowsPty` emits stable `DataReceived` snapshots in 64 KiB chunks so normal
  high-throughput output avoids per-event large-object-heap allocations.
- `WindowsPty` snapshots large writes into 64 KiB queued chunks. This preserves
  stream correctness, avoids large-object-heap paste buffers, and lets priority
  control writes be serviced between large queued chunks.
- Snapshot buffers use `GC.AllocateUninitializedArray<byte>` before copying
  known bytes into every position, avoiding unnecessary zero initialization.
- `WindowsPtyEnvironment` parses `WSLENV` with spans instead of `string.Split`
  and pre-sizes the environment block `StringBuilder`.
- `TerminalUnsupportedWindowsSequenceSanitizer` now pre-scans for actual target
  unsupported sequences or styled trailing line padding before renting/copying
  buffers. Ordinary SGR-colored output with no target sequence stays on the
  no-allocation fast path.

Correctness constraints preserved:

- PTY output still arrives as stable byte arrays for downstream consumers.
- Chunk boundaries remain semantically irrelevant because PTY input and output
  are byte streams and the VT parser already handles split escape sequences.
- Plain trailing spaces before line breaks remain untouched by the sanitizer.
- Windows Terminal-style reflow trimming remains at the buffer/screen layer.

## PowerShell and Delayed-Wrap Addendum

PowerShell's `Get-ChildItem` table output is not plain text. The current
PowerShell formatter builds the default child-item table with wrapped rows and a
decorated `NameString` column. `NameString` applies `PSStyle` around the file
name and immediately resets after the name; `TableWriter` then pads table
fields and appends a reset when a decorated field does not already end in one.
This means colored names and padding are expected from PowerShell, and the
terminal must keep VT cursor and row-wrap state correct rather than trying to
sanitize `ls` output.

The remaining resize corruption came from a separate terminal-state bug:
RoyalTerminal's managed VT processor represented delayed end-of-line wrap by
letting the cursor column advance to `Columns`. Windows Terminal, Ghostty, and
xterm-style behavior keep the cursor on the last visible column and store a
separate pending-wrap/last-column flag. That distinction matters when
PowerShell redraws or clears rows during resize:

- Windows Terminal stores delayed EOL wrap separately on the cursor, consumes it
  before the next printable write, and resets it for erase and cursor movement
  controls.
- Ghostty calls this cursor state `pending_wrap`, saves/restores it with the
  cursor, consumes it before printing, and explicitly tests that erase-line
  clears pending wrap.
- xterm.js keeps row wrapping as buffer metadata and clears wrapped-row metadata
  for clear-from-start erase-line redraws.
- PowerShell emits many `CSI K` clear-to-end controls during redraws. If the
  cursor is modeled as one column beyond the row, clear-to-end becomes a no-op.
  If `WrapsToNext` remains set after an erase-to-right redraw, later resize
  reflow concatenates rows that PowerShell has already logically broken.

The managed VT fix therefore follows the reference model:

- add an explicit delayed-wrap flag instead of using `CursorCol == Columns` as a
  sentinel
- keep the cursor clamped to the last visible cell while delayed wrap is pending
- consume delayed wrap only before the next printable cell
- save/restore delayed wrap with cursor state and alternate-screen state
- reset delayed wrap for cursor movement, erase, insert/delete, scroll, tab,
  margin, reset, and line movement controls
- map pending logical-line-end cursor positions through resize/reflow as
  end-column anchors
- clear row `WrapsToNext` when `CSI K` erases from the cursor to the end of the
  line so stale soft-wrap metadata cannot poison subsequent reflow

## Windows PTY Resize Ownership Addendum

The next failure showed the same PowerShell table corruption in both managed VT
and native Ghostty VT after a narrow resize followed by widening. The common
source was not the managed delayed-wrap state alone. RoyalTerminal still allowed
the local VT processor to reflow its primary screen on modern Windows builds,
then resized the Windows PTY. ConPTY also owns a console text buffer and performs
its own resize/reflow before sending the post-resize state through the VT pipe.
Applying local reflow first leaves RoyalTerminal with a terminal model that no
longer matches ConPTY's cursor-addressed redraw stream.

Reference check:

- Windows Terminal calls `Terminal::UserResize` before `ConptyConnection::Resize`,
  but its local reflow includes ConPTY-specific viewport anchoring logic that
  keeps the mutable viewport synchronized with ConPTY's buffer behavior.
- Console host's `SCREEN_INFORMATION::ResizeScreenBuffer` performs
  `ResizeWithReflow` when wrap text is enabled and marks the ConPTY cursor
  position as possibly wrong until the frontend synchronizes it.
- Ghostty's libghostty-vt API documents that primary-screen resize reflows when
  wraparound mode is enabled; this must be suppressed for Windows PTY when
  ConPTY is the authoritative reflow owner.
- xterm.js keeps reflow behavior behind buffer metadata and exposes Windows PTY
  options because Windows PTY backends can own native wrapping/reflow behavior.
- PowerShell's `Get-ChildItem` table rows are explicitly `wrap: true` and emit
  styled `NameString` fields, so the terminal must interoperate with ConPTY and
  PowerShell's formatted output rather than special-casing `ls`.

Decision:

- Windows PTY sessions always use backend-owned resize reflow in RoyalTerminal.
- The managed VT path resizes its local `TerminalScreen` without local reflow for
  Windows PTY; ConPTY output is expected to repaint the authoritative content.
- The native Ghostty path exposes a resize reflow policy sink. When the active
  transport is Windows PTY, RoyalTerminal temporarily disables Ghostty
  wraparound mode around `ghostty_terminal_resize`, preventing Ghostty from
  locally reflowing before ConPTY sends its resized buffer state.
- Non-Windows-PTY transports keep the existing local reflow behavior.

## Windows PTY Local Reflow Correction

The backend-owned resize experiment did not match the observed PowerShell
failure. After shrinking to roughly the first `Name` column cell and widening
again, rows such as `.android` came back as only `.`, while later rows were
missing or joined in VT snapshot export. That is the signature of RoyalTerminal
discarding hidden cells globally after the shrink and then depending on ConPTY
to repaint every hidden cell on widen. ConPTY does not guarantee that complete
historical table rows are repainted after every width change.

Reference re-check:

- Windows Terminal's `Terminal::UserResize` still reflows the local main buffer
  with `TextBuffer::Reflow`, then applies ConPTY-specific viewport anchoring so
  lines that enter scrollback remain in scrollback.
- PowerShell's `ConsoleLineOutput` uses `WriteLine` in the console host path and
  subtracts one buffer column when forcing newlines, so formatted table rows are
  intentionally hard-line output and should survive width round-trips in the
  terminal model.
- xterm.js documents Windows PTY compatibility because older Windows PTY
  backends do not expose enough wrap metadata; that is a web-frontend fallback,
  not a reason for a native frontend to throw away preserved row cells.
- Ghostty's primary screen resize reflow is still the right primitive for the
  native VT path when the user has resize reflow enabled.

Corrected decision:

- Keep local resize reflow enabled for Windows PTY when `ReflowOnResize` is on.
- Do not globally discard cells preserved outside the temporary narrow width.
  Those cells are needed when ConPTY does not repaint the old right side on
  widening.
- Continue clearing preserved hidden cells only on row mutation paths. If
  ConPTY actually rewrites or erases a row while the terminal is narrow, that row
  drops its stale hidden tail; untouched rows keep enough information to reflow
  back correctly.

## Managed VT Line Feed Wrap Metadata Correction

The demo app was instrumented with `ROYALTERMINAL_RESIZE_TRACE` and driven
through the real repro path: start the demo, activate the managed VT tab, run
`ls`, shrink from a wide window to a narrow PowerShell table, then widen again.
The trace showed that the key path was not transport sizing. The managed screen
correctly reflowed long `ls` rows into wrapped segments on shrink and reflowed
them back on widen. The remaining corruption came from stale `WrapsToNext`
metadata on rows that ConPTY/PowerShell later advanced past with explicit line
feeds.

Reference check:

- Windows Terminal's `AdaptDispatch::_DoLineFeed` sets the current row's
  `wrapForced` state from the caller. Explicit LF/IND/NEL passes `false`, while
  forced auto-wrap passes `true`.
- PowerShell table output emits explicit line movement for hard rows and relies
  on the host buffer's wrap metadata to distinguish a soft-wrapped row from a
  completed row.
- xterm.js and Ghostty both model soft-wrap metadata separately from row text;
  row text alone is not enough for resize reflow or unwrapped snapshot export.

Decision:

- Managed VT line feed now takes an explicit `wrapForced` flag.
- Autowrap paths call `LineFeed(wrapForced: true)`.
- LF, IND, and NEL call `LineFeed(wrapForced: false)`, clearing any stale
  soft-wrap metadata on the row being completed.
- Focused regression coverage verifies that after resize reflow, an explicit
  line feed clears a stale wrap flag so snapshot export does not join unrelated
  rows.

## Windows PTY Row-Growth Viewport Correction

The later repro narrowed the failure further: after several resizes, an old
PowerShell `ls` block in scrollback lost a contiguous middle section, while a
new `ls` command printed immediately after the resize was complete and correct.
That ruled out PowerShell table generation and pointed at viewport ownership
during height changes.

Reference check:

- xterm.js documents and implements a Windows PTY compatibility rule for row
  growth: when rows are added, it appends the new rows instead of pulling
  scrollback rows back into the live viewport, because ConPTY repaints the
  active screen with its own view after resize. Without that, historical rows
  can be pulled into the repaint target and replaced.
- Windows Terminal's ConPTY path has equivalent viewport anchoring inside its
  local resize/reflow path before it sends the resize to the ConPTY connection.
- PowerShell's second `ls` output being correct confirms the application output
  is not the corrupting source; the damage is caused by old scrollback rows
  becoming part of the mutable viewport during row-growth resize cycles.

Decision:

- Keep local width reflow enabled so preserved cells survive narrow/wide
  round-trips.
- For active Windows PTY sessions only, row growth appends blank rows at the
  bottom instead of pulling scrollback into the live viewport.
- The cursor remains at its pre-growth row until ConPTY or subsequent shell
  output consumes the newly appended rows. This matches xterm.js's Windows PTY
  behavior and protects historical scrollback rows from ConPTY's repaint stream.
- Non-Windows PTY sessions and inactive/offline managed screen resizing keep the
  existing bottom-anchored behavior.

## Fresh Resize-Ordering Correction

The remaining repro still lost a contiguous middle section of the first
PowerShell `ls` block after repeated shrink/grow cycles, while a second `ls`
printed at the final size was correct. That is a different failure from
PowerShell formatting. It means the shell can format the table correctly at the
final width, but resize traffic can still damage already-rendered rows.

Fresh reference check:

- Windows Terminal resizes its local terminal buffer before calling
  `ConptyConnection::Resize`, but both sides are integrated in one terminal
  core and the local buffer has ConPTY-specific viewport anchoring.
- Windows Terminal also debounces expensive output-idle/search work; resize is
  not generally treated as a place to repaint historical content repeatedly.
- PowerShell `ConsoleLineOutput` reads console width and writes formatted table
  rows to the host; the correct second `ls` confirms PowerShell is not emitting
  permanently corrupt data at the final width.
- xterm.js PR 5321 tried to follow ConPTY-specific reflow more closely because
  modern ConPTY passes more sequences through and requires the frontend buffer
  to stay synchronized. PR 5358 later reverted that approach because broad
  ConPTY-specific reflow changes could corrupt the buffer.
- Ghostty still treats primary-screen resize/reflow as frontend terminal state;
  it does not provide a safe way for RoyalTerminal to tag stale intermediate
  ConPTY repaint fragments.

Decision:

- Keep local screen resize/reflow immediate so the UI tracks the user drag and
  preserved hidden cells survive narrow/wide width round-trips.
- Coalesce Windows PTY/ConPTY transport resize calls on a short trailing timer.
  A drag from 133 columns to 51 and back no longer asks ConPTY and PowerShell to
  repaint each intermediate width into a frontend buffer that has already moved
  on to a later width.
- Flush the pending final transport resize before any keyboard, paste, focus, or
  mouse input so commands typed immediately after resizing observe the current
  terminal dimensions.
- Keep non-Windows-PTY transports on the old immediate resize behavior because
  the stale intermediate ConPTY repaint stream is the Windows-specific source.

Tests added/updated:

- A real PowerShell fixture creates deterministic `Get-ChildItem` rows, runs the
  rapid shrink/grow sequence, and asserts all expected table rows remain after
  resize.
- A fake Windows PTY transport test verifies rapid resize updates are
  coalesced and the final resize is flushed before input.
- Existing managed/native Windows PTY resize tests now assert that intermediate
  transport widths are not sent while the local screen still reflows
  immediately.

## Hidden-Column Row-Shift Correction

The latest remaining artifact was no longer a broad table corruption problem.
The snapshot showed specific hidden tails such as `usic` and repeated trailing
blocks being reintroduced after the table had otherwise survived the resize
cycle. This points at row movement, not PowerShell formatting: a row narrowed
from `Music` can retain hidden cells outside the active width; if a later
scroll, insert-line, or delete-line operation copies that full retained row
state into another logical row, widening can expose stale tail fragments in the
wrong place.

Reference check:

- Windows Terminal's `ROW::CopyFrom` copies the readable/current columns and
  then normalizes trailing attribute extents to the destination row width. It
  does not propagate frontend-only hidden columns when rows are shifted.
- xterm.js reflow operates on active buffer lines and explicitly clears
  remaining cells after layout changes so old fragments do not remain visible.
- Ghostty's terminal model keeps scrollback/page state in the terminal core and
  has resize/text reflow as terminal state, while resize events are coalesced on
  its I/O thread. There is no equivalent concept of copying stale hidden
  frontend columns during VT row movement.
- PowerShell's formatting layer writes concrete table rows based on the current
  host width. The repeated `Music`/`OneDrive` tail blocks are not new
  PowerShell output; they are old cells reappearing during terminal row moves.

Decision:

- Keep hidden-column retention for resize reflow, because it is required to
  reconstruct rows after narrowing and widening.
- Do not copy retained hidden columns during managed VT row-shift operations
  (`scroll up/down`, `insert lines`, `delete lines`). Those operations represent
  logical screen row movement at the current terminal width, so only active
  cells should move.
- Keep full `TerminalRow.CopyFrom` available for snapshot/mirror-style copies
  where preserving the entire row state is intentional.

Tests added/updated:

- The no-reflow row-shift test now asserts that hidden tails are dropped when a
  row is shifted at the narrow width.
- The PowerShell table resize test now runs multiple shrink/grow cycles and
  asserts each expected directory/file row appears exactly once, with no bare
  `usic`/`hotos` fragments.
- The Windows PTY managed control test now checks the resized PowerShell
  snapshot for duplicate listing rows and tail fragments.

## Transient Resize Padding Correction

The latest repro still showed repeated trailing blocks after several resize
cycles. The key difference from the earlier hidden-column issue is that the
duplicated rows were valid tail rows (`Music`, `OneDrive`, `Pictures`, etc.)
reappearing multiple times, not just exposed hidden suffixes. That means the
Windows PTY row-growth padding itself was being promoted into durable
scrollback after ConPTY repaint traffic wrote into it.

Fresh reference check:

- Windows Terminal keeps ConPTY resize handling inside the terminal core:
  `Terminal::UserResize` adjusts the local buffer and viewport before the
  connection resize, and row copies operate on current row data rather than
  frontend-only padding.
- xterm.js has explicit Windows PTY/reflow history: recent release notes call
  out both a ConPTY-like resize reflow attempt and a later revert, which
  reinforces that ConPTY repaint compatibility must be narrowly scoped.
- Ghostty/libghostty treats resize, text reflow, scrollback, and render state as
  terminal-core state. Embedders consume the synchronized screen state instead
  of preserving temporary frontend rows as history.
- PowerShell writes table output through its console host/RawUI width; the
  repeated tail rows are not new PowerShell output. The correct second `ls`
  after resize confirms this is frontend buffer lifecycle damage.

Decision:

- Rows appended only to preserve the Windows PTY live viewport are now marked as
  transient resize rows.
- Blank transient resize rows are discarded before the next
  Windows-PTY-style resize. They are no longer discarded at the start of a VT
  packet, because that deletes the viewport padding just before ConPTY sends the
  resize repaint that depends on it.
- Rows touched by VT repaint output are promoted from transient padding to real
  terminal content. The fallback discard path also refuses to remove transient
  rows that now contain visible cells, so a missed mutation mark cannot drop
  valid repaint rows.
- Managed row-shift operations continue to copy only active cells, preserving
  the previous hidden-column fix.
- Ghostty VT processing follows the same lifecycle: resize padding survives
  until native screen sync or VT mutation overwrites it, instead of being
  cleared before the backend repaint packet.

Tests added/updated:

- `BasicVtProcessor_ProcessAfterWindowsPtyResize_ActivatesRowsTouchedByRepaint`
  asserts that a row written by the resize repaint stops being transient and is
  not removed by later transient cleanup.
- `TerminalScreen_ResizeRowsIncrease_DropsBlankTransientPaddingBeforeNextWindowsPtyResize`
  asserts that only untouched blank padding is dropped on the next resize.
- The real Windows PowerShell PTY contract test now exercises
  `preserveViewportTopOnRowsIncrease: true`, matching the demo app path.
- Existing resize tests continue to assert that expected PowerShell listing
  rows appear once and no bare `usic`/`hotos` fragments remain.
