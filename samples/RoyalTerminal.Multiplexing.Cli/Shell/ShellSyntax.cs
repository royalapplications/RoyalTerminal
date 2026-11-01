// Copyright (c) Royal Apps. Licensed under the MIT license.
using System.Globalization;

namespace RoyalTerminal.Multiplexing.Cli;

/// <summary>Flag-oriented shell interface; the existing positional interface keeps its output and behavior.</summary>
internal sealed record ShellSyntax(string Verb, IReadOnlyDictionary<string,string> Options, string[] Words, string[] Tail)
{
    internal const string Usage = "new [LABEL] --json [--cwd DIR] [-- COMMAND ARG…] | run -s SESSION [--label TEXT] [--wait] [-- COMMAND ARG…] | split [-s SESSION] [-w WINDOW] [-b BLOCK] [--split right|left|above|below] [--ratio PERCENT] [--wait] [-- COMMAND ARG…] | capture [-s SESSION] [-w WINDOW] [-b BLOCK] [--format text|html|vt] [--trim] [--unwrap] [--json] | send [-s SESSION] [-w WINDOW] [-b BLOCK] [TEXT] | send-key [-s SESSION] [-b BLOCK] KEY… | wait [-s SESSION] [-w WINDOW] [-b BLOCK] [--for 10s]";
    internal string? Get(string k) => Options.GetValueOrDefault(k);
    internal bool Has(string k) => Options.ContainsKey(k);
    internal bool Launch => Verb is "new" or "run" or "split";
    internal static bool Handles(string verb, string[] a)
    {
        if (verb == "split") return true;
        if (verb is not ("new" or "run" or "send" or "send-key" or "capture" or "wait")) return false;
        if (verb == "new") return a.Length > 1 || a.FirstOrDefault()?.StartsWith('-') == true;
        if (a.Length == 0) return true;
        if (verb == "run" && a.Length >= 2 && a[0] == "--wait" && !a[1].StartsWith('-')) return false;
        if (a[0].StartsWith('-')) return true;
        // A positional legacy command is never reinterpreted because its child argv contains flags.
        return verb switch { "capture" or "wait" => a.Length == 1, "send" => a.Length == 1, "send-key" => a.Length < 3, _ => false };
    }

    internal static ShellSyntax Parse(string verb, string[] args)
    {
        var options = new Dictionary<string,string>(StringComparer.Ordinal);
        List<string> words = []; string[] tail = [];
        bool launch = verb is "new" or "run" or "split";
        for (int i = 0; i < args.Length; ++i)
        {
            string raw = args[i];
            if (raw == "--") { tail = args[(i + 1)..]; if (launch && tail.Length == 0) throw Bad("Expected an executable after --."); break; }
            if (!raw.StartsWith('-') || raw == "-") { words.Add(raw); continue; }
            int eq = raw.IndexOf('='); string k = eq < 0 ? raw : raw[..eq];
            k = k switch { "-s" => "--session", "-w" => "--window", "-b" => "--block", _ => k };
            bool allowed = k switch {
                "--session" => verb != "new", "--window" or "--block" => verb is not ("run" or "new"),
                "--json" => true, "--cwd" or "--label" or "--keep-open" or "--focus" or "--wait" => launch,
                "--split" or "--ratio" => verb == "split", "--trim" or "--unwrap" or "--format" => verb == "capture",
                "--for" => verb == "wait" || launch, _ => false };
            if (!allowed || options.ContainsKey(k)) throw Bad($"Unexpected or duplicate option {k} for {verb}.");
            if (k is "--json" or "--trim" or "--unwrap" or "--wait" or "--keep-open")
            { if (eq >= 0) throw Bad($"{k} takes no value."); options.Add(k, "true"); continue; }
            string value = eq >= 0 ? raw[(eq + 1)..] : ++i < args.Length && args[i] != "--" ? args[i] : throw Bad($"{k} requires a value.");
            if (string.IsNullOrWhiteSpace(value)) throw Bad($"Empty value for {k}."); options.Add(k, value);
        }
        if (!launch && tail.Length != 0) { words.AddRange(tail); tail = []; }
        var result = new ShellSyntax(verb, options, [..words], tail);
        if (words.Count > (verb switch { "new" or "send" or "wait" or "capture" => 1, "send-key" => 64, _ => 0 })) throw Bad("Too many operands; launch commands require -- before child argv.");
        if (verb == "send-key" && words.Count == 0) throw Bad("Supply at least one named key.");
        if (verb is "wait" or "capture" && words.Count == 1 && result.Has("--block")) throw Bad("Block specified twice.");
        if (result.Get("--focus") is not (null or "false")) throw Bad("Only --focus=false is supported: headless commands do not move another client's focus.");
        if (result.Get("--split") is not (null or "right" or "left" or "above" or "below")) throw Bad("Invalid split direction.");
        if (result.Get("--format") is not (null or "text" or "html" or "vt")) throw Bad("Expected text, html or vt.");
        if (result.Has("--for") && launch && !result.Has("--wait")) throw Bad("--for on launch requires --wait.");
        if (result.Get("--label") is {} label && (label.Length > 256 || label.Any(char.IsControl))) throw Bad("Invalid label.");
        if (tail.Length > 257 || tail.Length != 0 && string.IsNullOrWhiteSpace(tail[0]) || tail.Any(w => w.Length > 32768 || w.Contains('\0'))) throw Bad("Invalid command argv.");
        _ = result.Share; _ = result.Deadline;
        return result;
    }
    internal double Share => Get("--ratio") is not {} raw ? .5 : double.TryParse(raw, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double n) && n is > 0 and < 100 ? n / 100 : throw Bad("--ratio must be a percentage strictly between 0 and 100.");
    internal TimeSpan? Deadline
    {
        get
        {
            if (Get("--for") is not {} s) return null;
            int suffix = s.EndsWith("ms",StringComparison.Ordinal) ? 2 : 1;
            var scale = suffix == 2 ? 1 : s[^1] switch { 's' => 1000, 'm' => 60000, 'h' => 3600000, _ => 0 };
            if (scale == 0 || !double.TryParse(s[..^suffix], NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var n) || n * scale is not (>= 1 and <= 2147483647)) throw Bad("--for expects a positive duration in ms, s, m or h (maximum 24.8 days).");
            return TimeSpan.FromMilliseconds(n * scale);
        }
    }
    private static ArgumentException Bad(string message) => new(message);
}
