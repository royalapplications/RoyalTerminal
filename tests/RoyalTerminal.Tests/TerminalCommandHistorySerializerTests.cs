// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class TerminalCommandHistorySerializerTests
{
    [Fact]
    public async Task Serializer_RoundTripsCommandHistoryEntry_WithExitCodeAndWorkingDirectory()
    {
        DateTimeOffset started = new(2026, 6, 30, 10, 0, 0, TimeSpan.Zero);
        DateTimeOffset completed = started.AddSeconds(3);
        TerminalCommandHistoryDocument document = new()
        {
            Entries =
            [
                new TerminalCommandHistoryEntry
                {
                    Id = " command-1 ",
                    CommandLine = " dotnet test ",
                    StartedAtUtc = started,
                    CompletedAtUtc = completed,
                    ExitCode = 0,
                    WorkingDirectory = " /Users/alice/project ",
                    ProfileId = " local ",
                    TransportId = "pty",
                    Host = " localhost ",
                    ShellId = " zsh ",
                    ApplicationId = " aid-1 ",
                },
            ],
        };

        await using MemoryStream stream = new();
        await TerminalCommandHistorySerializer.SaveAsync(document, stream);
        stream.Position = 0;

        TerminalCommandHistoryDocument restored = await TerminalCommandHistorySerializer.LoadAsync(stream);

        TerminalCommandHistoryEntry entry = Assert.Single(restored.Entries);
        Assert.Equal("command-1", entry.Id);
        Assert.Equal("dotnet test", entry.CommandLine);
        Assert.Equal(started, entry.StartedAtUtc);
        Assert.Equal(completed, entry.CompletedAtUtc);
        Assert.Equal(0, entry.ExitCode);
        Assert.Equal("/Users/alice/project", entry.WorkingDirectory);
        Assert.Equal("local", entry.ProfileId);
        Assert.Equal(TerminalTransportIds.Pty, entry.TransportId);
        Assert.Equal("localhost", entry.Host);
        Assert.Equal("zsh", entry.ShellId);
        Assert.Equal("aid-1", entry.ApplicationId);
    }

    [Fact]
    public void Serializer_AppliesRetentionLimit_KeepingNewestEntries()
    {
        DateTimeOffset baseTime = new(2026, 6, 30, 10, 0, 0, TimeSpan.Zero);
        TerminalCommandHistoryDocument document = new()
        {
            RetentionLimit = 2,
            Entries =
            [
                CreateEntry("oldest", "echo oldest", baseTime),
                CreateEntry("newest", "echo newest", baseTime.AddMinutes(2)),
                CreateEntry("middle", "echo middle", baseTime.AddMinutes(1)),
            ],
        };

        TerminalCommandHistoryDocument restored = TerminalCommandHistorySerializer.FromJson(
            TerminalCommandHistorySerializer.ToJson(document));

        Assert.Equal(2, restored.Entries.Count);
        Assert.DoesNotContain(restored.Entries, static entry => entry.Id == "oldest");
        Assert.Contains(restored.Entries, static entry => entry.Id == "middle");
        Assert.Contains(restored.Entries, static entry => entry.Id == "newest");
    }

    [Fact]
    public void Serializer_FromJson_RejectsCompletedBeforeStarted()
    {
        const string json = """
                            {
                              "formatVersion": 1,
                              "entries": [
                                {
                                  "id": "bad",
                                  "commandLine": "echo bad",
                                  "startedAtUtc": "2026-06-30T10:00:00+00:00",
                                  "completedAtUtc": "2026-06-30T09:59:00+00:00"
                                }
                              ]
                            }
                            """;

        Assert.Throws<InvalidDataException>(() => TerminalCommandHistorySerializer.FromJson(json));
    }

    [Fact]
    public void Serializer_FromJson_RejectsDuplicateEntryIds()
    {
        const string json = """
                            {
                              "formatVersion": 1,
                              "entries": [
                                { "id": "dup", "commandLine": "echo one" },
                                { "id": "DUP", "commandLine": "echo two" }
                              ]
                            }
                            """;

        Assert.Throws<InvalidDataException>(() => TerminalCommandHistorySerializer.FromJson(json));
    }

    [Fact]
    public void Serializer_FromJson_RejectsUnsupportedTransportId()
    {
        const string json = """
                            {
                              "formatVersion": 1,
                              "entries": [
                                {
                                  "id": "entry",
                                  "commandLine": "echo one",
                                  "transportId": "not-a-transport"
                                }
                              ]
                            }
                            """;

        Assert.Throws<InvalidDataException>(() => TerminalCommandHistorySerializer.FromJson(json));
    }

    [Fact]
    public async Task JsonFileStore_LoadMissingFile_ReturnsEmptyDocument()
    {
        string filePath = Path.Combine(Path.GetTempPath(), "royalterminal-tests", Guid.NewGuid() + ".history.json");
        JsonFileTerminalCommandHistoryStore store = new(filePath);

        TerminalCommandHistoryDocument restored = await store.LoadAsync();

        Assert.Equal(TerminalCommandHistoryDocument.CurrentFormatVersion, restored.FormatVersion);
        Assert.Empty(restored.Entries);
    }

    [Fact]
    public void StoreFactory_DefaultPath_LivesNextToSessionProfiles()
    {
        string profilePath = TerminalSessionProfileStoreFactory.GetDefaultFilePath();
        string historyPath = TerminalCommandHistoryStoreFactory.GetDefaultFilePath();

        Assert.Equal(Path.GetDirectoryName(profilePath), Path.GetDirectoryName(historyPath));
        Assert.Equal("command-history.json", Path.GetFileName(historyPath));

        string customPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".history.json");
        JsonFileTerminalCommandHistoryStore store = Assert.IsType<JsonFileTerminalCommandHistoryStore>(
            TerminalCommandHistoryStoreFactory.CreateDefault(customPath));
        Assert.Equal(customPath, store.FilePath);
    }

    [Fact]
    public void SuggestionService_ReturnsDistinctPrefixMatches_WithFrequencyRanking()
    {
        DateTimeOffset baseTime = new(2026, 6, 30, 10, 0, 0, TimeSpan.Zero);
        TerminalCommandHistoryDocument document = new()
        {
            Entries =
            [
                CreateEntry("one", "git status", baseTime),
                CreateEntry("two", "dotnet test", baseTime.AddMinutes(1)),
                CreateEntry("three", "git status", baseTime.AddMinutes(2)),
                CreateEntry("four", "git switch main", baseTime.AddMinutes(3)),
            ],
        };
        TerminalCommandSuggestionService service = new();

        IReadOnlyList<TerminalCommandSuggestion> suggestions = service.GetSuggestions(document, "git", limit: 10);

        Assert.Equal(2, suggestions.Count);
        Assert.Equal("git status", suggestions[0].CommandLine);
        Assert.Equal("git switch main", suggestions[1].CommandLine);
        Assert.Equal(2, suggestions[0].UseCount);
        Assert.True(suggestions[0].Score > suggestions[1].Score);
    }

    [Fact]
    public void SuggestionService_PrioritizesCurrentWorkingDirectory()
    {
        DateTimeOffset baseTime = new(2026, 6, 30, 10, 0, 0, TimeSpan.Zero);
        TerminalCommandHistoryDocument document = new()
        {
            Entries =
            [
                CreateEntry("one", "npm test", baseTime.AddMinutes(2)) with
                {
                    WorkingDirectory = "/repo/other",
                },
                CreateEntry("two", "npm run build", baseTime) with
                {
                    WorkingDirectory = "/repo/current",
                },
            ],
        };
        TerminalCommandSuggestionService service = new();

        IReadOnlyList<TerminalCommandSuggestion> suggestions = service.GetSuggestions(
            document,
            "npm",
            workingDirectory: "/repo/current",
            limit: 10);

        Assert.Equal("npm run build", suggestions[0].CommandLine);
        Assert.Equal("/repo/current", suggestions[0].WorkingDirectory);
    }

    [Fact]
    public void SuggestionService_IncludesSnippetMatches()
    {
        TerminalCommandHistoryDocument document = new();
        TerminalCommandSuggestionService service = new();

        IReadOnlyList<TerminalCommandSuggestion> suggestions = service.GetSuggestions(
            document,
            "gs",
            limit: 10,
            snippets: TerminalCommandSnippets.GetDefaultSnippets());

        TerminalCommandSuggestion suggestion = Assert.Single(suggestions);
        Assert.Equal("git status", suggestion.CommandLine);
        Assert.Equal(TerminalCommandSuggestionKind.Snippet, suggestion.Kind);
        Assert.Equal("Show repository status", suggestion.Description);
    }

    [Fact]
    public void SuggestionService_BoostsHistoryWhenSnippetHasSameCommand()
    {
        DateTimeOffset baseTime = new(2026, 6, 30, 10, 0, 0, TimeSpan.Zero);
        TerminalCommandHistoryDocument document = new()
        {
            Entries =
            [
                CreateEntry("one", "git status", baseTime),
                CreateEntry("two", "git switch main", baseTime.AddMinutes(1)),
            ],
        };
        TerminalCommandSuggestionService service = new();

        IReadOnlyList<TerminalCommandSuggestion> suggestions = service.GetSuggestions(
            document,
            "git",
            limit: 10,
            snippets: TerminalCommandSnippets.GetDefaultSnippets());

        Assert.Equal("git status", suggestions[0].CommandLine);
        Assert.Equal(TerminalCommandSuggestionKind.History, suggestions[0].Kind);
        Assert.Equal("Show repository status", suggestions[0].Description);
        Assert.True(suggestions[0].Score > suggestions[1].Score);
    }

    [Fact]
    public void SuggestionService_RequestScopesHistoryByProfileAndTransport()
    {
        DateTimeOffset baseTime = new(2026, 6, 30, 10, 0, 0, TimeSpan.Zero);
        TerminalCommandHistoryDocument document = new()
        {
            Entries =
            [
                CreateEntry("one", "deploy prod", baseTime) with
                {
                    ProfileId = "prod",
                    TransportId = TerminalTransportIds.Ssh,
                },
                CreateEntry("two", "deploy local", baseTime.AddMinutes(1)) with
                {
                    ProfileId = "local",
                    TransportId = TerminalTransportIds.Pty,
                },
            ],
        };
        TerminalCommandSuggestionService service = new();

        IReadOnlyList<TerminalCommandSuggestion> suggestions = service.GetSuggestions(
            new TerminalCommandSuggestionRequest(document)
            {
                Query = "deploy",
                ProfileId = "prod",
                TransportId = TerminalTransportIds.Ssh,
                Limit = 10,
            });

        TerminalCommandSuggestion suggestion = Assert.Single(suggestions);
        Assert.Equal("deploy prod", suggestion.CommandLine);
    }

    [Fact(
        Skip = "macOS/xUnit v3 intermittently hangs JSON file-store roundtrips that use the shared atomic file writer in a multi-test process.",
        SkipType = typeof(TestPlatformConditions),
        SkipWhen = nameof(TestPlatformConditions.IsMacOS))]
    public async Task JsonFileStore_SaveThenLoad_RoundTripsDocument()
    {
        string filePath = Path.Combine(Path.GetTempPath(), "royalterminal-tests", Guid.NewGuid() + ".history.json");

        try
        {
            JsonFileTerminalCommandHistoryStore store = new(filePath);
            TerminalCommandHistoryDocument document = new()
            {
                Entries =
                [
                    CreateEntry("local", "echo local", new DateTimeOffset(2026, 6, 30, 10, 0, 0, TimeSpan.Zero)),
                ],
            };

            await store.SaveAsync(document);
            TerminalCommandHistoryDocument restored = await store.LoadAsync();

            TerminalCommandHistoryEntry entry = Assert.Single(restored.Entries);
            Assert.Equal("local", entry.Id);
            Assert.Equal("echo local", entry.CommandLine);
        }
        finally
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
    }

    private static TerminalCommandHistoryEntry CreateEntry(string id, string commandLine, DateTimeOffset startedAtUtc)
    {
        return new TerminalCommandHistoryEntry
        {
            Id = id,
            CommandLine = commandLine,
            StartedAtUtc = startedAtUtc,
        };
    }
}
