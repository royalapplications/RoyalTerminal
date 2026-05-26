// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class TerminalSessionProfileSerializerTests
{
    [Fact]
    public async Task Serializer_RoundTripsDocument_AndAssignsDefaultProfile()
    {
        TerminalSessionProfilesDocument document = new()
        {
            Profiles =
            [
                new TerminalSessionProfile
                {
                    Id = "dev-ssh",
                    DisplayName = "Dev SSH",
                    Layout = new TerminalSessionLayoutSettings
                    {
                        Columns = 132,
                        Rows = 43,
                        WidthPixels = 1320,
                        HeightPixels = 860,
                    },
                    Transport = new TerminalSessionTransportProfile
                    {
                        TransportId = TerminalTransportIds.Ssh,
                        Ssh = new TerminalSessionSshSettings
                        {
                            Host = "example.com",
                            Port = 22,
                            Username = "alice",
                            RequestPty = true,
                            TerminalType = "xterm-256color",
                            InitialCommand = "uname -a",
                            ExpectedHostKeyFingerprintSha256 = "SHA256:test",
                            Environment = new Dictionary<string, string>(StringComparer.Ordinal)
                            {
                                ["LANG"] = "en_US.UTF-8",
                            },
                            Authentication = new TerminalSessionSshAuthenticationSettings
                            {
                                UsePassword = true,
                                PasswordSecretId = "ssh/dev/password",
                                PrivateKeySecretIds = ["ssh/dev/key"],
                            },
                        },
                    },
                },
            ],
        };

        await using MemoryStream stream = new();
        await TerminalSessionProfileSerializer.SaveAsync(document, stream);
        stream.Position = 0;

        TerminalSessionProfilesDocument restored = await TerminalSessionProfileSerializer.LoadAsync(stream);

        Assert.Equal(TerminalSessionProfilesDocument.CurrentFormatVersion, restored.FormatVersion);
        Assert.Equal("dev-ssh", restored.DefaultProfileId);
        Assert.Single(restored.Profiles);

        TerminalSessionProfile profile = restored.Profiles[0];
        Assert.Equal("dev-ssh", profile.Id);
        Assert.Equal(TerminalTransportIds.Ssh, profile.Transport.TransportId);
        Assert.True(profile.Behavior.SixelGraphicsEnabled);
        Assert.Equal("example.com", profile.Transport.Ssh.Host);
        Assert.Equal("alice", profile.Transport.Ssh.Username);
        Assert.True(profile.Transport.Ssh.Authentication.UsePassword);
        Assert.Equal("ssh/dev/password", profile.Transport.Ssh.Authentication.PasswordSecretId);
        Assert.Single(profile.Transport.Ssh.Authentication.PrivateKeySecretIds);
    }

    [Fact]
    public void Serializer_RoundTripsReflowOnResizeBehavior()
    {
        TerminalSessionProfilesDocument document = new()
        {
            Profiles =
            [
                new TerminalSessionProfile
                {
                    Id = "fixed-width",
                    DisplayName = "Fixed Width",
                    Behavior = new TerminalSessionBehaviorSettings
                    {
                        ReflowOnResize = false,
                        SixelGraphicsEnabled = false,
                    },
                },
            ],
        };

        string json = TerminalSessionProfileSerializer.ToJson(document);
        TerminalSessionProfilesDocument restored = TerminalSessionProfileSerializer.FromJson(json);

        TerminalSessionProfile profile = Assert.Single(restored.Profiles);
        Assert.False(profile.Behavior.ReflowOnResize);
        Assert.False(profile.Behavior.SixelGraphicsEnabled);
    }

    [Fact]
    public void Serializer_RoundTripsFileFontAppearance()
    {
        TerminalSessionProfilesDocument document = new()
        {
            Profiles =
            [
                new TerminalSessionProfile
                {
                    Id = "file-font",
                    DisplayName = "File Font",
                    Appearance = new TerminalSessionAppearanceSettings
                    {
                        FontSource = TerminalFontSource.File,
                        FontFamilyName = "Custom Terminal",
                        FontFilePath = "/fonts/custom-terminal.otf",
                        FontSize = 16,
                    },
                },
            ],
        };

        string json = TerminalSessionProfileSerializer.ToJson(document);
        TerminalSessionProfilesDocument restored = TerminalSessionProfileSerializer.FromJson(json);

        TerminalSessionProfile profile = Assert.Single(restored.Profiles);
        Assert.Equal(TerminalFontSource.File, profile.Appearance.FontSource);
        Assert.Equal("Custom Terminal", profile.Appearance.FontFamilyName);
        Assert.Equal("/fonts/custom-terminal.otf", profile.Appearance.FontFilePath);
        Assert.Equal(16, profile.Appearance.FontSize);
    }

    [Fact]
    public void Serializer_RoundTripsFontRenderingAppearance()
    {
        TerminalSessionProfilesDocument document = new()
        {
            Profiles =
            [
                new TerminalSessionProfile
                {
                    Id = "font-rendering",
                    DisplayName = "Font Rendering",
                    Appearance = new TerminalSessionAppearanceSettings
                    {
                        FontRendering = new TerminalFontRenderingSettings
                        {
                            SubpixelPositioning = false,
                            Edging = TerminalFontEdging.Alias,
                            Hinting = TerminalFontHinting.Full,
                            BaselineSnap = false,
                            EmbeddedBitmaps = true,
                            Embolden = true,
                            ForceAutoHinting = true,
                            LinearMetrics = true,
                        },
                    },
                },
            ],
        };

        string json = TerminalSessionProfileSerializer.ToJson(document);
        TerminalSessionProfilesDocument restored = TerminalSessionProfileSerializer.FromJson(json);

        TerminalSessionProfile profile = Assert.Single(restored.Profiles);
        TerminalFontRenderingSettings settings = profile.Appearance.FontRendering;
        Assert.False(settings.SubpixelPositioning);
        Assert.Equal(TerminalFontEdging.Alias, settings.Edging);
        Assert.Equal(TerminalFontHinting.Full, settings.Hinting);
        Assert.False(settings.BaselineSnap);
        Assert.True(settings.EmbeddedBitmaps);
        Assert.True(settings.Embolden);
        Assert.True(settings.ForceAutoHinting);
        Assert.True(settings.LinearMetrics);
    }

    [Fact]
    public void Serializer_NormalizesInvalidFontRenderingAppearance()
    {
        TerminalSessionProfilesDocument document = new()
        {
            Profiles =
            [
                new TerminalSessionProfile
                {
                    Id = "font-rendering",
                    DisplayName = "Font Rendering",
                    Appearance = new TerminalSessionAppearanceSettings
                    {
                        FontRendering = new TerminalFontRenderingSettings
                        {
                            Edging = (TerminalFontEdging)999,
                            Hinting = (TerminalFontHinting)999,
                        },
                    },
                },
            ],
        };

        TerminalSessionProfilesDocument restored = TerminalSessionProfileSerializer.FromJson(
            TerminalSessionProfileSerializer.ToJson(document));

        TerminalSessionProfile profile = Assert.Single(restored.Profiles);
        Assert.Equal(TerminalFontRenderingSettings.Default, profile.Appearance.FontRendering);
    }

    [Fact]
    public void Serializer_RoundTripsTextHighlightRules_AndNormalizesColors()
    {
        TerminalSessionProfilesDocument document = new()
        {
            Profiles =
            [
                new TerminalSessionProfile
                {
                    Id = "highlighting",
                    DisplayName = "Highlighting",
                    Appearance = new TerminalSessionAppearanceSettings
                    {
                        TextHighlightingMode = TerminalTextHighlightingMode.Realtime,
                        TextHighlightRules =
                        [
                            new TerminalSessionTextHighlightRule
                            {
                                Name = "Errors",
                                Pattern = "ERROR|WARN",
                                ForegroundColor = "#ff0000",
                                BackgroundColor = "#40101010",
                                DarkForegroundColor = "#00ff00",
                                DarkBackgroundColor = "202020",
                            },
                        ],
                    },
                },
            ],
        };

        string json = TerminalSessionProfileSerializer.ToJson(document);
        TerminalSessionProfilesDocument restored = TerminalSessionProfileSerializer.FromJson(json);

        TerminalSessionProfile profile = Assert.Single(restored.Profiles);
        Assert.Equal(TerminalTextHighlightingMode.Realtime, profile.Appearance.TextHighlightingMode);
        TerminalSessionTextHighlightRule rule = Assert.Single(profile.Appearance.TextHighlightRules);
        Assert.Equal("Errors", rule.Name);
        Assert.Equal("ERROR|WARN", rule.Pattern);
        Assert.Equal("#FFFF0000", rule.ForegroundColor);
        Assert.Equal("#40101010", rule.BackgroundColor);
        Assert.Equal("#FF00FF00", rule.DarkForegroundColor);
        Assert.Equal("#FF202020", rule.DarkBackgroundColor);
    }

    [Fact]
    public void Serializer_NormalizesInvalidTextHighlightingMode_AndSkipsNullRules()
    {
        TerminalSessionProfilesDocument document = new()
        {
            Profiles =
            [
                new TerminalSessionProfile
                {
                    Id = "highlighting",
                    DisplayName = "Highlighting",
                    Appearance = new TerminalSessionAppearanceSettings
                    {
                        TextHighlightingMode = (TerminalTextHighlightingMode)999,
                        TextHighlightRules = [null!],
                    },
                },
            ],
        };

        TerminalSessionProfilesDocument restored = TerminalSessionProfileSerializer.FromJson(
            TerminalSessionProfileSerializer.ToJson(document));

        TerminalSessionProfile profile = Assert.Single(restored.Profiles);
        Assert.Equal(TerminalTextHighlightingMode.Static, profile.Appearance.TextHighlightingMode);
        Assert.Empty(profile.Appearance.TextHighlightRules);
    }

    [Fact]
    public void Serializer_FileFontWithoutPath_NormalizesToSystemFont()
    {
        TerminalSessionProfilesDocument document = new()
        {
            Profiles =
            [
                new TerminalSessionProfile
                {
                    Id = "missing-file-font",
                    DisplayName = "Missing File Font",
                    Appearance = new TerminalSessionAppearanceSettings
                    {
                        FontSource = TerminalFontSource.File,
                        FontFamilyName = "Custom Terminal",
                        FontFilePath = " ",
                    },
                },
            ],
        };

        TerminalSessionProfilesDocument restored = TerminalSessionProfileSerializer.FromJson(
            TerminalSessionProfileSerializer.ToJson(document));

        TerminalSessionProfile profile = Assert.Single(restored.Profiles);
        Assert.Equal(TerminalFontSource.System, profile.Appearance.FontSource);
        Assert.Null(profile.Appearance.FontFilePath);
    }

    [Fact]
    public async Task Serializer_LoadAsync_RejectsDuplicateProfileIds()
    {
        const string json = """
                            {
                              "formatVersion": 1,
                              "profiles": [
                                { "id": "dup", "displayName": "One" },
                                { "id": "DUP", "displayName": "Two" }
                              ]
                            }
                            """;

        await using MemoryStream stream = new(System.Text.Encoding.UTF8.GetBytes(json));
        await Assert.ThrowsAsync<InvalidDataException>(
            async () => await TerminalSessionProfileSerializer.LoadAsync(stream));
    }

    [Fact]
    public void Mapper_MapsSshProfileToTransportOptions_AndBack()
    {
        TerminalSessionProfile profile = new()
        {
            Id = "ssh-prod",
            DisplayName = "SSH Prod",
            Layout = new TerminalSessionLayoutSettings
            {
                Columns = 100,
                Rows = 30,
                WidthPixels = 1000,
                HeightPixels = 720,
            },
            Transport = new TerminalSessionTransportProfile
            {
                TransportId = TerminalTransportIds.Ssh,
                Ssh = new TerminalSessionSshSettings
                {
                    Host = "prod.example.com",
                    Port = 2222,
                    Username = "root",
                    RequestPty = false,
                    TerminalType = "xterm",
                    InitialCommand = "echo ready",
                    ExpectedHostKeyFingerprintSha256 = "SHA256:abc",
                    Environment = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["TERM"] = "xterm",
                    },
                    Authentication = new TerminalSessionSshAuthenticationSettings
                    {
                        UsePassword = false,
                        PasswordSecretId = null,
                        PrivateKeySecretIds = ["ssh/prod/key"],
                        UseAgent = true,
                    },
                },
            },
        };

        ITerminalTransportOptions mapped = TerminalSessionProfileMapper.ToTransportOptions(profile);
        SshTransportOptions ssh = Assert.IsType<SshTransportOptions>(mapped);

        Assert.Equal("prod.example.com", ssh.Endpoint.Host);
        Assert.Equal(2222, ssh.Endpoint.Port);
        Assert.Equal("root", ssh.Endpoint.Username);
        Assert.False(ssh.RequestPty);
        Assert.Equal("xterm", ssh.TerminalType);
        Assert.Equal("echo ready", ssh.InitialCommand);
        Assert.Equal("SHA256:abc", ssh.ExpectedHostKeyFingerprintSha256);
        Assert.True(ssh.Authentication.UseAgent);
        Assert.Single(ssh.Authentication.PrivateKeySecretIds);

        TerminalSessionProfile roundTripped = TerminalSessionProfileMapper.FromTransportOptions(
            "from-options",
            "From Options",
            ssh);
        Assert.Equal("from-options", roundTripped.Id);
        Assert.Equal(TerminalTransportIds.Ssh, roundTripped.Transport.TransportId);
        Assert.Equal("prod.example.com", roundTripped.Transport.Ssh.Host);
        Assert.Equal(2222, roundTripped.Transport.Ssh.Port);
        Assert.Equal("root", roundTripped.Transport.Ssh.Username);
    }

    [Fact]
    public void Mapper_MapsRawTelnetAndSerialProfiles()
    {
        TerminalSessionDimensions dimensions = new(80, 24, 640, 480);

        TerminalSessionProfile rawProfile = new()
        {
            Id = "raw",
            DisplayName = "Raw",
            Transport = new TerminalSessionTransportProfile
            {
                TransportId = TerminalTransportIds.RawTcp,
                RawTcp = new TerminalSessionRawTcpSettings
                {
                    Host = "127.0.0.1",
                    Port = 2323,
                },
            },
        };
        RawTcpTransportOptions raw = Assert.IsType<RawTcpTransportOptions>(
            TerminalSessionProfileMapper.ToTransportOptions(rawProfile));
        Assert.Equal("127.0.0.1", raw.Host);
        Assert.Equal(2323, raw.Port);

        TerminalSessionProfile telnetProfile = new()
        {
            Id = "telnet",
            DisplayName = "Telnet",
            Transport = new TerminalSessionTransportProfile
            {
                TransportId = TerminalTransportIds.Telnet,
                Telnet = new TerminalSessionTelnetSettings
                {
                    Host = "localhost",
                    Port = 23,
                    TerminalType = "xterm",
                },
            },
        };
        TelnetTransportOptions telnet = Assert.IsType<TelnetTransportOptions>(
            TerminalSessionProfileMapper.ToTransportOptions(telnetProfile));
        Assert.Equal("localhost", telnet.Host);
        Assert.Equal(23, telnet.Port);

        TerminalSessionProfile serialProfile = new()
        {
            Id = "serial",
            DisplayName = "Serial",
            Transport = new TerminalSessionTransportProfile
            {
                TransportId = TerminalTransportIds.Serial,
                Serial = new TerminalSessionSerialSettings
                {
                    PortName = "COM1",
                    BaudRate = 115200,
                    DataBits = 8,
                    Parity = TerminalSerialParity.None,
                    StopBits = TerminalSerialStopBits.One,
                    Handshake = TerminalSerialHandshake.None,
                },
            },
        };
        SerialTransportOptions serial = Assert.IsType<SerialTransportOptions>(
            TerminalSessionProfileMapper.ToTransportOptions(serialProfile));
        Assert.Equal("COM1", serial.PortName);
        Assert.Equal(115200, serial.BaudRate);

        TerminalSessionProfile mappedBack = TerminalSessionProfileMapper.FromTransportOptions("mapped", "Mapped", new RawTcpTransportOptions("example.com", 4000, dimensions));
        Assert.Equal(TerminalTransportIds.RawTcp, mappedBack.Transport.TransportId);
        Assert.Equal("example.com", mappedBack.Transport.RawTcp.Host);
    }

    [Fact]
    public void Mapper_MapsSshAdvancedSettings()
    {
        TerminalSessionProfile profile = new()
        {
            Id = "ssh-advanced",
            DisplayName = "SSH Advanced",
            Transport = new TerminalSessionTransportProfile
            {
                TransportId = TerminalTransportIds.Ssh,
                Ssh = new TerminalSessionSshSettings
                {
                    Host = "advanced.example.com",
                    Port = 22,
                    Username = "alice",
                    Proxy = new SshProxyOptions(
                        Type: SshProxyType.Socks5,
                        Host: "proxy.example.com",
                        Port: 1080,
                        Username: "proxy-user",
                        Password: "proxy-pass"),
                    PortForwardings =
                    [
                        new SshPortForwardOptions(
                            Mode: SshPortForwardMode.Local,
                            BindAddress: "127.0.0.1",
                            SourcePort: 15432,
                            DestinationHost: "db.internal",
                            DestinationPort: 5432),
                    ],
                    X11 = new SshX11Options(
                        Enabled: true,
                        Display: ":0"),
                    Policy = new SshPolicyOptions(
                        KeepAliveIntervalSeconds: 20,
                        ConnectTimeoutSeconds: 12),
                },
            },
        };

        SshTransportOptions options = Assert.IsType<SshTransportOptions>(
            TerminalSessionProfileMapper.ToTransportOptions(profile));

        Assert.NotNull(options.Proxy);
        Assert.Equal(SshProxyType.Socks5, options.Proxy!.Type);
        Assert.Single(options.PortForwardings);
        Assert.Equal(SshPortForwardMode.Local, options.PortForwardings[0].Mode);
        Assert.NotNull(options.X11);
        Assert.True(options.X11!.Enabled);
        Assert.Equal(":0", options.X11.Display);
        Assert.Equal(20, options.Policy.KeepAliveIntervalSeconds);
        Assert.Equal(12, options.Policy.ConnectTimeoutSeconds);
    }

    [Fact(
        Skip = "macOS/xUnit v3 intermittently hangs this file-store roundtrip when it runs in a multi-test process; JSON file persistence remains covered by SshCredentialProvidersTests.",
        SkipType = typeof(TestPlatformConditions),
        SkipWhen = nameof(TestPlatformConditions.IsMacOS))]
    public async Task JsonFileStore_SaveThenLoad_RoundTripsDocument()
    {
        string filePath = Path.Combine(Path.GetTempPath(), "royalterminal-tests", Guid.NewGuid() + ".profiles.json");

        try
        {
            JsonFileTerminalSessionProfileStore store = new(filePath);

            TerminalSessionProfilesDocument document = new()
            {
                Profiles =
                [
                    new TerminalSessionProfile
                    {
                        Id = "local",
                        DisplayName = "Local",
                        Transport = new TerminalSessionTransportProfile
                        {
                            TransportId = TerminalTransportIds.Pty,
                            Pty = new TerminalSessionPtySettings
                            {
                                ShellPath = "/bin/bash",
                            },
                        },
                    },
                ],
            };

            await store.SaveAsync(document);
            TerminalSessionProfilesDocument restored = await store.LoadAsync();

            Assert.Single(restored.Profiles);
            Assert.Equal("local", restored.Profiles[0].Id);
            Assert.Equal(TerminalTransportIds.Pty, restored.Profiles[0].Transport.TransportId);
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
