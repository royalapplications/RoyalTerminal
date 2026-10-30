// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Multiplexing.Automation;

/// <summary>Platform-neutral normalization for explicit modifier/key sequences.</summary>
public static class MultiplexerKeySyntax
{
    /// <summary>Normalizes a sequence such as Control+B&gt;Shift+C into ctrl+b&gt;shift+c.</summary>
    public static string NormalizeSequence(string sequence, int maximumKeys = 8)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sequence);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumKeys, 1);
        if (sequence.Length > 1024) throw MultiplexerArgumentValidator.Invalid("A key sequence exceeds its text budget.");
        string[] keys = sequence.Split('>');
        if (keys.Length > maximumKeys) throw MultiplexerArgumentValidator.Invalid("A key sequence exceeds its chord length budget.");
        for (int index = 0; index < keys.Length; index++) keys[index] = NormalizeKey(keys[index]);
        return string.Join('>', keys);
    }

    /// <summary>Normalizes one stroke. Modifiers have fixed ctrl, alt, shift, meta order.</summary>
    public static string NormalizeKey(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (key.Length > 128 || key.Contains('>')) throw MultiplexerArgumentValidator.Invalid("Expected one bounded key stroke.");
        string[] tokens = key.Trim().ToLowerInvariant().Split('+');
        bool control = false, alt = false, shift = false, meta = false;
        string? main = null;
        foreach (string raw in tokens)
        {
            string token = raw.Trim();
            switch (token)
            {
                case "ctrl": case "control": Set(ref control); break;
                case "alt": case "option": Set(ref alt); break;
                case "shift": Set(ref shift); break;
                case "meta": case "super": case "cmd": case "command": case "win": Set(ref meta); break;
                default:
                    if (main is not null || token.Length is < 1 or > 64 || token.Any(char.IsWhiteSpace) || token.Contains('/'))
                        throw MultiplexerArgumentValidator.Invalid("A stroke requires one key and distinct recognized modifiers; use 'plus' for the plus key.");
                    main = token switch { "esc" => "escape", "return" => "enter", "spacebar" => "space", _ => token };
                    break;
            }
        }
        if (main is null) throw MultiplexerArgumentValidator.Invalid("A key stroke cannot consist only of modifiers.");
        return (control ? "ctrl+" : "") + (alt ? "alt+" : "") + (shift ? "shift+" : "") + (meta ? "meta+" : "") + main;
    }

    private static void Set(ref bool modifier)
    {
        if (modifier) throw MultiplexerArgumentValidator.Invalid("A key stroke repeats a modifier.");
        modifier = true;
    }
}
