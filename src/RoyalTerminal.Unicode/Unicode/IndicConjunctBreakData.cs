// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Unicode;

/// <summary>Unicode Indic_Conjunct_Break property values for grapheme rule GB9c.</summary>
public enum IndicConjunctBreakClass
{
    /// <summary>No Indic conjunct role.</summary>
    None,
    /// <summary>Connects a preceding consonant to a following consonant.</summary>
    Linker,
    /// <summary>Consonant that may participate in an Indic conjunct.</summary>
    Consonant,
    /// <summary>Extends an Indic conjunct without terminating its linking state.</summary>
    Extend,
}

internal static class IndicConjunctBreakData
{
    public static IndicConjunctBreakClass Get(Codepoint codepoint)
        => codepoint.IndicConjunctBreakClass;
}
