// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Terminal - Durable command history contracts.

using System.Collections.ObjectModel;

namespace RoyalTerminal.Terminal;

/// <summary>
/// Versioned command history document.
/// </summary>
public sealed record TerminalCommandHistoryDocument
{
    /// <summary>
    /// Current supported command history document format version.
    /// </summary>
    public const int CurrentFormatVersion = 1;

    /// <summary>
    /// Default maximum number of command entries retained in a document.
    /// </summary>
    public const int DefaultRetentionLimit = 5000;

    /// <summary>
    /// Command history document format version.
    /// </summary>
    public int FormatVersion { get; init; } = CurrentFormatVersion;

    /// <summary>
    /// Maximum number of newest command entries to keep when normalizing the document.
    /// </summary>
    public int RetentionLimit { get; init; } = DefaultRetentionLimit;

    /// <summary>
    /// Persisted command history entries.
    /// </summary>
    public List<TerminalCommandHistoryEntry> Entries { get; init; } = [];
}

/// <summary>
/// Durable command execution entry captured from shell integration metadata.
/// </summary>
public sealed record TerminalCommandHistoryEntry
{
    /// <summary>
    /// Stable command entry identifier.
    /// </summary>
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// Command line reported by shell integration.
    /// </summary>
    public string CommandLine { get; init; } = string.Empty;

    /// <summary>
    /// Time when command output started, in UTC.
    /// </summary>
    public DateTimeOffset StartedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Time when command completion was reported, in UTC.
    /// </summary>
    public DateTimeOffset? CompletedAtUtc { get; init; }

    /// <summary>
    /// Command exit code reported by shell integration.
    /// </summary>
    public int? ExitCode { get; init; }

    /// <summary>
    /// Working directory reported by shell integration when the command ran.
    /// </summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>
    /// Session profile identifier, when known.
    /// </summary>
    public string? ProfileId { get; init; }

    /// <summary>
    /// Terminal transport identifier, when known.
    /// </summary>
    public string? TransportId { get; init; }

    /// <summary>
    /// Shell integration host, when reported.
    /// </summary>
    public string? Host { get; init; }

    /// <summary>
    /// Shell identifier, when known.
    /// </summary>
    public string? ShellId { get; init; }

    /// <summary>
    /// Application session identifier, when reported by OSC 133 aid.
    /// </summary>
    public string? ApplicationId { get; init; }
}

/// <summary>
/// Immutable context stamped onto captured command history entries.
/// </summary>
public sealed record TerminalCommandHistoryCaptureContext(
    string? ProfileId = null,
    string? TransportId = null,
    string? Host = null,
    string? ShellId = null);

/// <summary>
/// Type of command suggestion shown to the user.
/// </summary>
public enum TerminalCommandSuggestionKind
{
    /// <summary>
    /// Suggestion produced from durable command history.
    /// </summary>
    History,

    /// <summary>
    /// Suggestion produced from a built-in or configured command snippet.
    /// </summary>
    Snippet,
}

/// <summary>
/// Reusable command snippet that can participate in command suggestions.
/// </summary>
public sealed record TerminalCommandSnippet(
    string Trigger,
    string CommandLine,
    string? Description = null);

/// <summary>
/// Provides built-in command snippets for the default suggestion experience.
/// </summary>
public static class TerminalCommandSnippets
{
    private static readonly ReadOnlyCollection<TerminalCommandSnippet> s_defaultSnippets = Array.AsReadOnly(
    [
        new TerminalCommandSnippet("..", "cd ..", "Move to parent directory"),
        new TerminalCommandSnippet("ll", "ls -la", "List all files with details"),
        new TerminalCommandSnippet("gs", "git status", "Show repository status"),
        new TerminalCommandSnippet("gd", "git diff", "Show unstaged changes"),
        new TerminalCommandSnippet("gl", "git log --oneline --decorate --graph", "Show compact commit graph"),
        new TerminalCommandSnippet("gb", "git branch --sort=-committerdate", "List recent branches"),
        new TerminalCommandSnippet("dt", "dotnet test", "Run .NET tests"),
        new TerminalCommandSnippet("db", "dotnet build", "Build .NET solution or project"),
        new TerminalCommandSnippet("npmt", "npm test", "Run npm tests"),
        new TerminalCommandSnippet("dps", "docker ps", "List running containers"),
    ]);

