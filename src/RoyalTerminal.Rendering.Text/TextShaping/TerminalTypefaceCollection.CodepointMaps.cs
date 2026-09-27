// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Avalonia.Rendering;

/// <summary>An inclusive codepoint range mapped to a lazily discovered regular font family.</summary>
/// <param name="First">First codepoint, from zero through 0x1FFFFF.</param>
/// <param name="Last">Last codepoint, at least First and at most 0x1FFFFF.</param>
/// <param name="FamilyName">Exact family request; no style or presentation restriction is imposed.</param>
public readonly record struct TerminalTypefaceCodepointMapping(int First, int Last, string FamilyName);

public sealed partial class TerminalTypefaceCollection
{
    private readonly (int First, int Last)[] _codepointRanges = [];
    private readonly string[] _codepointFamilies = [];

    /// <summary>Whether this collection has codepoint-specific family requests.</summary>
    public bool HasCodepointMappings => _codepointRanges.Length != 0;

    /// <summary>
    /// Returns an immutable collection borrowing the same faces, with the supplied
    /// mappings replacing any previous mappings. Inputs are copied; the last
    /// matching range wins, including when that range's family cannot be found.
    /// Family discovery and its positive/negative cache belong to each resolver.
    /// </summary>
    public TerminalTypefaceCollection WithCodepointMappings(params TerminalTypefaceCodepointMapping[] mappings)
    {
        ArgumentNullException.ThrowIfNull(mappings);
        return new(this, mappings);
    }

    private TerminalTypefaceCollection(TerminalTypefaceCollection source, TerminalTypefaceCodepointMapping[] mappings)
    {
        _faces = source._faces;
        _codepointRanges = new (int, int)[mappings.Length];
        _codepointFamilies = new string[mappings.Length];
        for (int i = 0; i < mappings.Length; i++)
        {
            TerminalTypefaceCodepointMapping mapping = mappings[i];
            if ((uint)mapping.First > 0x1FFFFF || (uint)mapping.Last > 0x1FFFFF || mapping.Last < mapping.First)
                throw new ArgumentOutOfRangeException(nameof(mappings));
            ArgumentNullException.ThrowIfNull(mapping.FamilyName);
            _codepointRanges[i] = (mapping.First, mapping.Last);
            _codepointFamilies[i] = mapping.FamilyName.Trim();
        }
    }

    internal string? GetCodepointFamily(int codepoint)
    {
        // As in Ghostty, scan small packed ranges backwards; touch the larger
        // descriptors only on a match. Avoid enumerating fonts at configuration time.
        for (int i = _codepointRanges.Length - 1; i >= 0; i--)
            if (codepoint >= _codepointRanges[i].First && codepoint <= _codepointRanges[i].Last)
                return _codepointFamilies[i];
        return null;
    }
}
