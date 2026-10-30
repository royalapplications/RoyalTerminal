// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Globalization;

namespace RoyalTerminal.Multiplexing.Automation;

/// <summary>An externally ordered selectable item, such as a session, window, or block.</summary>
public sealed record MultiplexerTarget(string Id, string Label);

/// <summary>Resolves selectors without performing any mutations.</summary>
public static class MultiplexerTargetResolver
{
    /// <summary>Resolves exact identity, case-insensitive label, one-based index, then identity suffix.
    /// Ambiguity at an earlier stage is an error and never falls through to a later stage.</summary>
    public static MultiplexerTarget Resolve(string selector, IReadOnlyList<MultiplexerTarget> targets,
        int maxCandidates = 4096)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(selector);
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxCandidates, 1);
        if (selector.Length > 256 || targets.Count > maxCandidates)
            throw new MultiplexerAutomationException(MultiplexerAutomationError.InvalidConfiguration, "Selector or candidate budget exceeded.");
        string value = selector.Trim();
        MultiplexerTarget? result = Match(targets, target => string.Equals(target.Id, value, StringComparison.Ordinal));
        if (result is not null) return result;
        result = Match(targets, target => string.Equals(target.Label, value, StringComparison.OrdinalIgnoreCase));
        if (result is not null) return result;
        if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int index) && index > 0 && index <= targets.Count)
            return targets[index - 1];
        if (value.Length >= 4)
        {
            result = Match(targets, target => target.Id.EndsWith(value, StringComparison.OrdinalIgnoreCase));
            if (result is not null) return result;
        }
        throw new MultiplexerAutomationException(MultiplexerAutomationError.TargetNotFound, "No target matches this selector.");
    }

    private static MultiplexerTarget? Match(IReadOnlyList<MultiplexerTarget> targets, Func<MultiplexerTarget, bool> matches)
    {
        MultiplexerTarget? result = null;
        for (int i = 0; i < targets.Count; i++)
        {
            MultiplexerTarget target = targets[i] ?? throw new ArgumentException("Targets cannot contain null items.", nameof(targets));
            ArgumentException.ThrowIfNullOrWhiteSpace(target.Id);
            if (!matches(target)) continue;
            if (result is not null)
                throw new MultiplexerAutomationException(MultiplexerAutomationError.AmbiguousTarget,
                    "More than one target matches this selector; use an exact identity.");
            result = target;
        }
        return result;
    }
}