    /// <summary>
    /// Gets the built-in snippets used by the demo application.
    /// </summary>
    public static IReadOnlyList<TerminalCommandSnippet> GetDefaultSnippets()
    {
        return s_defaultSnippets;
    }
}

/// <summary>
/// Command suggestion produced from persisted command history or snippets.
/// </summary>
public sealed record TerminalCommandSuggestion(
    string CommandLine,
    string? WorkingDirectory,
    DateTimeOffset LastUsedAtUtc,
    int UseCount,
    TerminalCommandSuggestionKind Kind = TerminalCommandSuggestionKind.History,
    string? Description = null,
    double Score = 0);

/// <summary>
/// Request passed to command suggestion providers.
/// </summary>
public sealed record TerminalCommandSuggestionRequest
{
    /// <summary>
    /// Creates a command suggestion request.
    /// </summary>
    /// <param name="document">Command history document.</param>
    public TerminalCommandSuggestionRequest(TerminalCommandHistoryDocument document)
    {
        Document = document ?? throw new ArgumentNullException(nameof(document));
    }

    /// <summary>
    /// Gets the command history document.
    /// </summary>
    public TerminalCommandHistoryDocument Document { get; init; }

    /// <summary>
    /// Gets the command query.
    /// </summary>
    public string? Query { get; init; }

    /// <summary>
    /// Gets the active working directory used for ranking.
    /// </summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>
    /// Gets the active profile id used to scope history, when supplied.
    /// </summary>
    public string? ProfileId { get; init; }

    /// <summary>
    /// Gets the active transport id used to scope history, when supplied.
    /// </summary>
    public string? TransportId { get; init; }

    /// <summary>
    /// Gets provider snippets, including profile-scoped snippets.
    /// </summary>
    public IReadOnlyList<TerminalCommandSnippet>? Snippets { get; init; }

    /// <summary>
    /// Gets the maximum number of suggestions to return.
    /// </summary>
    public int Limit { get; init; } = 10;
}

/// <summary>
/// Produces command suggestions from a suggestion request.
/// </summary>
public interface ITerminalCommandSuggestionProvider
{
    /// <summary>
    /// Gets command suggestions for the supplied request.
    /// </summary>
    /// <param name="request">Suggestion request.</param>
    /// <returns>Suggestions ordered by provider relevance.</returns>
    IReadOnlyList<TerminalCommandSuggestion> GetSuggestions(TerminalCommandSuggestionRequest request);
}

/// <summary>
/// Produces command-line suggestions from shell-integrated command history.
/// </summary>
public sealed class TerminalCommandSuggestionService : ITerminalCommandSuggestionProvider
{
    /// <inheritdoc />
    public IReadOnlyList<TerminalCommandSuggestion> GetSuggestions(TerminalCommandSuggestionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return GetSuggestions(
            request.Document,
            request.Query,
            request.WorkingDirectory,
            request.Limit,
            request.Snippets,
            request.ProfileId,
            request.TransportId);
    }

