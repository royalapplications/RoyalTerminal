// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// Policy reference: Ghostty #13634, macos/Sources/Helpers/UntrustedURL.swift.

using System.Buffers;
using System.Globalization;
using System.Text;

namespace RoyalTerminal.Terminal;

/// <summary>The next permitted step for a producer-controlled hyperlink.</summary>
public enum TerminalHyperlinkDisposition
{
    /// <summary>A well-formed HTTP, HTTPS or nonempty mailto target may open directly.</summary>
    Allow,
    /// <summary>A custom scheme needs informed confirmation before dispatch.</summary>
    Confirm,
    /// <summary>A local file needs canonicalization and platform safety inspection before dispatch.</summary>
    InspectFile,
    /// <summary>The target must not be dispatched; a host may explain the denial and offer copying.</summary>
    Deny,
}

/// <summary>Why a hyperlink cannot currently be opened.</summary>
public enum TerminalHyperlinkDenialReason
{
    /// <summary>No denial has been determined.</summary>
    None,
    /// <summary>The target lacks a valid explicit scheme or has invalid URL structure.</summary>
    Malformed,
    /// <summary>The target contains invisible, line-breaking or malformed Unicode characters.</summary>
    UnsafeCharacters,
    /// <summary>An HTTP or HTTPS target has no valid authority.</summary>
    InvalidWebHost,
}

/// <summary>An immutable, untrusted launch request; classifying it never opens a URL or reads a file.</summary>
/// <param name="Target">The original target, also used for an explicit copy action.</param>
/// <param name="DisplayText">A single-line preview with unsafe scalars escaped visibly.</param>
/// <param name="Uri">The parsed absolute URI, if available; its presence is not permission to open it.</param>
/// <param name="Disposition">The next permitted step.</param>
/// <param name="DenialReason">The reason for a denied request.</param>
public sealed record TerminalHyperlinkRequest(string Target, string DisplayText, Uri? Uri,
    TerminalHyperlinkDisposition Disposition, TerminalHyperlinkDenialReason DenialReason);

/// <summary>Pure shared policy for links supplied by either terminal engine.</summary>
public static class TerminalHyperlinkSafety
{
    /// <summary>Classifies a target before an OS launcher can normalize or dispatch it.</summary>
    public static TerminalHyperlinkRequest Classify(string target)
    {
        ArgumentNullException.ThrowIfNull(target);
        string display = SanitizeDisplay(target);
        if (!ReferenceEquals(display, target)) return Denied(TerminalHyperlinkDenialReason.UnsafeCharacters);
        int colon = target.IndexOf(':');
        if (colon <= 0 || !Uri.CheckSchemeName(target[..colon]) ||
            !Uri.TryCreate(target, UriKind.Absolute, out Uri? uri) ||
            !uri.Scheme.Equals(target[..colon], StringComparison.OrdinalIgnoreCase))
            return Denied(TerminalHyperlinkDenialReason.Malformed);
        switch (uri.Scheme.ToLowerInvariant())
        {
            case "http":
            case "https":
                ReadOnlySpan<char> authority = target.AsSpan(colon + 1);
                if (authority.Length <= 2 || !authority.StartsWith("//", StringComparison.Ordinal) ||
                    authority[2] is '/' or '\\' or '?' or '#' || string.IsNullOrEmpty(uri.Host))
                    return Denied(TerminalHyperlinkDenialReason.InvalidWebHost);
                int authorityEnd = authority[2..].IndexOfAny('/', '?', '#');
                if (authority.Slice(2, authorityEnd < 0 ? authority.Length - 2 : authorityEnd).Contains('\\'))
                    return Denied(TerminalHyperlinkDenialReason.InvalidWebHost);
                return new(target, display, uri, TerminalHyperlinkDisposition.Allow, TerminalHyperlinkDenialReason.None);
            case "mailto":
                ReadOnlySpan<char> address = target.AsSpan(colon + 1);
                int suffix = address.IndexOfAny('?', '#');
                if (suffix >= 0) address = address[..suffix];
                if (address.IsEmpty) return Denied(TerminalHyperlinkDenialReason.Malformed);
                return new(target, display, uri, TerminalHyperlinkDisposition.Allow, TerminalHyperlinkDenialReason.None);
            case "file":
                if (!uri.IsFile || uri.Query.Length != 0 || uri.Fragment.Length != 0 ||
                    (uri.Host.Length != 0 && !uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)))
                    return Denied(TerminalHyperlinkDenialReason.Malformed);
                return new(target, display, uri, TerminalHyperlinkDisposition.InspectFile, TerminalHyperlinkDenialReason.None);
            default:
                return new(target, display, uri, TerminalHyperlinkDisposition.Confirm, TerminalHyperlinkDenialReason.None);
        }

        TerminalHyperlinkRequest Denied(TerminalHyperlinkDenialReason reason)
            => new(target, display, null, TerminalHyperlinkDisposition.Deny, reason);
    }

    /// <summary>Escapes unsafe scalars without changing safe target spellings or decoding percent escapes.</summary>
    public static string SanitizeDisplay(string target)
    {
        ArgumentNullException.ThrowIfNull(target);
        StringBuilder? result = null;
        for (int index = 0; index < target.Length;)
        {
            OperationStatus status = Rune.DecodeFromUtf16(target.AsSpan(index), out Rune rune, out int length);
            bool malformed = status != OperationStatus.Done;
            int scalar = malformed ? target[index] : rune.Value;
            if (malformed) length = 1;
            bool unsafeScalar = malformed || scalar is <= 0x1F or (>= 0x7F and <= 0x9F) or 0x061C or
                (>= 0x200B and <= 0x200F) or (>= 0x2028 and <= 0x202E) or 0x2060 or
                (>= 0x2066 and <= 0x2069) or 0xFEFF;
            if (unsafeScalar)
            {
                if (result is null) { result = new(target.Length); result.Append(target.AsSpan(0, index)); }
                result.Append("\\u{").Append(scalar.ToString("X", CultureInfo.InvariantCulture)).Append('}');
            }
            else result?.Append(target.AsSpan(index, length));
            index += length;
        }
        return result?.ToString() ?? target;
    }
}
