// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Terminal - Shell integration contracts and OSC parsers.

using System.Globalization;

namespace RoyalTerminal.Terminal;

/// <summary>
/// Shell integration event kind derived from OSC 7 and OSC 133 sequences.
/// </summary>
public enum TerminalShellIntegrationEventKind
{
    /// <summary>OSC 7 reported the current working directory.</summary>
    WorkingDirectoryChanged,

    /// <summary>OSC 133 L requested a fresh line.</summary>
    FreshLine,

    /// <summary>OSC 133 A marked a fresh line and new prompt.</summary>
    FreshLineNewPrompt,

    /// <summary>OSC 133 N marked a new command.</summary>
    NewCommand,

    /// <summary>OSC 133 P marked prompt start.</summary>
    PromptStarted,

    /// <summary>OSC 133 B marked prompt end and input start.</summary>
    InputStarted,

    /// <summary>OSC 133 I marked prompt end, input start, and end-of-line termination.</summary>
    InputStartedAndTerminatedEndOfLine,

    /// <summary>OSC 133 C marked input end and command output start.</summary>
    OutputStarted,

    /// <summary>OSC 133 D marked command completion.</summary>
    CommandFinished,
}

/// <summary>
/// Structured shell integration event emitted by a VT processor.
/// </summary>
public sealed record TerminalShellIntegrationEvent
{
    /// <summary>Gets the shell integration event kind.</summary>
    public TerminalShellIntegrationEventKind Kind { get; init; }

    /// <summary>Gets the event timestamp in UTC.</summary>
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Gets the decoded command line, when reported by OSC 133.</summary>
    public string? CommandLine { get; init; }

    /// <summary>Gets the decoded working directory, when reported by OSC 7.</summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>Gets the OSC 7 host, when available.</summary>
    public string? Host { get; init; }

    /// <summary>Gets the command exit code, when reported by OSC 133 D.</summary>
    public int? ExitCode { get; init; }

    /// <summary>Gets the semantic prompt application identifier, when reported.</summary>
    public string? ApplicationId { get; init; }

    /// <summary>Gets unknown or additional OSC 133 options.</summary>
    public IReadOnlyDictionary<string, string> Options { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
}

/// <summary>
/// Event arguments for shell integration events.
/// </summary>
public sealed class TerminalShellIntegrationEventArgs : EventArgs
{
    /// <summary>
    /// Creates shell integration event arguments.
    /// </summary>
    public TerminalShellIntegrationEventArgs(TerminalShellIntegrationEvent value)
    {
        Value = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>Gets the shell integration event.</summary>
    public TerminalShellIntegrationEvent Value { get; }
}

/// <summary>
/// Optional VT processor capability for structured shell integration events.
/// </summary>
public interface ITerminalShellIntegrationEventSource
{
    /// <summary>
    /// Raised when OSC 7 or OSC 133 shell integration metadata is parsed.
    /// </summary>
    event EventHandler<TerminalShellIntegrationEventArgs>? ShellIntegrationEventReceived;
}

/// <summary>
/// Parses OSC 7 and OSC 133 payloads into structured shell integration events.
/// </summary>
public sealed class TerminalShellIntegrationParser
{
    /// <summary>
    /// Raised when a shell integration event is parsed.
    /// </summary>
    public event EventHandler<TerminalShellIntegrationEventArgs>? EventReceived;

    /// <summary>
    /// Tries to handle an OSC payload.
    /// </summary>
    /// <param name="selectorCode">OSC selector code.</param>
    /// <param name="value">Payload after the selector separator.</param>
    /// <param name="timestampUtc">Optional event timestamp. When null, current UTC time is used.</param>
    /// <returns>True when the payload was recognized and emitted as a shell event.</returns>
    public bool TryHandleOsc(int selectorCode, string value, DateTimeOffset? timestampUtc = null)
    {
        return selectorCode switch
        {
            7 => TryHandleWorkingDirectory(value, timestampUtc),
            133 => TryHandleSemanticPrompt(value, timestampUtc),
            _ => false,
        };
    }

