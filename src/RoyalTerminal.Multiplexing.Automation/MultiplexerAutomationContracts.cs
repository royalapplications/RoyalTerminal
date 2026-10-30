// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace RoyalTerminal.Multiplexing.Automation;

/// <summary>Host boundary for invoking authenticated, authorized API methods.</summary>
public interface IMultiplexerMethodInvoker
{
    /// <summary>Invokes one method once. The host retains responsibility for permission checks.</summary>
    ValueTask<JsonElement> InvokeAsync(string method, JsonElement arguments,
        CancellationToken cancellationToken = default);
}

/// <summary>A method's name and supported JSON Schema input contract.</summary>
public sealed record MultiplexerActionMethod(string Name, JsonElement InputSchema);

/// <summary>One explicitly parameterized API call; no scripting language or expression evaluation is performed.</summary>
public sealed record MultiplexerActionStep
{
    /// <summary>Exact registered API method name.</summary>
    [JsonRequired] public string Method { get; init; } = "";
    /// <summary>JSON arguments validated before any action step executes.</summary>
    [JsonRequired] public JsonElement Arguments { get; init; }
    /// <summary>Optional positive deadline for this step, in milliseconds.</summary>
    public int? TimeoutMilliseconds { get; init; }
}

/// <summary>A named, bounded, sequential list of API calls.</summary>
public sealed record MultiplexerAction
{
    /// <summary>Stable action identifier used by keybindings.</summary>
    [JsonRequired] public string Id { get; init; } = "";
    /// <summary>Steps in execution order. Every step is validated before the first invocation.</summary>
    [JsonRequired] public IReadOnlyList<MultiplexerActionStep> Steps { get; init; } = [];
    /// <summary>Optional deadline covering the complete action.</summary>
    public int? TimeoutMilliseconds { get; init; }
}

/// <summary>An owned successful step result.</summary>
public sealed record MultiplexerActionStepResult(int Index, string Method, JsonElement Result);

/// <summary>All successfully completed steps of an action.</summary>
public sealed record MultiplexerActionResult(string ActionId, IReadOnlyList<MultiplexerActionStepResult> Steps);

/// <summary>Classifiable configuration, target, and action errors.</summary>
public enum MultiplexerAutomationError
{
    /// <summary>Configuration or arguments violate a declared contract.</summary>
    InvalidConfiguration,
    /// <summary>A selector matches no target.</summary>
    TargetNotFound,
    /// <summary>A selector matches more than one target at the same precedence.</summary>
    AmbiguousTarget,
    /// <summary>An action references an unregistered method.</summary>
    MethodNotFound,
    /// <summary>An action or step exceeded its deadline.</summary>
    Timeout,
    /// <summary>A host method failed.</summary>
    InvocationFailed,
}

/// <summary>An expected failure without embedding argument values or secret payloads in its message.</summary>
public class MultiplexerAutomationException : InvalidOperationException
{
    /// <summary>Creates a categorized error.</summary>
    public MultiplexerAutomationException(MultiplexerAutomationError code, string message,
        Exception? innerException = null) : base(message, innerException) => Code = code;
    /// <summary>Stable error category.</summary>
    public MultiplexerAutomationError Code { get; }
}

/// <summary>An action stopped at a specific step; earlier successful effects are not rolled back.</summary>
public sealed class MultiplexerActionExecutionException : MultiplexerAutomationException
{
    /// <summary>Creates a failure with an owned snapshot of completed step results.</summary>
    public MultiplexerActionExecutionException(MultiplexerAutomationError code, string actionId,
        int stepIndex, string method, IReadOnlyList<MultiplexerActionStepResult> completedSteps,
        Exception innerException) : base(code, $"Action '{actionId}' stopped at step {stepIndex + 1} ({method}).", innerException)
    {
        ActionId = actionId;
        StepIndex = stepIndex;
        Method = method;
        CompletedSteps = completedSteps;
    }
    /// <summary>Action identifier.</summary>
    public string ActionId { get; }
    /// <summary>Zero-based index of the failed or timed-out step.</summary>
    public int StepIndex { get; }
    /// <summary>Method that did not complete successfully.</summary>
    public string Method { get; }
    /// <summary>Successful results before failure.</summary>
    public IReadOnlyList<MultiplexerActionStepResult> CompletedSteps { get; }
}

/// <summary>Explicit resource and time limits shared by configuration and action execution.</summary>
public sealed record MultiplexerAutomationLimits
{
    /// <summary>Maximum sequential API calls in an action.</summary>
    public int MaxSteps { get; init; } = 32;
    /// <summary>Default action deadline.</summary>
    public int DefaultActionTimeoutMilliseconds { get; init; } = 30_000;
    /// <summary>Largest configurable action deadline.</summary>
    public int MaxActionTimeoutMilliseconds { get; init; } = 120_000;
    /// <summary>Default per-step deadline.</summary>
    public int DefaultStepTimeoutMilliseconds { get; init; } = 10_000;
    /// <summary>Largest configurable per-step deadline.</summary>
    public int MaxStepTimeoutMilliseconds { get; init; } = 60_000;
    /// <summary>Maximum UTF-8 bytes in a step's arguments or one method schema.</summary>
    public int MaxJsonBytes { get; init; } = 64 * 1024;
    /// <summary>Maximum configuration file bytes.</summary>
    public int MaxConfigurationBytes { get; init; } = 1024 * 1024;
    /// <summary>Maximum registered methods or target candidates.</summary>
    public int MaxMethods { get; init; } = 4096;
    /// <summary>Maximum configured actions.</summary>
    public int MaxActions { get; init; } = 256;
    /// <summary>Maximum configured keybindings.</summary>
    public int MaxBindings { get; init; } = 2048;
    /// <summary>Maximum named modes.</summary>
    public int MaxModes { get; init; } = 128;
    /// <summary>Maximum keys in one chord sequence.</summary>
    public int MaxChordKeys { get; init; } = 8;
    /// <summary>Maximum stacked modes, including the default frame.</summary>
    public int MaxModeDepth { get; init; } = 16;
    /// <summary>Idle deadline for an incomplete key sequence.</summary>
    public int ChordTimeoutMilliseconds { get; init; } = 1500;

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxSteps, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(DefaultActionTimeoutMilliseconds, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(DefaultStepTimeoutMilliseconds, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxActionTimeoutMilliseconds, DefaultActionTimeoutMilliseconds);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxStepTimeoutMilliseconds, DefaultStepTimeoutMilliseconds);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxJsonBytes, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxConfigurationBytes, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxMethods, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxActions, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxBindings, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxModes, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxChordKeys, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxModeDepth, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(ChordTimeoutMilliseconds, 1);
    }
}
