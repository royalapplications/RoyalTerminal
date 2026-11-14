// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text.Json;

namespace RoyalTerminal.Multiplexing.Automation;

/// <summary>Validates complete declarative actions, then invokes their steps sequentially without retries.</summary>
public sealed class MultiplexerActionRunner
{
    private readonly IMultiplexerMethodInvoker _invoker;
    private readonly Dictionary<string, JsonElement> _schemas = new(StringComparer.Ordinal);
    private readonly MultiplexerAutomationLimits _limits;
    private readonly TimeProvider _timeProvider;

    /// <summary>Creates an action runner from the server's explicit method registry.</summary>
    public MultiplexerActionRunner(IMultiplexerMethodInvoker invoker, IEnumerable<MultiplexerActionMethod> methods,
        MultiplexerAutomationLimits? limits = null, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(invoker);
        ArgumentNullException.ThrowIfNull(methods);
        _invoker = invoker;
        _limits = limits ?? new();
        _limits.Validate();
        _timeProvider = timeProvider ?? TimeProvider.System;
        foreach (MultiplexerActionMethod method in methods)
        {
            ValidateName(method.Name, "method");
            if (_schemas.Count >= _limits.MaxMethods) throw MultiplexerArgumentValidator.Invalid("The method registry exceeds its limit.");
            MultiplexerArgumentValidator.ValidateSchema(method.InputSchema, _limits.MaxJsonBytes);
            if (!_schemas.TryAdd(method.Name, method.InputSchema.Clone())) throw MultiplexerArgumentValidator.Invalid("The method registry contains duplicate names.");
        }
    }

    /// <summary>Checks every action step without invoking any method.</summary>
    public void Validate(MultiplexerAction action) => _ = Prepare(action);

    /// <summary>Runs a prevalidated snapshot. Cancellation never starts another step and never retries input.</summary>
    /// <remarks>A timeout stops the runner and cancels the supplied token, but an uncooperative host method
    /// may still complete an already-started side effect. Earlier successful steps are not rolled back.</remarks>
    public async ValueTask<MultiplexerActionResult> RunAsync(MultiplexerAction action,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        MultiplexerAction prepared = Prepare(action);
        using CancellationTokenSource deadline = new(TimeSpan.FromMilliseconds(prepared.TimeoutMilliseconds!.Value), _timeProvider);
        using CancellationTokenSource lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        List<MultiplexerActionStepResult> results = new(prepared.Steps.Count);
        for (int index = 0; index < prepared.Steps.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            MultiplexerActionStep step = prepared.Steps[index];
            using CancellationTokenSource stepDeadline = new(TimeSpan.FromMilliseconds(step.TimeoutMilliseconds!.Value), _timeProvider);
            using CancellationTokenSource stepLifetime = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, stepDeadline.Token);
            Task<JsonElement>? invocation = null;
            try
            {
                stepLifetime.Token.ThrowIfCancellationRequested();
                invocation = _invoker.InvokeAsync(step.Method, step.Arguments, stepLifetime.Token).AsTask();
                JsonElement result = await invocation.WaitAsync(stepLifetime.Token).ConfigureAwait(false);
                MultiplexerArgumentValidator.ValidateJson(result, _limits.MaxJsonBytes);
                results.Add(new(index, step.Method, result.Clone()));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                ObserveFailure(invocation);
                throw new OperationCanceledException(cancellationToken);
            }
            catch (Exception error)
            {
                ObserveFailure(invocation);
                MultiplexerAutomationError kind = deadline.IsCancellationRequested || stepDeadline.IsCancellationRequested
                    ? MultiplexerAutomationError.Timeout : MultiplexerAutomationError.InvocationFailed;
                throw new MultiplexerActionExecutionException(kind, prepared.Id, index, step.Method, results.ToArray(), error);
            }
        }
        return new(prepared.Id, results.ToArray());
    }

    private MultiplexerAction Prepare(MultiplexerAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        ValidateName(action.Id, "action");
        if (action.Steps is null || action.Steps.Count is < 1 || action.Steps.Count > _limits.MaxSteps)
            throw MultiplexerArgumentValidator.Invalid("An action must contain a bounded, nonempty list of steps.");
        int actionTimeout = ValidateTimeout(action.TimeoutMilliseconds, _limits.DefaultActionTimeoutMilliseconds,
            _limits.MaxActionTimeoutMilliseconds);
        MultiplexerActionStep[] steps = action.Steps.ToArray();
        for (int index = 0; index < steps.Length; index++)
        {
            MultiplexerActionStep step = steps[index] ?? throw MultiplexerArgumentValidator.Invalid("Action steps cannot be null.");
            ValidateName(step.Method, "method");
            if (!_schemas.TryGetValue(step.Method, out JsonElement schema))
                throw new MultiplexerAutomationException(MultiplexerAutomationError.MethodNotFound,
                    $"Action '{action.Id}' refers to unregistered method '{step.Method}'.");
            MultiplexerArgumentValidator.ValidateArguments(step.Arguments, schema, _limits.MaxJsonBytes);
            int timeout = ValidateTimeout(step.TimeoutMilliseconds, _limits.DefaultStepTimeoutMilliseconds, _limits.MaxStepTimeoutMilliseconds);
            steps[index] = step with { Arguments = step.Arguments.Clone(), TimeoutMilliseconds = timeout };
        }
        return action with { Steps = steps, TimeoutMilliseconds = actionTimeout };
    }

    private static int ValidateTimeout(int? configured, int defaultTimeout, int maximum)
    {
        int value = configured ?? defaultTimeout;
        if (value < 1 || value > maximum) throw MultiplexerArgumentValidator.Invalid("Action deadlines must be positive and within configured bounds.");
        return value;
    }

    internal static void ValidateName(string name, string kind)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 128) throw MultiplexerArgumentValidator.Invalid($"Invalid {kind} name.");
        foreach (char character in name)
            if (!(char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-'))
                throw MultiplexerArgumentValidator.Invalid($"Invalid character in {kind} name.");
    }

    private static void ObserveFailure(Task? task)
    {
        if (task is null) return;
        _ = task.ContinueWith(static failed => _ = failed.Exception, CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }
}