    private bool TryHandleWorkingDirectory(string value, DateTimeOffset? timestampUtc)
    {
        string normalizedValue = value.Trim();
        if (normalizedValue.Length == 0)
        {
            Raise(new TerminalShellIntegrationEvent
            {
                Kind = TerminalShellIntegrationEventKind.WorkingDirectoryChanged,
                TimestampUtc = NormalizeTimestamp(timestampUtc),
            });
            return true;
        }

        if (!Uri.TryCreate(normalizedValue, UriKind.Absolute, out Uri? uri) ||
            !IsWorkingDirectoryUriScheme(uri.Scheme))
        {
            return false;
        }

        string workingDirectory = GetWorkingDirectoryPath(uri);
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            return false;
        }

        Raise(new TerminalShellIntegrationEvent
        {
            Kind = TerminalShellIntegrationEventKind.WorkingDirectoryChanged,
            TimestampUtc = NormalizeTimestamp(timestampUtc),
            WorkingDirectory = workingDirectory,
            Host = string.IsNullOrWhiteSpace(uri.Host) ? null : uri.Host,
        });
        return true;
    }

    private static string GetWorkingDirectoryPath(Uri uri)
    {
        string workingDirectory = TryUnescapeDataString(uri.AbsolutePath, out string decodedPath)
            ? decodedPath
            : uri.AbsolutePath;
        if (workingDirectory.Length >= 3 &&
            workingDirectory[0] == '/' &&
            IsAsciiLetter(workingDirectory[1]) &&
            workingDirectory[2] == ':')
        {
            return workingDirectory[1..];
        }

        if (workingDirectory.Length >= 3 &&
            workingDirectory[0] == '/' &&
            workingDirectory[1] == '\\' &&
            workingDirectory[2] == '\\')
        {
            return workingDirectory[1..];
        }

        return workingDirectory;
    }

    private static bool IsAsciiLetter(char value)
    {
        return value is >= 'A' and <= 'Z' or >= 'a' and <= 'z';
    }

    private bool TryHandleSemanticPrompt(string value, DateTimeOffset? timestampUtc)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        TerminalShellIntegrationEventKind kind;
        switch (value[0])
        {
            case 'A':
                kind = TerminalShellIntegrationEventKind.FreshLineNewPrompt;
                break;
            case 'B':
                kind = TerminalShellIntegrationEventKind.InputStarted;
                break;
            case 'C':
                kind = TerminalShellIntegrationEventKind.OutputStarted;
                break;
            case 'D':
                kind = TerminalShellIntegrationEventKind.CommandFinished;
                break;
            case 'I':
                kind = TerminalShellIntegrationEventKind.InputStartedAndTerminatedEndOfLine;
                break;
            case 'L':
                if (value.Length != 1)
                {
                    return false;
                }

                kind = TerminalShellIntegrationEventKind.FreshLine;
                break;
            case 'N':
                kind = TerminalShellIntegrationEventKind.NewCommand;
                break;
            case 'P':
                kind = TerminalShellIntegrationEventKind.PromptStarted;
                break;
            default:
                return false;
        }

        if (value.Length > 1 && value[1] != ';')
        {
            return false;
        }

        string optionsText = value.Length > 2 ? value[2..] : string.Empty;
        Dictionary<string, string> options = ParseOptions(optionsText);
        int? exitCode = kind == TerminalShellIntegrationEventKind.CommandFinished
            ? ParseExitCode(optionsText, options)
            : null;
        string? commandLine = DecodeCommandLine(options);

        Raise(new TerminalShellIntegrationEvent
        {
            Kind = kind,
            TimestampUtc = NormalizeTimestamp(timestampUtc),
            CommandLine = NormalizeOptional(commandLine),
            ExitCode = exitCode,
            ApplicationId = options.TryGetValue("aid", out string? applicationId)
                ? NormalizeOptional(applicationId)
                : null,
            Options = options,
        });
        return true;
    }

    private void Raise(TerminalShellIntegrationEvent value)
    {
        EventReceived?.Invoke(this, new TerminalShellIntegrationEventArgs(value));
    }