    /// <summary>
    /// Gets scored command suggestions that match the supplied query.
    /// </summary>
    public IReadOnlyList<TerminalCommandSuggestion> GetSuggestions(
        TerminalCommandHistoryDocument document,
        string? prefix,
        string? workingDirectory = null,
        int limit = 10,
        IReadOnlyList<TerminalCommandSnippet>? snippets = null,
        string? profileId = null,
        string? transportId = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (limit <= 0)
        {
            return [];
        }

        string normalizedPrefix = prefix?.Trim() ?? string.Empty;
        string? normalizedWorkingDirectory = NormalizeOptional(workingDirectory);
        string? normalizedProfileId = NormalizeOptional(profileId);
        string? normalizedTransportId = NormalizeOptional(transportId);
        Dictionary<string, SuggestionAccumulator> accumulators = new(StringComparer.Ordinal);
        DateTimeOffset newestTimestamp = DateTimeOffset.MinValue;
        for (int i = document.Entries.Count - 1; i >= 0; i--)
        {
            TerminalCommandHistoryEntry entry = document.Entries[i];
            if (!MatchesScope(entry.ProfileId, normalizedProfileId) ||
                !MatchesScope(entry.TransportId, normalizedTransportId))
            {
                continue;
            }

            string? commandLine = NormalizeOptional(entry.CommandLine);
            DateTimeOffset entryTimestamp = entry.CompletedAtUtc ?? entry.StartedAtUtc;
            if (commandLine is null ||
                GetQueryMatchQuality(commandLine, normalizedPrefix) == 0)
            {
                continue;
            }

            if (entryTimestamp > newestTimestamp)
            {
                newestTimestamp = entryTimestamp;
            }

            if (!accumulators.TryGetValue(commandLine, out SuggestionAccumulator? accumulator))
            {
                accumulator = new SuggestionAccumulator(
                    commandLine,
                    NormalizeOptional(entry.WorkingDirectory),
                    entryTimestamp,
                    useCount: 0,
                    workingDirectoryAffinity: GetWorkingDirectoryAffinity(entry.WorkingDirectory, normalizedWorkingDirectory),
                    kind: TerminalCommandSuggestionKind.History,
                    description: null);
                accumulators[commandLine] = accumulator;
            }

            accumulator.UseCount++;
            if (entryTimestamp > accumulator.LastUsedAtUtc)
            {
                accumulator.LastUsedAtUtc = entryTimestamp;
            }

            int workingDirectoryAffinity = GetWorkingDirectoryAffinity(entry.WorkingDirectory, normalizedWorkingDirectory);
            if (workingDirectoryAffinity > accumulator.WorkingDirectoryAffinity)
            {
                accumulator.WorkingDirectory = NormalizeOptional(entry.WorkingDirectory);
                accumulator.WorkingDirectoryAffinity = workingDirectoryAffinity;
            }
        }

        AddSnippetSuggestions(accumulators, snippets, normalizedPrefix, newestTimestamp);

        List<SuggestionAccumulator> ordered = new(accumulators.Values);
        for (int i = 0; i < ordered.Count; i++)
        {
            ordered[i].Score = CalculateScore(ordered[i], normalizedPrefix);
        }

        ordered.Sort(static (left, right) =>
        {
            int scoreComparison = right.Score.CompareTo(left.Score);
            if (scoreComparison != 0)
            {
                return scoreComparison;
            }

            int lastUsedComparison = right.LastUsedAtUtc.CompareTo(left.LastUsedAtUtc);
            return lastUsedComparison != 0
                ? lastUsedComparison
                : string.Compare(left.CommandLine, right.CommandLine, StringComparison.Ordinal);
        });

        int resultCount = Math.Min(limit, ordered.Count);
        TerminalCommandSuggestion[] suggestions = new TerminalCommandSuggestion[resultCount];
        for (int i = 0; i < resultCount; i++)
        {
            SuggestionAccumulator accumulator = ordered[i];
            suggestions[i] = new TerminalCommandSuggestion(
                accumulator.CommandLine,
                accumulator.WorkingDirectory,
                accumulator.LastUsedAtUtc,
                accumulator.UseCount,
                accumulator.Kind,
                accumulator.Description,
                accumulator.Score);
        }

        return suggestions;
    }

