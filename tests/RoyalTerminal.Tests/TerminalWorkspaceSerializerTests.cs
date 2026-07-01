// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class TerminalWorkspaceSerializerTests
{
    [Fact]
    public async Task Serializer_RoundTripsDocument_AndAssignsSelections()
    {
        // Reference decision: workspace restore persists durable launch/layout metadata and
        // stable identifiers; terminal runtime buffer replay remains outside this contract.
        TerminalWorkspaceDocument document = new()
        {
            Windows =
            [
                new TerminalWorkspaceWindow
                {
                    Id = " main ",
                    Title = " Main Window ",
                    WidthPixels = 0,
                    HeightPixels = -10,
                    TabsInTitleBar = true,
                    Tabs =
                    [
                        new TerminalWorkspaceTab
                        {
                            Id = " shell ",
                            ProfileId = " dev-profile ",
                            Title = " Dev Shell ",
                            WorkingDirectory = " /Users/alice/src ",
                            TransportId = "SSH",
                            TransportProfileId = " ssh/dev ",
                            RenderMode = "ghostty",
                            RootPane = new TerminalWorkspacePane
                            {
                                Id = " root ",
                                Title = " Shell Pane ",
                                TransportId = "pty",
                            },
                        },
                    ],
                },
            ],
        };

        await using MemoryStream stream = new();
        await TerminalWorkspaceSerializer.SaveAsync(document, stream);
        stream.Position = 0;

        TerminalWorkspaceDocument restored = await TerminalWorkspaceSerializer.LoadAsync(stream);

        Assert.Equal(TerminalWorkspaceDocument.CurrentFormatVersion, restored.FormatVersion);
        Assert.Equal("main", restored.SelectedWindowId);
        TerminalWorkspaceWindow window = Assert.Single(restored.Windows);
        Assert.Equal("main", window.Id);
        Assert.Equal("Main Window", window.Title);
        Assert.Equal("shell", window.SelectedTabId);
        Assert.Equal(1, window.WidthPixels);
        Assert.Equal(1, window.HeightPixels);
        Assert.True(window.TabsInTitleBar);

        TerminalWorkspaceTab tab = Assert.Single(window.Tabs);
        Assert.Equal("shell", tab.Id);
        Assert.Equal("dev-profile", tab.ProfileId);
        Assert.Equal("Dev Shell", tab.Title);
        Assert.Equal("/Users/alice/src", tab.WorkingDirectory);
        Assert.Equal(TerminalTransportIds.Ssh, tab.TransportId);
        Assert.Equal("ssh/dev", tab.TransportProfileId);
        Assert.Equal(TerminalWorkspaceRenderModes.Ghostty, tab.RenderMode);
        Assert.Equal("root", tab.RootPane.Id);
        Assert.Equal("Shell Pane", tab.RootPane.Title);
        Assert.Equal(TerminalTransportIds.Pty, tab.RootPane.TransportId);
    }

    [Fact]
    public void Serializer_RoundTripsPaneSplitModel_AndNormalizesFields()
    {
        TerminalWorkspaceDocument document = new()
        {
            SelectedWindowId = "workspace-main",
            Windows =
            [
                new TerminalWorkspaceWindow
                {
                    Id = "workspace-main",
                    SelectedTabId = "split-tab",
                    Tabs =
                    [
                        new TerminalWorkspaceTab
                        {
                            Id = "split-tab",
                            ProfileId = "local",
                            TransportId = TerminalTransportIds.Pty,
                            RenderMode = "invalid-render-mode",
                            RootPane = new TerminalWorkspacePane
                            {
                                Id = "root",
                                Split = new TerminalWorkspacePaneSplit
                                {
                                    Orientation = "vertical",
                                    Ratio = 2.0,
                                    FirstPane = new TerminalWorkspacePane
                                    {
                                        Id = "left",
                                        ProfileId = " local ",
                                        WorkingDirectory = " /tmp ",
                                    },
                                    SecondPane = new TerminalWorkspacePane
                                    {
                                        Id = "right",
                                        ProfileId = " ssh-profile ",
                                        TransportId = "SSH",
                                        TransportProfileId = " ssh/prod ",
                                    },
                                },
                            },
                        },
                    ],
                },
            ],
        };

        TerminalWorkspaceDocument restored = TerminalWorkspaceSerializer.FromJson(
            TerminalWorkspaceSerializer.ToJson(document));

        TerminalWorkspaceTab tab = restored.Windows[0].Tabs[0];
        Assert.Equal(TerminalWorkspaceRenderModes.Default, tab.RenderMode);
        Assert.NotNull(tab.RootPane.Split);
        TerminalWorkspacePaneSplit split = tab.RootPane.Split!;
        Assert.Equal(TerminalWorkspacePaneSplitOrientations.Vertical, split.Orientation);
        Assert.Equal(0.95, split.Ratio);
        Assert.Equal("left", split.FirstPane.Id);
        Assert.Equal("local", split.FirstPane.ProfileId);
        Assert.Equal("/tmp", split.FirstPane.WorkingDirectory);
        Assert.Equal("right", split.SecondPane.Id);
        Assert.Equal("ssh-profile", split.SecondPane.ProfileId);
        Assert.Equal(TerminalTransportIds.Ssh, split.SecondPane.TransportId);
        Assert.Equal("ssh/prod", split.SecondPane.TransportProfileId);
    }

    [Fact]
    public async Task Serializer_LoadAsync_RejectsDuplicateTabIds()
    {
        const string json = """
                            {
                              "formatVersion": 1,
                              "windows": [
                                {
                                  "id": "main",
                                  "tabs": [
                                    { "id": "dup", "profileId": "local" },
                                    { "id": "DUP", "profileId": "ssh" }
                                  ]
                                }
                              ]
                            }
                            """;

        await using MemoryStream stream = new(System.Text.Encoding.UTF8.GetBytes(json));
        await Assert.ThrowsAsync<InvalidDataException>(
            async () => await TerminalWorkspaceSerializer.LoadAsync(stream));
    }

    [Fact]
    public void Serializer_FromJson_RejectsMissingSelectedTab()
    {
        const string json = """
                            {
                              "formatVersion": 1,
                              "windows": [
                                {
                                  "id": "main",
                                  "selectedTabId": "missing",
                                  "tabs": [
                                    { "id": "local", "profileId": "default" }
                                  ]
                                }
                              ]
                            }
                            """;

        Assert.Throws<InvalidDataException>(() => TerminalWorkspaceSerializer.FromJson(json));
    }

    [Fact]
    public void Serializer_FromJson_RejectsUnsupportedTransportId()
    {
        const string json = """
                            {
                              "formatVersion": 1,
                              "windows": [
                                {
                                  "id": "main",
                                  "tabs": [
                                    {
                                      "id": "local",
                                      "profileId": "default",
                                      "transportId": "not-a-transport"
                                    }
                                  ]
                                }
                              ]
                            }
                            """;

        Assert.Throws<InvalidDataException>(() => TerminalWorkspaceSerializer.FromJson(json));
    }

    [Fact]
    public async Task JsonFileStore_LoadMissingFile_ReturnsEmptyDocument()
    {
        string filePath = Path.Combine(Path.GetTempPath(), "royalterminal-tests", Guid.NewGuid() + ".workspace.json");
        JsonFileTerminalWorkspaceStore store = new(filePath);

        TerminalWorkspaceDocument restored = await store.LoadAsync();

        Assert.Equal(TerminalWorkspaceDocument.CurrentFormatVersion, restored.FormatVersion);
        Assert.Null(restored.SelectedWindowId);
        Assert.Empty(restored.Windows);
    }

    [Fact]
    public void StoreFactory_DefaultPath_LivesNextToSessionProfiles()
    {
        string profilePath = TerminalSessionProfileStoreFactory.GetDefaultFilePath();
        string workspacePath = TerminalWorkspaceStoreFactory.GetDefaultFilePath();

        Assert.Equal(Path.GetDirectoryName(profilePath), Path.GetDirectoryName(workspacePath));
        Assert.Equal("workspace.json", Path.GetFileName(workspacePath));

        string customPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".workspace.json");
        JsonFileTerminalWorkspaceStore store = Assert.IsType<JsonFileTerminalWorkspaceStore>(
            TerminalWorkspaceStoreFactory.CreateDefault(customPath));
        Assert.Equal(customPath, store.FilePath);
    }

    [Fact(
        Skip = "macOS/xUnit v3 intermittently hangs JSON file-store roundtrips that use the shared atomic file writer in a multi-test process.",
        SkipType = typeof(TestPlatformConditions),
        SkipWhen = nameof(TestPlatformConditions.IsMacOS))]
    public async Task JsonFileStore_SaveThenLoad_RoundTripsDocument()
    {
        string filePath = Path.Combine(Path.GetTempPath(), "royalterminal-tests", Guid.NewGuid() + ".workspace.json");

        try
        {
            JsonFileTerminalWorkspaceStore store = new(filePath);
            TerminalWorkspaceDocument document = new()
            {
                Windows =
                [
                    new TerminalWorkspaceWindow
                    {
                        Id = "main",
                        Tabs =
                        [
                            new TerminalWorkspaceTab
                            {
                                Id = "local",
                                ProfileId = "default",
                                TransportId = TerminalTransportIds.Pty,
                            },
                        ],
                    },
                ],
            };

            await store.SaveAsync(document);
            TerminalWorkspaceDocument restored = await store.LoadAsync();

            TerminalWorkspaceWindow window = Assert.Single(restored.Windows);
            TerminalWorkspaceTab tab = Assert.Single(window.Tabs);
            Assert.Equal("main", restored.SelectedWindowId);
            Assert.Equal("local", window.SelectedTabId);
            Assert.Equal("default", tab.ProfileId);
        }
        finally
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
    }
}