    private static bool IsWorkingDirectoryUriScheme(string scheme)
    {
        return string.Equals(scheme, Uri.UriSchemeFile, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(scheme, "kitty-shell-cwd", StringComparison.OrdinalIgnoreCase);
    }

    private static Dictionary<string, string> ParseOptions(string optionsText)
    {
        Dictionary<string, string> options = new(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(optionsText))
        {
            return options;
        }

        ReadOnlySpan<char> remaining = optionsText.AsSpan();
        while (!remaining.IsEmpty)
        {
            int separatorIndex = remaining.IndexOf(';');
            ReadOnlySpan<char> segment = separatorIndex < 0
                ? remaining
                : remaining[..separatorIndex];
            if (!segment.IsEmpty)
            {
                int equalsIndex = segment.IndexOf('=');
                if (equalsIndex > 0)
                {
                    string key = segment[..equalsIndex].ToString();
                    string optionValue = segment[(equalsIndex + 1)..].ToString();
                    options[key] = optionValue;
                }
            }

            if (separatorIndex < 0)
            {
                break;
            }

            remaining = remaining[(separatorIndex + 1)..];
        }

        return options;
    }

    private static int? ParseExitCode(string optionsText, IReadOnlyDictionary<string, string> options)
    {
        if (options.TryGetValue("exit_code", out string? namedExitCode) &&
            int.TryParse(namedExitCode, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedNamedExitCode))
        {
            return parsedNamedExitCode;
        }

        int separatorIndex = optionsText.IndexOf(';', StringComparison.Ordinal);
        string firstSegment = separatorIndex < 0
            ? optionsText
            : optionsText[..separatorIndex];
        return int.TryParse(firstSegment, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedExitCode)
            ? parsedExitCode
            : null;
    }

    private static string? DecodeCommandLine(IReadOnlyDictionary<string, string> options)
    {
        if (options.TryGetValue("cmdline_url", out string? encodedUrlCommandLine))
        {
            return TryUnescapeDataString(encodedUrlCommandLine, out string decodedCommandLine)
                ? decodedCommandLine
                : null;
        }

        return options.TryGetValue("cmdline", out string? encodedCommandLine)
            ? DecodeShellQuotedCommandLine(encodedCommandLine)
            : null;
    }

    private static bool TryUnescapeDataString(string value, out string decoded)
    {
        if (ContainsMalformedPercentEscape(value.AsSpan()))
        {
            decoded = string.Empty;
            return false;
        }

        try
        {
            decoded = Uri.UnescapeDataString(value);
            return true;
        }
        catch (UriFormatException)
        {
            decoded = string.Empty;
            return false;
        }
    }

    private static bool ContainsMalformedPercentEscape(ReadOnlySpan<char> value)
    {
        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] != '%')
            {
                continue;
            }

            if (i + 2 >= value.Length ||
                !IsAsciiHexDigit(value[i + 1]) ||
                !IsAsciiHexDigit(value[i + 2]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsAsciiHexDigit(char value)
    {
        return value is >= '0' and <= '9' or >= 'A' and <= 'F' or >= 'a' and <= 'f';
    }

    private static string DecodeShellQuotedCommandLine(string value)
    {
        ReadOnlySpan<char> span = value.AsSpan();
        if (span.Length >= 3 && span[0] == '$' && span[1] == '\'' && span[^1] == '\'')
        {
            span = span[2..^1];
        }
        else if (span.Length >= 2 && span[0] == '\'' && span[^1] == '\'' ||
                 span.Length >= 2 && span[0] == '"' && span[^1] == '"')
        {
            span = span[1..^1];
        }

        if (span.IndexOf('\\') < 0)
        {
            return span.ToString();
        }

        return DecodeBackslashEscapes(span);
    }

    private static string DecodeBackslashEscapes(ReadOnlySpan<char> value)
    {
        char[] buffer = new char[value.Length];
        int count = 0;
        for (int i = 0; i < value.Length; i++)
        {
            char current = value[i];
            if (current != '\\' || i + 1 >= value.Length)
            {
                buffer[count++] = current;
                continue;
            }

            char next = value[++i];
            buffer[count++] = next switch
            {
                'n' => '\n',
                'r' => '\r',
                't' => '\t',
                '\\' => '\\',
                '\'' => '\'',
                '"' => '"',
                _ => next,
            };
        }

        return new string(buffer, 0, count);
    }

    private static DateTimeOffset NormalizeTimestamp(DateTimeOffset? timestampUtc)
    {
        return timestampUtc?.ToUniversalTime() ?? DateTimeOffset.UtcNow;
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
