// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Terminal - Session profile serialization and persistence helpers.

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RoyalTerminal.Terminal;

/// <summary>
/// Serialization helpers for <see cref="TerminalSessionProfilesDocument"/>.
/// </summary>
public static class TerminalSessionProfileSerializer
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters =
        {
            new JsonStringEnumConverter(),
        },
    };

    /// <summary>
    /// Saves a profile document to a stream.
    /// </summary>
    public static ValueTask SaveAsync(
        TerminalSessionProfilesDocument document,
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(stream);
        cancellationToken.ThrowIfCancellationRequested();

        TerminalSessionProfilesDocument normalized = NormalizeAndValidate(document);
        return new ValueTask(JsonSerializer.SerializeAsync(stream, normalized, s_jsonOptions, cancellationToken));
    }

    /// <summary>
    /// Loads a profile document from a stream.
    /// </summary>
    public static async ValueTask<TerminalSessionProfilesDocument> LoadAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        cancellationToken.ThrowIfCancellationRequested();

        TerminalSessionProfilesDocument? document = await JsonSerializer
            .DeserializeAsync<TerminalSessionProfilesDocument>(stream, s_jsonOptions, cancellationToken)
            .ConfigureAwait(false);
        if (document is null)
        {
            throw new InvalidDataException("Profile document is empty or malformed.");
        }

        return NormalizeAndValidate(document);
    }

    /// <summary>
    /// Saves a profile document to a file.
    /// </summary>
    public static async ValueTask SaveToFileAsync(
        TerminalSessionProfilesDocument document,
        string filePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        cancellationToken.ThrowIfCancellationRequested();

        string directoryPath = Path.GetDirectoryName(filePath) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        await using FileStream stream = File.Create(filePath);
        await SaveAsync(document, stream, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Loads a profile document from a file.
    /// </summary>
    public static async ValueTask<TerminalSessionProfilesDocument> LoadFromFileAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        cancellationToken.ThrowIfCancellationRequested();

        await using FileStream stream = File.OpenRead(filePath);
        return await LoadAsync(stream, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Serializes a profile document to JSON text.
    /// </summary>
    public static string ToJson(TerminalSessionProfilesDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        TerminalSessionProfilesDocument normalized = NormalizeAndValidate(document);
        return JsonSerializer.Serialize(normalized, s_jsonOptions);
    }

    /// <summary>
    /// Deserializes a profile document from JSON text.
    /// </summary>
    public static TerminalSessionProfilesDocument FromJson(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        TerminalSessionProfilesDocument? document = JsonSerializer.Deserialize<TerminalSessionProfilesDocument>(json, s_jsonOptions);
        if (document is null)
        {
            throw new InvalidDataException("Profile JSON is empty or malformed.");
        }

        return NormalizeAndValidate(document);
    }

    private static TerminalSessionProfilesDocument NormalizeAndValidate(TerminalSessionProfilesDocument document)
    {
        if (document.FormatVersion <= 0 ||
            document.FormatVersion > TerminalSessionProfilesDocument.CurrentFormatVersion)
        {
            throw new InvalidDataException(
                $"Unsupported profile format version '{document.FormatVersion}'.");
        }

        List<TerminalSessionProfile> normalizedProfiles = new(document.Profiles.Count);
        HashSet<string> profileIds = new(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < document.Profiles.Count; i++)
        {
            TerminalSessionProfile source = document.Profiles[i];
            string id = NormalizeRequired(source.Id, $"Profile at index {i} is missing a valid id.");
            if (!profileIds.Add(id))
            {
                throw new InvalidDataException($"Profile id '{id}' is duplicated.");
            }

            string displayName = NormalizeOptional(source.DisplayName) ?? id;
            TerminalSessionLayoutSettings layout = NormalizeLayout(source.Layout);
            TerminalSessionAppearanceSettings appearance = NormalizeAppearance(source.Appearance);
            TerminalSessionBehaviorSettings behavior = NormalizeBehavior(source.Behavior);
            TerminalSessionTransportProfile transport = NormalizeTransport(source.Transport, i);
            TerminalSessionLoggingSettings logging = NormalizeLogging(source.Logging);
            TerminalSessionProxySettings proxy = NormalizeProxy(source.Proxy);

            normalizedProfiles.Add(source with
            {
                Id = id,
                DisplayName = displayName,
                Layout = layout,
                Appearance = appearance,
                Behavior = behavior,
                Transport = transport,
                Logging = logging,
                Proxy = proxy,
            });
        }

        string? defaultProfileId = NormalizeOptional(document.DefaultProfileId);
        if (defaultProfileId is not null && !profileIds.Contains(defaultProfileId))
        {
            throw new InvalidDataException(
                $"Default profile '{defaultProfileId}' does not exist.");
        }

        if (defaultProfileId is null && normalizedProfiles.Count > 0)
        {
            defaultProfileId = normalizedProfiles[0].Id;
        }

        return document with
        {
            DefaultProfileId = defaultProfileId,
            Profiles = normalizedProfiles,
        };
    }

    private static TerminalSessionLayoutSettings NormalizeLayout(TerminalSessionLayoutSettings layout)
    {
        return layout with
        {
            Columns = Math.Max(1, layout.Columns),
            Rows = Math.Max(1, layout.Rows),
            WidthPixels = Math.Max(1, layout.WidthPixels),
            HeightPixels = Math.Max(1, layout.HeightPixels),
            ScrollbackLimit = Math.Max(0, layout.ScrollbackLimit),
        };
    }

    private static TerminalSessionAppearanceSettings NormalizeAppearance(TerminalSessionAppearanceSettings appearance)
    {
        string? fontFilePath = NormalizeOptional(appearance.FontFilePath);
        TerminalFontSource fontSource = appearance.FontSource == TerminalFontSource.File && fontFilePath is not null
            ? TerminalFontSource.File
            : TerminalFontSource.System;

        return appearance with
        {
            FontSource = fontSource,
            FontFamilyName = NormalizeOptional(appearance.FontFamilyName) ?? TerminalSessionProfileDefaults.DefaultMonoFont,
            FontFilePath = fontSource == TerminalFontSource.File ? fontFilePath : null,
            FontSize = appearance.FontSize > 0 ? appearance.FontSize : 14.0,
            FontRendering = NormalizeFontRendering(appearance.FontRendering),
            TextHighlightingMode = NormalizeTextHighlightingMode(appearance.TextHighlightingMode),
            TextHighlightRules = NormalizeTextHighlightRules(appearance.TextHighlightRules),
        };
    }

    private static TerminalFontRenderingSettings NormalizeFontRendering(TerminalFontRenderingSettings? settings)
    {
        return (settings ?? TerminalFontRenderingSettings.Default).Normalize();
    }

    private static TerminalTextHighlightingMode NormalizeTextHighlightingMode(TerminalTextHighlightingMode mode)
    {
        return Enum.IsDefined(mode)
            ? mode
            : TerminalTextHighlightingMode.Static;
    }

    private static List<TerminalSessionTextHighlightRule> NormalizeTextHighlightRules(
        List<TerminalSessionTextHighlightRule>? rules)
    {
        if (rules is null || rules.Count == 0)
        {
            return [];
        }

        List<TerminalSessionTextHighlightRule> normalized = new(rules.Count);
        for (int i = 0; i < rules.Count; i++)
        {
            TerminalSessionTextHighlightRule? rule = rules[i];
            if (rule is null)
            {
                continue;
            }

            string? pattern = NormalizeOptional(rule.Pattern);
            if (pattern is null)
            {
                continue;
            }

            normalized.Add(rule with
            {
                Name = NormalizeOptional(rule.Name) ?? "Highlight Rule",
                Pattern = pattern,
                ForegroundColor = NormalizeColor(rule.ForegroundColor),
                BackgroundColor = NormalizeColor(rule.BackgroundColor),
                DarkForegroundColor = NormalizeColor(rule.DarkForegroundColor),
                DarkBackgroundColor = NormalizeColor(rule.DarkBackgroundColor),
            });
        }

        return normalized;
    }

    private static TerminalSessionBehaviorSettings NormalizeBehavior(TerminalSessionBehaviorSettings behavior)
    {
        return behavior with
        {
            PasteSafetyPolicy = NormalizePasteSafetyPolicy(behavior.PasteSafetyPolicy),
        };
    }

    private static TerminalSessionTransportProfile NormalizeTransport(
        TerminalSessionTransportProfile transport,
        int profileIndex)
    {
        string transportId = NormalizeTransportId(transport.TransportId);
        TerminalSessionPtySettings pty = NormalizePtySettings(transport.Pty);
        TerminalSessionPipeSettings pipe = NormalizePipeSettings(transport.Pipe);
        TerminalSessionSshSettings ssh = NormalizeSshSettings(transport.Ssh);
        TerminalSessionRawTcpSettings rawTcp = NormalizeRawTcpSettings(transport.RawTcp);
        TerminalSessionTelnetSettings telnet = NormalizeTelnetSettings(transport.Telnet);
        TerminalSessionSerialSettings serial = NormalizeSerialSettings(transport.Serial);

        if (string.Equals(transportId, TerminalTransportIds.Pipe, StringComparison.Ordinal) &&
            string.IsNullOrWhiteSpace(pipe.FileName))
        {
            throw new InvalidDataException($"Profile '{profileIndex}' uses Pipe transport but FileName is empty.");
        }

        if (string.Equals(transportId, TerminalTransportIds.Ssh, StringComparison.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(ssh.Host))
            {
                throw new InvalidDataException($"Profile '{profileIndex}' uses SSH transport but Host is empty.");
            }

            if (string.IsNullOrWhiteSpace(ssh.Username))
            {
                throw new InvalidDataException($"Profile '{profileIndex}' uses SSH transport but Username is empty.");
            }

            if (ssh.Port is <= 0 or > 65_535)
            {
                throw new InvalidDataException($"Profile '{profileIndex}' uses invalid SSH port '{ssh.Port}'.");
            }

            if (ssh.Proxy is { } proxy && proxy.Type != SshProxyType.None)
            {
                if (string.IsNullOrWhiteSpace(proxy.Host))
                {
                    throw new InvalidDataException($"Profile '{profileIndex}' uses SSH proxy but Proxy.Host is empty.");
                }

                if (proxy.Port is <= 0 or > 65_535)
                {
                    throw new InvalidDataException($"Profile '{profileIndex}' uses invalid SSH proxy port '{proxy.Port}'.");
                }
            }

            for (int i = 0; i < ssh.PortForwardings.Count; i++)
            {
                SshPortForwardOptions forward = ssh.PortForwardings[i];
                if (forward.SourcePort > 65_535)
                {
                    throw new InvalidDataException($"Profile '{profileIndex}' has SSH port forward with invalid source port '{forward.SourcePort}'.");
                }

                if (forward.Mode is SshPortForwardMode.Local or SshPortForwardMode.Remote)
                {
                    if (string.IsNullOrWhiteSpace(forward.DestinationHost))
                    {
                        throw new InvalidDataException($"Profile '{profileIndex}' has SSH port forward missing destination host.");
                    }

                    if (forward.DestinationPort is null or 0 || forward.DestinationPort > 65_535)
                    {
                        throw new InvalidDataException($"Profile '{profileIndex}' has SSH port forward with invalid destination port.");
                    }
                }
            }

            if (ssh.X11 is { Enabled: true } x11 && string.IsNullOrWhiteSpace(x11.Display))
            {
                throw new InvalidDataException($"Profile '{profileIndex}' enables X11 forwarding but Display is empty.");
            }

            if (ssh.Policy.KeepAliveIntervalSeconds <= 0 || ssh.Policy.ConnectTimeoutSeconds <= 0)
            {
                throw new InvalidDataException($"Profile '{profileIndex}' has invalid SSH policy values.");
            }
        }

        if (string.Equals(transportId, TerminalTransportIds.RawTcp, StringComparison.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(rawTcp.Host))
            {
                throw new InvalidDataException($"Profile '{profileIndex}' uses Raw TCP transport but Host is empty.");
            }

            if (rawTcp.Port is <= 0 or > 65_535)
            {
                throw new InvalidDataException($"Profile '{profileIndex}' uses invalid Raw TCP port '{rawTcp.Port}'.");
            }
        }

        if (string.Equals(transportId, TerminalTransportIds.Telnet, StringComparison.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(telnet.Host))
            {
                throw new InvalidDataException($"Profile '{profileIndex}' uses Telnet transport but Host is empty.");
            }

            if (telnet.Port is <= 0 or > 65_535)
            {
                throw new InvalidDataException($"Profile '{profileIndex}' uses invalid Telnet port '{telnet.Port}'.");
            }
        }

        if (string.Equals(transportId, TerminalTransportIds.Serial, StringComparison.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(serial.PortName))
            {
                throw new InvalidDataException($"Profile '{profileIndex}' uses Serial transport but PortName is empty.");
            }

            if (serial.BaudRate <= 0)
            {
                throw new InvalidDataException($"Profile '{profileIndex}' uses invalid baud rate '{serial.BaudRate}'.");
            }

            if (serial.DataBits is < 5 or > 8)
            {
                throw new InvalidDataException($"Profile '{profileIndex}' uses invalid data bits '{serial.DataBits}'.");
            }
        }

        return transport with
        {
            TransportId = transportId,
            Pty = pty,
            Pipe = pipe,
            Ssh = ssh,
            RawTcp = rawTcp,
            Telnet = telnet,
            Serial = serial,
        };
    }

    private static TerminalSessionPtySettings NormalizePtySettings(TerminalSessionPtySettings settings)
    {
        return settings with
        {
            ShellPath = NormalizeOptional(settings.ShellPath),
            WorkingDirectory = NormalizeOptional(settings.WorkingDirectory),
            Arguments = NormalizeList(settings.Arguments),
            Environment = NormalizeDictionary(settings.Environment),
        };
    }

    private static TerminalSessionPipeSettings NormalizePipeSettings(TerminalSessionPipeSettings settings)
    {
        return settings with
        {
            FileName = NormalizeOptional(settings.FileName) ?? string.Empty,
            WorkingDirectory = NormalizeOptional(settings.WorkingDirectory),
            Arguments = NormalizeList(settings.Arguments),
            Environment = NormalizeDictionary(settings.Environment),
        };
    }

    private static TerminalSessionSshSettings NormalizeSshSettings(TerminalSessionSshSettings settings)
    {
        TerminalSessionSshAuthenticationSettings authentication = NormalizeSshAuthentication(settings.Authentication);
        SshProxyOptions? proxy = NormalizeSshProxy(settings.Proxy);
        List<SshPortForwardOptions> portForwardings = NormalizeSshPortForwardings(settings.PortForwardings);
        SshX11Options? x11 = NormalizeSshX11(settings.X11);
        SshPolicyOptions policy = NormalizeSshPolicy(settings.Policy);
        return settings with
        {
            Host = NormalizeOptional(settings.Host) ?? string.Empty,
            Username = NormalizeOptional(settings.Username) ?? string.Empty,
            Port = settings.Port,
            TerminalType = NormalizeOptional(settings.TerminalType) ?? "xterm-256color",
            InitialCommand = NormalizeOptional(settings.InitialCommand),
            ExpectedHostKeyFingerprintSha256 = NormalizeOptional(settings.ExpectedHostKeyFingerprintSha256),
            Environment = NormalizeDictionary(settings.Environment),
            Authentication = authentication,
            Proxy = proxy,
            PortForwardings = portForwardings,
            X11 = x11,
            Policy = policy,
        };
    }

    private static TerminalSessionSshAuthenticationSettings NormalizeSshAuthentication(
        TerminalSessionSshAuthenticationSettings authentication)
    {
        string? passwordSecretId = NormalizeOptional(authentication.PasswordSecretId);
        List<string> privateKeys = NormalizeList(authentication.PrivateKeySecretIds, trimEntries: true, skipEmpty: true);

        if (authentication.UsePassword && passwordSecretId is null)
        {
            throw new InvalidDataException("Password authentication requires PasswordSecretId.");
        }

        return authentication with
        {
            PasswordSecretId = passwordSecretId,
            PrivateKeySecretIds = privateKeys,
        };
    }

    private static SshProxyOptions? NormalizeSshProxy(SshProxyOptions? proxy)
    {
        if (proxy is null)
        {
            return null;
        }

        return proxy with
        {
            Host = NormalizeOptional(proxy.Host) ?? string.Empty,
            Port = proxy.Port,
            Username = NormalizeOptional(proxy.Username),
            Password = NormalizeOptional(proxy.Password),
        };
    }

    private static List<SshPortForwardOptions> NormalizeSshPortForwardings(List<SshPortForwardOptions> forwardings)
    {
        if (forwardings.Count == 0)
        {
            return [];
        }

        List<SshPortForwardOptions> normalized = new(forwardings.Count);
        for (int i = 0; i < forwardings.Count; i++)
        {
            SshPortForwardOptions next = forwardings[i];
            normalized.Add(next with
            {
                BindAddress = NormalizeOptional(next.BindAddress) ?? "127.0.0.1",
                DestinationHost = NormalizeOptional(next.DestinationHost),
            });
        }

        return normalized;
    }

    private static SshX11Options? NormalizeSshX11(SshX11Options? x11)
    {
        if (x11 is null)
        {
            return null;
        }

        return x11 with
        {
            Display = NormalizeOptional(x11.Display) ?? string.Empty,
        };
    }

    private static SshPolicyOptions NormalizeSshPolicy(SshPolicyOptions policy)
    {
        int keepAlive = policy.KeepAliveIntervalSeconds > 0
            ? policy.KeepAliveIntervalSeconds
            : 30;
        int connectTimeout = policy.ConnectTimeoutSeconds > 0
            ? policy.ConnectTimeoutSeconds
            : 15;
        return policy with
        {
            KeepAliveIntervalSeconds = keepAlive,
            ConnectTimeoutSeconds = connectTimeout,
        };
    }

    private static TerminalSessionRawTcpSettings NormalizeRawTcpSettings(TerminalSessionRawTcpSettings settings)
    {
        return settings with
        {
            Host = NormalizeOptional(settings.Host) ?? string.Empty,
            Port = settings.Port,
        };
    }

    private static TerminalSessionTelnetSettings NormalizeTelnetSettings(TerminalSessionTelnetSettings settings)
    {
        return settings with
        {
            Host = NormalizeOptional(settings.Host) ?? string.Empty,
            Port = settings.Port,
            TerminalType = NormalizeOptional(settings.TerminalType) ?? "xterm",
            InitialCommand = NormalizeOptional(settings.InitialCommand),
        };
    }

    private static TerminalSessionSerialSettings NormalizeSerialSettings(TerminalSessionSerialSettings settings)
    {
        return settings with
        {
            PortName = NormalizeOptional(settings.PortName) ?? string.Empty,
            BaudRate = settings.BaudRate,
            DataBits = settings.DataBits,
            NewLine = NormalizeOptional(settings.NewLine) ?? "\n",
        };
    }

    private static TerminalSessionLoggingSettings NormalizeLogging(TerminalSessionLoggingSettings logging)
    {
        return logging with
        {
            FilePath = NormalizeOptional(logging.FilePath),
        };
    }

    private static TerminalSessionProxySettings NormalizeProxy(TerminalSessionProxySettings proxy)
    {
        string? host = NormalizeOptional(proxy.Host);
        if (proxy.Enabled && host is null)
        {
            throw new InvalidDataException("Enabled proxy settings require a proxy Host.");
        }

        if (proxy.Enabled && proxy.Port is <= 0 or > 65_535)
        {
            throw new InvalidDataException($"Enabled proxy uses invalid port '{proxy.Port}'.");
        }

        return proxy with
        {
            Host = host,
            Username = NormalizeOptional(proxy.Username),
            PasswordSecretId = NormalizeOptional(proxy.PasswordSecretId),
            Command = NormalizeOptional(proxy.Command),
            ExcludedHosts = NormalizeList(proxy.ExcludedHosts, trimEntries: true, skipEmpty: true),
        };
    }

    private static Dictionary<string, string> NormalizeDictionary(Dictionary<string, string> source)
    {
        if (source.Count == 0)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        Dictionary<string, string> normalized = new(source.Count, StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> pair in source)
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
            {
                continue;
            }

            normalized[pair.Key.Trim()] = pair.Value;
        }

        return normalized;
    }

    private static List<string> NormalizeList(
        List<string> source,
        bool trimEntries = false,
        bool skipEmpty = false)
    {
        if (source.Count == 0)
        {
            return [];
        }

        List<string> normalized = new(source.Count);
        for (int i = 0; i < source.Count; i++)
        {
            string value = source[i];
            string next = trimEntries ? value.Trim() : value;
            if (skipEmpty && string.IsNullOrWhiteSpace(next))
            {
                continue;
            }

            normalized.Add(next);
        }

        return normalized;
    }

    private static string NormalizePasteSafetyPolicy(string? policy)
    {
        if (string.Equals(policy, "None", StringComparison.OrdinalIgnoreCase))
        {
            return "None";
        }

        if (string.Equals(policy, "ConfirmUnsafe", StringComparison.OrdinalIgnoreCase))
        {
            return "ConfirmUnsafe";
        }

        if (string.Equals(policy, "BlockUnsafe", StringComparison.OrdinalIgnoreCase))
        {
            return "BlockUnsafe";
        }

        if (string.Equals(policy, "SanitizeControlSequences", StringComparison.OrdinalIgnoreCase))
        {
            return "SanitizeControlSequences";
        }

        return "None";
    }

    private static string NormalizeTransportId(string? transportId)
    {
        if (string.Equals(transportId, TerminalTransportIds.Pty, StringComparison.OrdinalIgnoreCase))
        {
            return TerminalTransportIds.Pty;
        }

        if (string.Equals(transportId, TerminalTransportIds.Pipe, StringComparison.OrdinalIgnoreCase))
        {
            return TerminalTransportIds.Pipe;
        }

        if (string.Equals(transportId, TerminalTransportIds.Ssh, StringComparison.OrdinalIgnoreCase))
        {
            return TerminalTransportIds.Ssh;
        }

        if (string.Equals(transportId, TerminalTransportIds.RawTcp, StringComparison.OrdinalIgnoreCase))
        {
            return TerminalTransportIds.RawTcp;
        }

        if (string.Equals(transportId, TerminalTransportIds.Telnet, StringComparison.OrdinalIgnoreCase))
        {
            return TerminalTransportIds.Telnet;
        }

        if (string.Equals(transportId, TerminalTransportIds.Serial, StringComparison.OrdinalIgnoreCase))
        {
            return TerminalTransportIds.Serial;
        }

        throw new InvalidDataException($"Unsupported transport id '{transportId}'.");
    }

    private static string NormalizeRequired(string? value, string error)
    {
        string? normalized = NormalizeOptional(value);
        return normalized ?? throw new InvalidDataException(error);
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string? NormalizeColor(string? value)
    {
        string? normalized = NormalizeOptional(value);
        if (normalized is null)
        {
            return null;
        }

        ReadOnlySpan<char> text = normalized.AsSpan();
        if (text.Length > 0 && text[0] == '#')
        {
            text = text[1..];
        }

        if (text.Length != 6 && text.Length != 8)
        {
            return null;
        }

        if (!uint.TryParse(
                text,
                NumberStyles.HexNumber,
                CultureInfo.InvariantCulture,
                out uint color))
        {
            return null;
        }

        if (text.Length == 6)
        {
            color |= 0xFF000000u;
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"#{color:X8}");
    }
}

/// <summary>
/// Persistence abstraction for session profile documents.
/// </summary>
public interface ITerminalSessionProfileStore
{
    /// <summary>
    /// Loads a profile document.
    /// </summary>
    ValueTask<TerminalSessionProfilesDocument> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves a profile document.
    /// </summary>
    ValueTask SaveAsync(TerminalSessionProfilesDocument document, CancellationToken cancellationToken = default);
}

/// <summary>
/// JSON file-backed session profile store.
/// </summary>
public sealed class JsonFileTerminalSessionProfileStore : ITerminalSessionProfileStore
{
    /// <summary>
    /// Creates a JSON file profile store.
    /// </summary>
    public JsonFileTerminalSessionProfileStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        FilePath = filePath;
    }

    /// <summary>
    /// Gets the backing file path.
    /// </summary>
    public string FilePath { get; }

    /// <inheritdoc />
    public async ValueTask<TerminalSessionProfilesDocument> LoadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(FilePath))
        {
            return new TerminalSessionProfilesDocument();
        }

        await using FileStream stream = File.OpenRead(FilePath);
        return await TerminalSessionProfileSerializer.LoadAsync(stream, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public ValueTask SaveAsync(TerminalSessionProfilesDocument document, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(document);

        string json = TerminalSessionProfileSerializer.ToJson(document);
        SshSecretFileIo.WriteJsonAtomically(FilePath, json);
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Factory and path helpers for profile stores.
/// </summary>
public static class TerminalSessionProfileStoreFactory
{
    /// <summary>
    /// Creates a default JSON file profile store.
    /// </summary>
    public static ITerminalSessionProfileStore CreateDefault(string? filePath = null)
    {
        string path = string.IsNullOrWhiteSpace(filePath)
            ? GetDefaultFilePath()
            : filePath;
        return new JsonFileTerminalSessionProfileStore(path);
    }

    /// <summary>
    /// Gets the default profile file path.
    /// </summary>
    public static string GetDefaultFilePath()
    {
        return Path.Combine(GetDefaultStorageDirectory(), "session-profiles.json");
    }

    private static string GetDefaultStorageDirectory()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            return Path.Combine(localAppData, "RoyalTerminal");
        }

        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            return Path.Combine(userProfile, ".royalterminal");
        }

        return Path.Combine(Directory.GetCurrentDirectory(), ".royalterminal");
    }
}
