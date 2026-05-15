// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Terminal - Capture session validation helpers.

namespace RoyalTerminal.Terminal;

internal static class TerminalCaptureSessionValidator
{
    public static TerminalCaptureSession NormalizeAndValidate(TerminalCaptureSession session)
    {
        if (session.FormatVersion <= 0 || session.FormatVersion > TerminalCaptureSession.CurrentFormatVersion)
        {
            throw new InvalidDataException(
                $"Unsupported capture format version '{session.FormatVersion}'.");
        }

        List<TerminalCaptureEvent> sourceEvents = session.Events ?? [];
        List<OrderedCaptureEvent> normalizedEvents = new(sourceEvents.Count);
        bool requiresSorting = false;
        long previousOffset = 0;
        long durationMilliseconds = 0;
        for (int i = 0; i < sourceEvents.Count; i++)
        {
            TerminalCaptureEvent sourceEvent = sourceEvents[i];
            long offset = sourceEvent.OffsetMilliseconds < 0 ? 0 : sourceEvent.OffsetMilliseconds;
            TerminalCaptureEventKind kind = sourceEvent.Kind;
            if (offset > durationMilliseconds)
            {
                durationMilliseconds = offset;
            }

            if (i > 0 && offset < previousOffset)
            {
                requiresSorting = true;
            }
            previousOffset = offset;

            normalizedEvents.Add(new OrderedCaptureEvent(
                NormalizeEvent(sourceEvent, kind, offset, i),
                i));
        }

        if (requiresSorting)
        {
            normalizedEvents.Sort(static (left, right) =>
            {
                int offsetComparison = left.Event.OffsetMilliseconds.CompareTo(right.Event.OffsetMilliseconds);
                return offsetComparison != 0
                    ? offsetComparison
                    : left.OriginalIndex.CompareTo(right.OriginalIndex);
            });
        }

        List<TerminalCaptureEvent> orderedEvents = new(normalizedEvents.Count);
        for (int i = 0; i < normalizedEvents.Count; i++)
        {
            orderedEvents.Add(normalizedEvents[i].Event);
        }

        return session with
        {
            InitialColumns = Math.Max(1, session.InitialColumns),
            InitialRows = Math.Max(1, session.InitialRows),
            DurationMilliseconds = durationMilliseconds,
            Events = orderedEvents,
        };
    }

    private static TerminalCaptureEvent NormalizeEvent(
        TerminalCaptureEvent sourceEvent,
        TerminalCaptureEventKind kind,
        long offset,
        int index)
    {
        switch (kind)
        {
            case TerminalCaptureEventKind.Output:
            case TerminalCaptureEventKind.Input:
                if (sourceEvent.Data is null || sourceEvent.Data.Length == 0)
                {
                    throw new InvalidDataException(
                        $"Capture event at index {index} requires a non-empty data payload.");
                }

                return sourceEvent with
                {
                    OffsetMilliseconds = offset,
                    Data = sourceEvent.Data,
                    Columns = 0,
                    Rows = 0,
                    Label = null,
                    ExitCode = 0,
                };

            case TerminalCaptureEventKind.Resize:
                if (sourceEvent.Columns <= 0 || sourceEvent.Rows <= 0)
                {
                    throw new InvalidDataException(
                        $"Resize event at index {index} has invalid dimensions ({sourceEvent.Columns}x{sourceEvent.Rows}).");
                }

                return sourceEvent with
                {
                    OffsetMilliseconds = offset,
                    Data = null,
                    Columns = sourceEvent.Columns,
                    Rows = sourceEvent.Rows,
                    Label = null,
                    ExitCode = 0,
                };

            case TerminalCaptureEventKind.Marker:
                return sourceEvent with
                {
                    OffsetMilliseconds = offset,
                    Data = null,
                    Columns = 0,
                    Rows = 0,
                    Label = sourceEvent.Label ?? string.Empty,
                    ExitCode = 0,
                };

            case TerminalCaptureEventKind.Exit:
                return sourceEvent with
                {
                    OffsetMilliseconds = offset,
                    Data = null,
                    Columns = 0,
                    Rows = 0,
                    Label = null,
                };

            default:
                throw new InvalidDataException(
                    $"Capture event at index {index} uses unsupported kind '{kind}'.");
        }
    }

    private readonly record struct OrderedCaptureEvent(TerminalCaptureEvent Event, int OriginalIndex);
}
