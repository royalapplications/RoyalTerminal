// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace RoyalTerminal.Multiplexing.Automation;

/// <summary>Options for a named key table. Unbound keys in exclusive modes are consumed.</summary>
public sealed record MultiplexerKeyModeDefinition
{
    /// <summary>Lowercase mode identifier; default is the permanent base table.</summary>
    [JsonRequired] public string Name { get; set; } = "";
    /// <summary>Suppress fallback into lower modes and terminal input.</summary>
    public bool Exclusive { get; set; }
}

/// <summary>A shortcut with exactly one action, mode operation, or blocked-key operation.</summary>
public sealed record MultiplexerKeyBinding
{
    /// <summary>Canonical key sequence, with keys separated by greater-than characters.</summary>
    [JsonRequired] public string Sequence { get; set; } = "";
    /// <summary>Key table name.</summary>
    public string Mode { get; set; } = "default";
    /// <summary>Action to dispatch; null when this binding only changes modes or blocks a key.</summary>
    public string? ActionId { get; set; }
    /// <summary>Mode to push.</summary>
    public string? EnterMode { get; set; }
    /// <summary>Whether the entered frame leaves after its first resolved shortcut.</summary>
    public bool Once { get; set; }
    /// <summary>Pop the current mode, keeping the permanent default frame.</summary>
    public bool ExitMode { get; set; }
    /// <summary>Pop every frame above default.</summary>
    public bool ExitAllModes { get; set; }
    /// <summary>Consume this shortcut without an action or fallback.</summary>
    public bool Block { get; set; }
}

/// <summary>Versioned experimental JSON configuration. This is not a Lua program.</summary>
public sealed record MultiplexerAutomationConfiguration
{
    /// <summary>Supported format version, currently one.</summary>
    public int Version { get; set; } = 1;
    /// <summary>Declarative actions in definition order.</summary>
    public IReadOnlyList<MultiplexerAction> Actions { get; set; } = [];
    /// <summary>Mode options in definition order.</summary>
    public IReadOnlyList<MultiplexerKeyModeDefinition> Modes { get; set; } = [];
    /// <summary>Bindings in definition order. Later definitions win exact and prefix conflicts.</summary>
    public IReadOnlyList<MultiplexerKeyBinding> Bindings { get; set; } = [];
}

/// <summary>A visible notice produced when newer configuration shadows an older binding.</summary>
public sealed record MultiplexerConfigurationDiagnostic(string Code, string Message);

/// <summary>Effective configuration and nonfatal override diagnostics.</summary>
public sealed record MultiplexerResolvedConfiguration(MultiplexerAutomationConfiguration Configuration,
    IReadOnlyList<MultiplexerConfigurationDiagnostic> Diagnostics);

/// <summary>Reflection-free JSON serialization contract for experimental configuration and actions.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, WriteIndented = true)]
[JsonSerializable(typeof(MultiplexerAutomationConfiguration))]
[JsonSerializable(typeof(MultiplexerResolvedConfiguration))]
[JsonSerializable(typeof(MultiplexerAction))]
[JsonSerializable(typeof(MultiplexerActionResult))]
public partial class MultiplexerAutomationJsonContext : JsonSerializerContext;

/// <summary>Loads strict JSON and merges defaults, server configuration, then client overrides.</summary>
public static class MultiplexerConfigurationLoader
{
    /// <summary>Parses bounded JSON, rejects duplicate/unknown properties, and validates all action calls.</summary>
    public static MultiplexerAutomationConfiguration Parse(ReadOnlySpan<byte> utf8,
        MultiplexerActionRunner actionRunner, MultiplexerAutomationLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(actionRunner);
        MultiplexerAutomationLimits bounds = limits ?? new();
        bounds.Validate();
        if (utf8.Length > bounds.MaxConfigurationBytes) throw MultiplexerArgumentValidator.Invalid("Configuration exceeds its byte budget.");
        try
        {
            using JsonDocument document = JsonDocument.Parse(utf8.ToArray(), new() { MaxDepth = 32 });
            MultiplexerArgumentValidator.ValidateJson(document.RootElement, bounds.MaxConfigurationBytes);
            MultiplexerAutomationConfiguration config = JsonSerializer.Deserialize(document.RootElement,
                MultiplexerAutomationJsonContext.Default.MultiplexerAutomationConfiguration)
                ?? throw MultiplexerArgumentValidator.Invalid("Configuration cannot be null.");
            ValidateLayer(config, bounds);
            foreach (MultiplexerAction action in config.Actions) actionRunner.Validate(action);
            foreach (MultiplexerKeyModeDefinition mode in config.Modes)
            {
                if (mode is null) throw MultiplexerArgumentValidator.Invalid("Modes cannot be null.");
                ValidateModeName(mode.Name);
            }
            return config with
            {
                Actions = config.Actions.ToArray(),
                Modes = config.Modes.ToArray(),
                Bindings = config.Bindings.Select(binding => ValidateBinding(binding, bounds)).ToArray(),
            };
        }
        catch (JsonException error)
        {
            throw new MultiplexerAutomationException(MultiplexerAutomationError.InvalidConfiguration,
                "Configuration is not valid versioned automation JSON.", error);
        }
    }

