// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Terminal;

namespace RoyalTerminal.Avalonia.App.Services.Links;

internal enum HyperlinkFileKind { Other, Regular, Directory }

internal readonly record struct HyperlinkFileFacts(string CanonicalPath, HyperlinkFileKind Kind,
    bool Executable, bool UnsafeContentType);

internal interface IHyperlinkFileProbe
{
    HyperlinkFileFacts Read(string path);
}

internal interface ITerminalHyperlinkFileInspector
{
    ValueTask<TerminalHyperlinkRequest> InspectAsync(TerminalHyperlinkRequest request, CancellationToken cancellationToken);
}

internal static class HyperlinkFilePolicy
{
    // Ghostty #13634, UntrustedURL.swift. Evaluate the canonical object's name,
    // not an innocent-looking alias. Directory execute bits are traversal rights.
    internal static bool IsUnsafeExtension(string path, bool windows)
    {
        string extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension is ".action" or ".app" or ".applescript" or ".class" or ".command" or ".desktop" or
            ".inetloc" or ".jar" or ".mobileconfig" or ".mpkg" or ".pkg" or ".scpt" or ".terminal" or
            ".tool" or ".url" or ".webloc" or ".workflow") return true;
        // Windows has executable containers and shell shortcuts absent from
        // Launch Services' type hierarchy. These must not enter ShellExecute.
        return windows && (extension is ".exe" or ".dll" or ".com" or ".scr" or ".cpl" or ".pif" or
            ".bat" or ".cmd" or ".ps1" or ".psm1" or ".vbs" or ".vbe" or ".js" or ".jse" or
            ".wsf" or ".wsh" or ".hta" or ".msi" or ".msp" or ".mst" or ".lnk" or ".reg" or
            ".msc" or ".application" or ".appref-ms" or ".appx" or ".appxbundle" or ".msix" or
            ".msixbundle" or ".gadget" or ".sh" or ".bash" or ".zsh" or ".fish" or ".csh" or ".ksh" or
            ".py" or ".pyw" or ".pyc" or ".pyo" or ".pl" or ".pm" or ".rb" or ".php" or ".lua" or ".tcl");
    }

    internal static bool IsLocalPath(string path, bool windows)
    {
        if (path.Length == 0 || path.Contains('\0')) return false;
        if (!windows) return path[0] == '/' && !path.StartsWith("//", StringComparison.Ordinal);
        // Exclude UNC/device paths and alternate data streams before touching
        // the filesystem. Check again after resolving junctions/reparse points.
        if (path.Length < 3 || !char.IsAsciiLetter(path[0]) || path[1] != ':' ||
            path[2] is not ('/' or '\\') || path.AsSpan(2).IndexOf(':') >= 0) return false;
        foreach (Range range in path.AsSpan(3).SplitAny("\\/"))
        {
            ReadOnlySpan<char> component = path.AsSpan(3)[range];
            // Shell dispatch may trim these even when an extended-length file
            // handle preserves them. Never classify one name and open another.
            if (!component.IsEmpty && component[^1] is ' ' or '.') return false;
            int dot = component.IndexOf('.');
            ReadOnlySpan<char> stem = (dot >= 0 ? component[..dot] : component).TrimEnd(' ');
            if (stem.Equals("CON", StringComparison.OrdinalIgnoreCase) || stem.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
                stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) || stem.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
                stem.Equals("CONIN$", StringComparison.OrdinalIgnoreCase) || stem.Equals("CONOUT$", StringComparison.OrdinalIgnoreCase) ||
                stem.Equals("CLOCK$", StringComparison.OrdinalIgnoreCase)) return false;
            if (stem.Length == 4 && (stem[..3].Equals("COM", StringComparison.OrdinalIgnoreCase) || stem[..3].Equals("LPT", StringComparison.OrdinalIgnoreCase)) &&
                (char.IsAsciiDigit(stem[3]) || stem[3] is '\u00B9' or '\u00B2' or '\u00B3')) return false;
        }
        return true;
    }

    internal static TerminalHyperlinkRequest Apply(TerminalHyperlinkRequest request, HyperlinkFileFacts facts, bool windows)
    {
        string display = TerminalHyperlinkSafety.SanitizeDisplay(facts.CanonicalPath);
        if (!IsLocalPath(facts.CanonicalPath, windows)) return Denied(TerminalHyperlinkDenialReason.Malformed);
        if (facts.Kind is not (HyperlinkFileKind.Regular or HyperlinkFileKind.Directory))
            return Denied(TerminalHyperlinkDenialReason.InaccessibleFile);
        if (IsUnsafeExtension(facts.CanonicalPath, windows) || facts.UnsafeContentType ||
            (facts.Kind == HyperlinkFileKind.Regular && facts.Executable))
            return Denied(TerminalHyperlinkDenialReason.UnsafeFile);

        // Encode path bytes exactly once. Separators are structural; all other
        // characters, including percent, query and fragment delimiters, are data.
        string path = windows ? facts.CanonicalPath.Replace('\\', '/') : facts.CanonicalPath;
        string encoded = Uri.EscapeDataString(path).Replace("%2F", "/", StringComparison.Ordinal);
        if (windows) encoded = "/" + encoded.Replace("%3A", ":", StringComparison.Ordinal);
        Uri uri = new("file://" + encoded, UriKind.Absolute);
        return request with { Uri = uri, DisplayText = display, Disposition = TerminalHyperlinkDisposition.Allow,
            DenialReason = TerminalHyperlinkDenialReason.None };

        TerminalHyperlinkRequest Denied(TerminalHyperlinkDenialReason reason)
            => request with { Uri = null, DisplayText = display, Disposition = TerminalHyperlinkDisposition.Deny, DenialReason = reason };
    }
}
