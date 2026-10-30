// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text.Json;
using RoyalTerminal.Multiplexing.Automation;
using Xunit;
using static RoyalTerminal.Multiplexing.AutomationTests.AutomationTestData;

namespace RoyalTerminal.Multiplexing.AutomationTests;

public sealed class ActionRunnerTests
{
    [Fact]
    public async Task EntireActionIsValidatedBeforeAnyMutation()
    {
        RecordingInvoker invoker = new();
        MultiplexerActionRunner runner = Runner(invoker);
        MultiplexerAction action = Action("invalid", "echo", "unknown");
        Assert.Equal(MultiplexerAutomationError.MethodNotFound,
            (await Assert.ThrowsAsync<MultiplexerAutomationException>(() => runner.RunAsync(action).AsTask())).Code);
        Assert.Empty(invoker.Calls);
        action = Action() with { Steps = [Action().Steps[0], new() { Method = "echo", Arguments = Json("{\"typo\":\"bad\"}") }] };
        await Assert.ThrowsAsync<MultiplexerAutomationException>(() => runner.RunAsync(action).AsTask());
        Assert.Empty(invoker.Calls);
    }

    [Fact]
    public async Task StepsRunSequentiallyAndPreserveTheirResults()
    {
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        RecordingInvoker invoker = new()
        {
            Handler = async (method, args, token) =>
            {
                if (method == "echo") { entered.SetResult(); await release.Task.WaitAsync(token); }
                return args;
            },
        };
        Task<MultiplexerActionResult> run = Runner(invoker).RunAsync(Action("ordered", "echo", "other")).AsTask();
        await entered.Task;
        Assert.Single(invoker.Calls);
        release.SetResult();
        MultiplexerActionResult result = await run;
        Assert.Equal(["echo", "other"], invoker.Calls.Select(call => call.Method).ToArray());
        Assert.Equal([0, 1], result.Steps.Select(step => step.Index).ToArray());
        Assert.Equal("hello", result.Steps[0].Result.GetProperty("text").GetString());
    }

    [Fact]
    public async Task HostFailureStopsLaterStepsAndReportsEarlierSuccess()
    {
        RecordingInvoker invoker = new()
        {
            Handler = (method, args, _) => method == "other" ? throw new IOException("failure") : new(args),
        };
        MultiplexerActionExecutionException failure = await Assert.ThrowsAsync<MultiplexerActionExecutionException>(() =>
            Runner(invoker).RunAsync(Action("fail", "echo", "other", "echo")).AsTask());
        Assert.Equal(MultiplexerAutomationError.InvocationFailed, failure.Code);
        Assert.Equal(1, failure.StepIndex);
        Assert.Single(failure.CompletedSteps);
        Assert.Equal(2, invoker.Calls.Count);
    }

    [Fact]
    public async Task CallerCancellationStopsAnUncooperativeMethodWithoutStartingNextStep()
    {
        TaskCompletionSource<JsonElement> pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        RecordingInvoker invoker = new() { Handler = (_, _, _) => { entered.SetResult(); return new(pending.Task); } };
        using CancellationTokenSource cancel = new();
        Task<MultiplexerActionResult> run = Runner(invoker).RunAsync(Action("cancel", "echo", "other"), cancel.Token).AsTask();
        await entered.Task;
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.Single(invoker.Calls);
        pending.SetException(new IOException("late failure is observed"));
    }

    [Fact]
    public async Task StepDeadlineBoundsWaitingEvenWhenInvokerIgnoresCancellation()
    {
        TaskCompletionSource<JsonElement> pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken supplied = default;
        RecordingInvoker invoker = new() { Handler = (_, _, token) => { supplied = token; return new(pending.Task); } };
        MultiplexerAction action = Action("deadline", "echo", "other");
        action = action with { Steps = [action.Steps[0] with { TimeoutMilliseconds = 30 }, action.Steps[1]] };
        MultiplexerActionExecutionException failure = await Assert.ThrowsAsync<MultiplexerActionExecutionException>(() =>
            Runner(invoker).RunAsync(action).AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(MultiplexerAutomationError.Timeout, failure.Code);
        Assert.True(supplied.IsCancellationRequested);
        Assert.Single(invoker.Calls);
        pending.SetResult(Json("{}"));
    }

    [Fact]
    public async Task ActionDeadlineAlsoBoundsLongIndividualSteps()
    {
        TaskCompletionSource<JsonElement> pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        RecordingInvoker invoker = new() { Handler = (_, _, _) => new(pending.Task) };
        MultiplexerAction action = Action() with { TimeoutMilliseconds = 30 };
        MultiplexerActionExecutionException failure = await Assert.ThrowsAsync<MultiplexerActionExecutionException>(() =>
            Runner(invoker).RunAsync(action).AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(MultiplexerAutomationError.Timeout, failure.Code);
        pending.SetResult(Json("{}"));
    }

    [Fact]
    public async Task StepSnapshotCannotBeChangedWhileEarlierInvocationWaits()
    {
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        RecordingInvoker invoker = new()
        {
            Handler = async (method, args, token) => { if (method == "echo") await release.Task.WaitAsync(token); return args; },
        };
        MultiplexerActionStep[] steps = [Action().Steps[0], Action("unused", "other").Steps[0]];
        Task<MultiplexerActionResult> run = Runner(invoker).RunAsync(new() { Id = "snapshot", Steps = steps }).AsTask();
        steps[1] = steps[1] with { Method = "unknown" };
        release.SetResult();
        await run;
        Assert.Equal("other", invoker.Calls[1].Method);
    }

    [Fact]
    public void LimitsAndSchemaVocabularyAreValidatedBeforeExecution()
    {
        MultiplexerActionRunner runner = Runner(limits: new() { MaxSteps = 1 });
        Assert.Throws<MultiplexerAutomationException>(() => runner.Validate(Action("too-many", "echo", "other")));
        Assert.Throws<MultiplexerAutomationException>(() => runner.Validate(Action() with { TimeoutMilliseconds = 0 }));
        Assert.Throws<MultiplexerAutomationException>(() => new MultiplexerActionRunner(new RecordingInvoker(),
            [new("bad", Json("{\"type\":\"string\",\"pattern\":\".*\"}"))]));
    }

    [Fact]
    public async Task OversizedMethodResultStopsTheAction()
    {
        RecordingInvoker invoker = new() { Handler = (_, _, _) => new(Json("\"" + new string('a', 1000) + "\"")) };
        MultiplexerActionRunner runner = Runner(invoker, new() { MaxJsonBytes = 256 });
        MultiplexerActionExecutionException failure = await Assert.ThrowsAsync<MultiplexerActionExecutionException>(() => runner.RunAsync(Action()).AsTask());
        Assert.Equal(MultiplexerAutomationError.InvocationFailed, failure.Code);
        Assert.Empty(failure.CompletedSteps);
    }
}