    /// <summary>Builds an owned effective configuration. A failure leaves every supplied layer untouched.</summary>
    public static MultiplexerResolvedConfiguration Merge(MultiplexerAutomationConfiguration defaults,
        MultiplexerAutomationConfiguration server, MultiplexerAutomationConfiguration client,
        MultiplexerActionRunner actionRunner, MultiplexerAutomationLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(actionRunner);
        MultiplexerAutomationLimits bounds = limits ?? new();
        bounds.Validate();
        Dictionary<string, MultiplexerAction> actions = new(StringComparer.Ordinal);
        Dictionary<string, MultiplexerKeyModeDefinition> modes = new(StringComparer.Ordinal)
        {
            ["default"] = new() { Name = "default" },
        };
        List<(MultiplexerKeyBinding Binding, string Source)> bindings = [];
        List<MultiplexerConfigurationDiagnostic> diagnostics = [];
        (MultiplexerAutomationConfiguration Config, string Source)[] layers = [(defaults, "defaults"), (server, "server"), (client, "client")];
        foreach ((MultiplexerAutomationConfiguration config, string source) in layers)
        {
            ValidateLayer(config, bounds);
            foreach (MultiplexerAction action in config.Actions)
            {
                actionRunner.Validate(action);
                actions[action.Id] = action with { Steps = action.Steps.Select(step => step with { Arguments = step.Arguments.Clone() }).ToArray() };
            }
            foreach (MultiplexerKeyModeDefinition mode in config.Modes)
            {
                if (mode is null) throw MultiplexerArgumentValidator.Invalid("Modes cannot contain null items.");
                ValidateModeName(mode.Name);
                modes[mode.Name] = mode with { };
            }
            foreach (MultiplexerKeyBinding raw in config.Bindings)
            {
                MultiplexerKeyBinding binding = ValidateBinding(raw, bounds);
                modes.TryAdd(binding.Mode, new() { Name = binding.Mode });
                for (int index = bindings.Count - 1; index >= 0; index--)
                {
                    (MultiplexerKeyBinding old, string origin) = bindings[index];
                    if (old.Mode != binding.Mode || !Conflicts(old.Sequence, binding.Sequence)) continue;
                    diagnostics.Add(new("binding_shadowed",
                        $"{source}:{binding.Mode}/{binding.Sequence} shadows {origin}:{old.Mode}/{old.Sequence}."));
                    bindings.RemoveAt(index);
                }
                bindings.Add((binding, source));
            }
        }
        if (actions.Count > bounds.MaxActions || modes.Count > bounds.MaxModes || bindings.Count > bounds.MaxBindings)
            throw MultiplexerArgumentValidator.Invalid("Merged configuration exceeds its item budgets.");
        foreach ((MultiplexerKeyBinding binding, _) in bindings)
        {
            if (binding.ActionId is { } action && !actions.ContainsKey(action)) throw MultiplexerArgumentValidator.Invalid($"Binding refers to unknown action '{action}'.");
            if (binding.EnterMode is { } mode && !modes.ContainsKey(mode)) throw MultiplexerArgumentValidator.Invalid($"Binding refers to unknown mode '{mode}'.");
        }
        return new(new()
        {
            Actions = actions.Values.ToArray(),
            Modes = modes.Values.ToArray(),
            Bindings = bindings.Select(item => item.Binding).ToArray(),
        }, diagnostics.ToArray());
    }

    private static void ValidateLayer(MultiplexerAutomationConfiguration config, MultiplexerAutomationLimits bounds)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (config.Version != 1) throw MultiplexerArgumentValidator.Invalid("Unsupported automation configuration version.");
        if (config.Actions is null || config.Modes is null || config.Bindings is null || config.Actions.Count > bounds.MaxActions ||
            config.Modes.Count > bounds.MaxModes || config.Bindings.Count > bounds.MaxBindings)
            throw MultiplexerArgumentValidator.Invalid("Configuration has missing lists or exceeds item budgets.");
    }

    internal static MultiplexerKeyBinding ValidateBinding(MultiplexerKeyBinding binding, MultiplexerAutomationLimits limits)
    {
        if (binding is null) throw MultiplexerArgumentValidator.Invalid("Bindings cannot contain null items.");
        ValidateModeName(binding.Mode);
        int operations = (binding.ActionId is null ? 0 : 1) + (binding.EnterMode is null ? 0 : 1) +
            (binding.ExitMode ? 1 : 0) + (binding.ExitAllModes ? 1 : 0) + (binding.Block ? 1 : 0);
        if (operations != 1 || binding.Once && binding.EnterMode is null)
            throw MultiplexerArgumentValidator.Invalid("A binding requires exactly one action, mode operation, or block operation; once applies only to enterMode.");
        if (binding.ActionId is { } action) MultiplexerActionRunner.ValidateName(action, "action");
        if (binding.EnterMode is { } mode) ValidateModeName(mode);
        return binding with { Sequence = MultiplexerKeySyntax.NormalizeSequence(binding.Sequence, limits.MaxChordKeys) };
    }

    internal static void ValidateModeName(string name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > 64) throw MultiplexerArgumentValidator.Invalid("Invalid mode name.");
        foreach (char character in name)
            if (!(character is >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-'))
                throw MultiplexerArgumentValidator.Invalid("Mode names contain only lowercase ASCII letters, digits, underscores, and hyphens.");
    }

    private static bool Conflicts(string first, string second) => first == second ||
        first.StartsWith(second + ">", StringComparison.Ordinal) || second.StartsWith(first + ">", StringComparison.Ordinal);
}
