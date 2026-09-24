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

Implement a managed streaming matcher with scratch space proportional to the
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