    private static void AddSnippetSuggestions(
        Dictionary<string, SuggestionAccumulator> accumulators,
        IReadOnlyList<TerminalCommandSnippet>? snippets,
        string normalizedPrefix,
        DateTimeOffset newestTimestamp)
    {
        if (snippets is null || snippets.Count == 0)
        {
            return;
        }

        DateTimeOffset snippetTimestamp = newestTimestamp == DateTimeOffset.MinValue
            ? DateTimeOffset.UnixEpoch
            : newestTimestamp.AddTicks(1);
        for (int i = 0; i < snippets.Count; i++)
        {
            TerminalCommandSnippet snippet = snippets[i];
            string? commandLine = NormalizeOptional(snippet.CommandLine);
            string? trigger = NormalizeOptional(snippet.Trigger);
            if (commandLine is null ||
                trigger is null ||
                !SnippetMatches(trigger, commandLine, normalizedPrefix))
            {
                continue;
            }

            if (accumulators.TryGetValue(commandLine, out SuggestionAccumulator? existing))
            {
                existing.Kind = TerminalCommandSuggestionKind.History;
                existing.Description ??= NormalizeOptional(snippet.Description);
                existing.HasSnippetAlias = true;
                continue;
            }

            accumulators[commandLine] = new SuggestionAccumulator(
                commandLine,
                workingDirectory: null,
                snippetTimestamp,
                useCount: 0,
                workingDirectoryAffinity: 0,
                kind: TerminalCommandSuggestionKind.Snippet,
                description: NormalizeOptional(snippet.Description))
            {
                HasSnippetAlias = true,
            };
        }
    }

    private static bool SnippetMatches(string trigger, string commandLine, string normalizedPrefix)
    {
        return normalizedPrefix.Length == 0 ||
               trigger.StartsWith(normalizedPrefix, StringComparison.OrdinalIgnoreCase) ||
               commandLine.StartsWith(normalizedPrefix, StringComparison.OrdinalIgnoreCase);
    }

    private static double CalculateScore(SuggestionAccumulator accumulator, string normalizedPrefix)
    {
        double score = accumulator.Kind == TerminalCommandSuggestionKind.Snippet ? 70 : 100;
        score += accumulator.WorkingDirectoryAffinity * 40;
        score += Math.Min(accumulator.UseCount, 20) * 6;
        if (accumulator.HasSnippetAlias)
        {
            score += 12;
        }

        score += GetQueryMatchQuality(accumulator.CommandLine, normalizedPrefix) * 25;
        return score;
    }

