// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal.Snapshots;

namespace RoyalTerminal.Terminal;

public sealed partial class BasicVtProcessor : ITerminalPromptStateSource
{
    private SemanticPen _primarySemanticPen;
    private SemanticPen _alternateSemanticPen;
    private ref SemanticPen CurrentSemanticPen => ref (_inAltScreen ? ref _alternateSemanticPen : ref _primarySemanticPen);
    private PromptPolicy _primaryPromptPolicy;
    private PromptPolicy _alternatePromptPolicy;
    // The embedding VT API disables redraw at construction; RIS restores the
    // terminal's full-app default. An explicit OSC option overrides either.
    private TerminalPromptRedraw _promptRedraw = TerminalPromptRedraw.None;
    private ref PromptPolicy CurrentPromptPolicy => ref (_inAltScreen ? ref _alternatePromptPolicy : ref _primaryPromptPolicy);

    /// <inheritdoc />
    public TerminalPromptState PromptState => new(CurrentPromptPolicy.Seen, CurrentSemanticPen.Content,
        CurrentSemanticPen.ClearAtEndOfLine, CurrentPromptPolicy.Click, _promptRedraw);

    private struct PromptPolicy
    {
        public bool Seen;
        public TerminalPromptClick Click;
    }

    // Unlike DECSC state, semantic content belongs to the live cursor. Legacy
    // screen switches copy it; 1049 restore leaves the primary cursor's value.
    private struct SemanticPen
    {
        public TerminalSemanticContent Content;
        public bool ClearAtEndOfLine;
    }

    private void HandleSemanticPrompt(ReadOnlySpan<char> command)
    {
        if (command.IsEmpty || (command.Length > 1 && command[1] != ';')) return;
        char action = command[0];
        if (action == 'L' && command.Length != 1) return;

        switch (action)
        {
            case 'L':
                SemanticFreshLine();
                break;
            case 'A':
            case 'N':
                SemanticFreshLine();
                ReadPromptPolicy(command.Length > 1 ? command[2..] : default);
                goto case 'P';
            case 'P':
                CurrentPromptPolicy.Seen = true;
                CurrentSemanticPen = new() { Content = TerminalSemanticContent.Prompt };
                ReadOnlySpan<char> kind = ReadFirstSemanticOption(command.Length > 1 ? command[2..] : default, "k");
                SetCurrentRowSemanticPrompt(kind is "c" or "s"
                    ? TerminalSemanticPrompt.PromptContinuation : TerminalSemanticPrompt.Prompt);
                break;
            case 'B':
            case 'I':
                CurrentSemanticPen = new() { Content = TerminalSemanticContent.Input, ClearAtEndOfLine = action == 'I' };
                break;
            case 'C':
                CurrentSemanticPen = default;
                // Fish reports output after a newline that we tentatively
                // marked as a continuation. Only unmark at column zero.
                if (_cursorCol == 0) SetCurrentRowSemanticPrompt(TerminalSemanticPrompt.None);
                break;
            case 'D':
                CurrentSemanticPen = default;
                break;
        }
    }

    private void ReadPromptPolicy(ReadOnlySpan<char> options)
    {
        _promptRedraw = ReadFirstSemanticOption(options, "redraw") switch
        {
            "0" => TerminalPromptRedraw.None,
            "1" => TerminalPromptRedraw.All,
            "last" => TerminalPromptRedraw.Last,
            _ => _promptRedraw,
        };
        TerminalPromptClick click = ReadFirstSemanticOption(options, "click_events") switch
        {
            "1" => TerminalPromptClick.Absolute,
            "2" => TerminalPromptClick.Relative,
            _ => TerminalPromptClick.None,
        };
        if (click == TerminalPromptClick.None)
            click = ReadFirstSemanticOption(options, "cl") switch
            {
                "line" => TerminalPromptClick.Line,
                "m" => TerminalPromptClick.Multiple,
                "v" => TerminalPromptClick.ConservativeVertical,
                "w" => TerminalPromptClick.SmartVertical,
                _ => CurrentPromptPolicy.Click,
            };
        CurrentPromptPolicy.Click = click;
    }

    private void ClearPromptForRedraw()
    {
        if (_inAltScreen || _promptRedraw == TerminalPromptRedraw.None ||
            CurrentSemanticPen.Content == TerminalSemanticContent.Output) return;
        int first = _screen.GetAbsoluteRowForViewportRow(_cursorRow);
        int last = first;
        if (_promptRedraw == TerminalPromptRedraw.All)
        {
            // Find the preceding initial prompt, including history. Clearing
            // does not remove rows or reset their prompt/wrap classifications.
            while (first >= 0 && _screen.GetRow(first).SemanticPrompt != TerminalSemanticPrompt.Prompt) first--;
            if (first < 0) return;
            last = _screen.TotalRows - 1;
        }
        for (int index = first; index <= last; index++)
        {
            TerminalRow row = _screen.GetRow(index);
            ClearPreservedCellsForMutation(row);
            // Native resize temporarily detaches the cursor pen before this
            // clear, so prompt blanks use default colors, not the active SGR.
            using GhosttySnapshotStyleTracker.RowEdit styles = _screen.EditSnapshotRowStyles(row);
            styles.Clear(0, row.Columns);
            row.Cells.Fill(TerminalCell.Empty(_screen.DefaultForeground, _screen.DefaultBackground));
            row.IsDirty = true;
        }
    }

    private void SemanticFreshLine()
    {
        int left = _cursorCol < _scrollLeft ? 0 : _scrollLeft;
        if (_cursorCol == left) return;
        _cursorCol = left;
        ResetDelayedWrap();
        LineFeed(wrapForced: false);
    }

    private void SetCurrentRowSemanticPrompt(TerminalSemanticPrompt value)
    {
        TerminalRow row = _screen.GetViewportRow(_cursorRow);
        if (row.SemanticPrompt == value) return;
        row.SemanticPrompt = value;
        row.IsDirty = true;
    }

    private void AdvanceSemanticLine(bool softWrap)
    {
        SemanticPen pen = CurrentSemanticPen;
        if (pen.Content == TerminalSemanticContent.Output) return;
        if (pen.ClearAtEndOfLine)
        {
            if (!softWrap) CurrentSemanticPen = default;
        }
        else
        {
            SetCurrentRowSemanticPrompt(TerminalSemanticPrompt.PromptContinuation);
        }
    }

    private static ReadOnlySpan<char> ReadFirstSemanticOption(ReadOnlySpan<char> options, ReadOnlySpan<char> key)
    {
        while (!options.IsEmpty)
        {
            int separator = options.IndexOf(';');
            ReadOnlySpan<char> entry = separator < 0 ? options : options[..separator];
            int equals = entry.IndexOf('=');
            if (equals >= 0 && entry[..equals].SequenceEqual(key)) return entry[(equals + 1)..];
            if (separator < 0) break;
            options = options[(separator + 1)..];
        }
        return default;
    }
}
