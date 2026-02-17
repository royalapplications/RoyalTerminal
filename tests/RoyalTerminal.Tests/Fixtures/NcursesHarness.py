#!/usr/bin/env python3
"""
Minimal ncurses harness for terminal integration tests.
Logs READY/KEY/MOUSE/RESIZE events to a file supplied via RT_HARNESS_LOG.
"""

import curses
import os
import time


def _log(path: str, line: str) -> None:
    with open(path, "a", encoding="utf-8") as f:
        f.write(line + "\n")
        f.flush()


def _draw(stdscr: "curses._CursesWindow", line_no: int, text: str) -> None:
    rows, cols = stdscr.getmaxyx()
    width = max(1, cols - 1)
    clipped = text[:width]
    padded = clipped + (" " * max(0, width - len(clipped)))
    stdscr.addstr(line_no, 0, padded)


def run(stdscr: "curses._CursesWindow", log_path: str) -> None:
    curses.noecho()
    curses.cbreak()
    stdscr.keypad(True)
    stdscr.timeout(50)

    # Hide cursor if terminal supports it.
    try:
        curses.curs_set(0)
    except curses.error:
        pass

    mouse_mask = curses.ALL_MOUSE_EVENTS | curses.REPORT_MOUSE_POSITION
    try:
        curses.mouseinterval(0)
        curses.mousemask(mouse_mask)
    except curses.error:
        pass

    rows, cols = stdscr.getmaxyx()
    stdscr.clear()
    _draw(stdscr, 0, "RT_NCURSES_HARNESS")
    _draw(stdscr, 1, "READY")
    _draw(stdscr, 2, f"SIZE {rows}x{cols}")
    _draw(stdscr, 3, "KEY none")
    _draw(stdscr, 4, "MOUSE none")
    stdscr.refresh()

    _log(log_path, f"READY {rows}x{cols}")
    _log(log_path, f"SIZE {rows}x{cols}")

    start = time.monotonic()
    timeout_seconds = float(os.environ.get("RT_HARNESS_TIMEOUT_SEC", "20"))

    while True:
        if time.monotonic() - start > timeout_seconds:
            _log(log_path, "EXIT timeout")
            break

        ch = stdscr.getch()
        if ch == -1:
            continue

        start = time.monotonic()

        if ch == curses.KEY_RESIZE:
            rows, cols = stdscr.getmaxyx()
            _draw(stdscr, 2, f"SIZE {rows}x{cols}")
            stdscr.refresh()
            _log(log_path, f"RESIZE {rows}x{cols}")
            continue

        if ch == curses.KEY_MOUSE:
            try:
                _id, x, y, _z, bstate = curses.getmouse()
                _draw(stdscr, 4, f"MOUSE {x},{y} state={bstate}")
                stdscr.refresh()
                _log(log_path, f"MOUSE x={x} y={y} bstate={bstate}")
            except curses.error:
                _log(log_path, "MOUSE parse-error")
            continue

        _draw(stdscr, 3, f"KEY code={ch}")
        stdscr.refresh()
        _log(log_path, f"KEY code={ch}")

        if ch in (ord("q"), ord("Q")):
            _log(log_path, "EXIT quit")
            break


def main() -> int:
    log_path = os.environ.get("RT_HARNESS_LOG")
    if not log_path:
        return 2

    os.makedirs(os.path.dirname(log_path) or ".", exist_ok=True)

    try:
        curses.wrapper(lambda stdscr: run(stdscr, log_path))
        return 0
    except Exception as ex:  # pragma: no cover
        _log(log_path, f"ERROR {type(ex).__name__}: {ex}")
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