    private static int GetQueryMatchQuality(string commandLine, string normalizedPrefix)
    {
        if (normalizedPrefix.Length == 0)
        {
            return 1;
        }

        if (commandLine.StartsWith(normalizedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return 3;
        }

        int index = commandLine.IndexOf(normalizedPrefix, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return 0;
        }

        return index == 0 || IsTokenBoundary(commandLine[index - 1]) ? 2 : 1;
    }

    private static int GetWorkingDirectoryAffinity(string? entryWorkingDirectory, string? requestedWorkingDirectory)
    {
        string? normalizedEntryWorkingDirectory = NormalizePath(entryWorkingDirectory);
        string? normalizedRequestedWorkingDirectory = NormalizePath(requestedWorkingDirectory);
        if (normalizedEntryWorkingDirectory is null || normalizedRequestedWorkingDirectory is null)
        {
            return 0;
        }

        if (string.Equals(
                normalizedEntryWorkingDirectory,
                normalizedRequestedWorkingDirectory,
                StringComparison.Ordinal))
        {
            return 2;
        }

        return IsSameDirectoryTree(normalizedEntryWorkingDirectory, normalizedRequestedWorkingDirectory) ? 1 : 0;
    }

    private static bool MatchesScope(string? entryValue, string? requestedValue)
    {
        return requestedValue is null ||
               string.Equals(NormalizeOptional(entryValue), requestedValue, StringComparison.Ordinal);
    }

    private static bool IsSameDirectoryTree(string left, string right)
    {
        return IsPathAncestor(left, right) || IsPathAncestor(right, left);
    }

    private static bool IsPathAncestor(string ancestor, string descendant)
    {
        return descendant.Length > ancestor.Length &&
               descendant.StartsWith(ancestor, StringComparison.Ordinal) &&
               IsDirectorySeparator(descendant[ancestor.Length]);
    }

    private static string? NormalizePath(string? value)
    {
        string? normalized = NormalizeOptional(value);
        if (normalized is null)
        {
            return null;
        }

        while (normalized.Length > 1 && IsDirectorySeparator(normalized[^1]))
        {
            normalized = normalized[..^1];
        }

        return normalized;
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static bool IsTokenBoundary(char value)
    {
        return char.IsWhiteSpace(value) ||
               value is '-' or '_' or '/' or '\\' or '.' or ':' or ';' or '|';
    }

    private static bool IsDirectorySeparator(char value)
    {
        return value is '/' or '\\';
    }

    private sealed class SuggestionAccumulator
    {
        public SuggestionAccumulator(
            string commandLine,
            string? workingDirectory,
            DateTimeOffset lastUsedAtUtc,
            int useCount,
            int workingDirectoryAffinity,
            TerminalCommandSuggestionKind kind,
            string? description)
        {
            CommandLine = commandLine;
            WorkingDirectory = workingDirectory;
            LastUsedAtUtc = lastUsedAtUtc;
            UseCount = useCount;
            WorkingDirectoryAffinity = workingDirectoryAffinity;
            Kind = kind;
            Description = description;
        }

        public string CommandLine { get; }
        public string? WorkingDirectory { get; set; }
        public DateTimeOffset LastUsedAtUtc { get; set; }
        public int UseCount { get; set; }
        public int WorkingDirectoryAffinity { get; set; }
        public TerminalCommandSuggestionKind Kind { get; set; }
        public string? Description { get; set; }
        public bool HasSnippetAlias { get; set; }
        public double Score { get; set; }
    }
}

/// <summary>
/// Converts shell integration events into completed command history entries.
/// </summary>
public sealed class TerminalCommandHistoryCaptureService
{
    private readonly TerminalCommandHistoryCaptureContext _context;
    private string? _currentWorkingDirectory;
    private string? _currentHost;
    private PendingCommand? _pendingCommand;

    /// <summary>
    /// Creates a command history capture service.
    /// </summary>
    public TerminalCommandHistoryCaptureService(TerminalCommandHistoryCaptureContext? context = null)
    {
        _context = context ?? new TerminalCommandHistoryCaptureContext();
    }

    /// <summary>
    /// Processes one shell integration event and returns a completed history entry when available.
    /// </summary>
    public TerminalCommandHistoryEntry? Process(TerminalShellIntegrationEvent value)
    {
        ArgumentNullException.ThrowIfNull(value);

        switch (value.Kind)
        {
            case TerminalShellIntegrationEventKind.WorkingDirectoryChanged:
                _currentWorkingDirectory = NormalizeOptional(value.WorkingDirectory);
                _currentHost = NormalizeOptional(value.Host);
                return null;

            case TerminalShellIntegrationEventKind.OutputStarted:
                string? commandLine = NormalizeOptional(value.CommandLine);
                if (commandLine is null)
                {
                    return null;
                }

                _pendingCommand = new PendingCommand(
                    commandLine,
                    value.TimestampUtc.ToUniversalTime(),
                    NormalizeOptional(value.WorkingDirectory) ?? _currentWorkingDirectory,
                    NormalizeOptional(value.Host) ?? _currentHost,
                    NormalizeOptional(value.ApplicationId));
                return null;

            case TerminalShellIntegrationEventKind.CommandFinished:
                if (_pendingCommand is null)
                {
                    return null;
                }

                PendingCommand completed = _pendingCommand;
                _pendingCommand = null;
                return new TerminalCommandHistoryEntry
                {
                    Id = Guid.NewGuid().ToString("N"),
                    CommandLine = completed.CommandLine,
                    StartedAtUtc = completed.StartedAtUtc,
                    CompletedAtUtc = value.TimestampUtc.ToUniversalTime(),
                    ExitCode = value.ExitCode,
                    WorkingDirectory = completed.WorkingDirectory,
                    ProfileId = NormalizeOptional(_context.ProfileId),
                    TransportId = NormalizeOptional(_context.TransportId),
                    Host = NormalizeOptional(_context.Host) ?? completed.Host,
                    ShellId = NormalizeOptional(_context.ShellId),
                    ApplicationId = NormalizeOptional(value.ApplicationId) ?? completed.ApplicationId,
                };

            default:
                return null;
        }
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private sealed record PendingCommand(
        string CommandLine,
        DateTimeOffset StartedAtUtc,
        string? WorkingDirectory,
        string? Host,
        string? ApplicationId);
}
