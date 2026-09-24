# Managed search follow-up (September 24, 2026)

## Reference decision and scope

Ghostty `622b4eecd` (`src/terminal/search/sliding_window.zig`, `formatter.zig`)
searches unwrapped, trimmed plain text, folds **ASCII only**, permits overlapping
matches, and maps every encoded grapheme component back to its owning cell.
Its match is a range, not one independent result per physical row.
The upstream feature is [Ghostty #14097](https://github.com/ghostty-org/ghostty/pull/14097).

Windows Terminal's `TextBuffer::SearchText` uses ICU over a text-buffer adapter,
with optional Unicode case-insensitive and regex matching. xterm.js's
`SearchLineCache` joins wrapped rows and removes wide-character padding;
`SearchEngine` offers case-sensitive/regex options. RoyalTerminal follows
Ghostty's literal ASCII-folding behavior for its existing optionless search
contract, not ICU/JavaScript Unicode folding or regex behavior. No shell or
PowerShell invocation semantics change.

Implemented a managed streaming matcher with scratch space proportional to the
needle, not scrollback size. Keep search in the terminal layer and one logical
range per match through navigation and highlighting, including the native
adapter. Reuse needle preprocessing and coordinate storage across scans.

This batch does **not** port Ghostty's pinned-page incremental history cache,
background search thread, or active/history scheduling. Searches still scan the
current published managed buffer synchronously. Exact formatter/page-boundary
behavior for pathological blank-only/newline searches remains outside the
equivalence claim. The managed search table must remain partial, with these
specific remaining items stated rather than claiming the native algorithm was
transplanted.

## Delivered behavior

| Area | Implemented | Remaining boundary |
| --- | --- | --- |
| Managed terminal search capability | `BasicVtProcessor` implements `ITerminalSearchSource`; no UI row conversion on this path | Third-party processors without this optional capability retain the old fallback |
| Literal matching | ASCII case folding, overlapping results, KMP prefix fallback, reused needle/coordinate buffers | No regex, Unicode case folding or normalization |
| Text and coordinates | Soft wraps, interior spaces/tabs, trimmed trailing spaces, omitted wide spacers, UTF-16 grapheme-component mapping to owning cells | Exact native page-local formatter behavior for blank-only/newline edge cases is not asserted |
| Logical match ranges | `EndAbsoluteRow` extends the existing three-argument record; one match/navigation entry across rows in both adapters | Existing three-value deconstruction still describes start row/start column/end column; multi-row consumers must also read `EndAbsoluteRow` |
| Shared host | Selected/unselected highlights cover each visible row; history-limit clipping retained in the native adapter | No background search or match budget UI |
| Mutation lifecycle | Rescan after edits/reflow, alternate-screen switch, reset and history eviction; managed synchronized-output search reads published state | Incremental/pinned-page caching and active/history scheduling remain deferred |

## Validation and performance

At code commit `19e6eef`, the full local Release solution run passed **3,956
unit/headless + 240 required-native integration = 4,196 tests**, with **16
conditional unit skips and zero failures**. Both test projects write
`managed-search-release.trx`. CI flags and `ROYALTERMINAL_REQUIRE_NATIVE_TESTS=1`
were enabled. The focused search suite passed 38 cases before the final direct
ASCII-cell fast path; the full run includes that fast path and all focused cases.
Fresh cross-platform CI is tracked in the PR; local results do not imply that it
has completed. A separate full solution Release build passed with **zero warnings
and zero errors**. The native dependency/binary implementation is unchanged.

Focused coverage includes direct expected results, native comparisons (including
160 seeded history/viewport queries), mutation/lifecycle cases and headless Skia
pixels for selected/unselected wrapped matches with both engine adapters.
The allocation regression test performs ten warmed scans of 1,000 matching rows
with **zero managed allocations**, reusing destination capacity.

An isolated macOS arm64/.NET 10 Release profile compared the prior row-conversion
and ordinal `IndexOf` loop with the streaming matcher: 10,000 rows, 120 columns,
`prefix needle suffix repeated log message`, 100 repeated scans, preallocated
results. Stable paired samples were **1.572–1.605 ms/scan** for the old loop and
**1.887–1.894 ms/scan** for the new loop; both allocated **zero bytes** after warmup.
The new loop is about **18–20% slower in this narrow ASCII-only workload** while
adding case folding, overlaps and cross-row matching. A direct ASCII-cell path
reduced the initial streaming result of 2.166–2.178 ms/scan. This is a correctness
and bounded-storage implementation, **not a throughput improvement claim**;
incremental search remains a separate performance follow-up. These microbenchmark
numbers are not whole-application UI latency or a claim of native search speed.
