// Copyright (c) Royal Apps. Licensed under the MIT license.
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using RoyalTerminal.Multiplexing.Cli;

namespace RoyalTerminal.Multiplexing.Server;

/// <summary>One trusted worker's private process handles. Never registered in the HTTP method catalog.</summary>
internal sealed class ConfigProcessScope(bool enabled, DetachedConfigJobs supervisor) : IDisposable
{
    private readonly LuaLocalProcesses _owned = new();
    private readonly Dictionary<int, DetachedConfigJobs.Lease> _detached = [];
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private int _serial;

    internal async ValueTask<LuaReply> DispatchAsync(JsonElement frame, CancellationToken ct)
    {
        if (!enabled) return new(false, null, "Server subprocesses are disabled by the operator.");
        try
        {
            ct.ThrowIfCancellationRequested();
            if (frame.GetProperty("op").GetString() == "clock") return Result(JsonValue.Create(_clock.Elapsed.TotalSeconds)!);
            if (frame.GetProperty("op").GetString() != "process") return new(false,null,"Invalid process operation.");
            string? operation = frame.GetProperty("operation").GetString();
            if (operation == "start")
            {
                var input = JsonNode.Parse(frame.GetRawText())!.AsObject();
                var args = input["argv"]?.AsArray(); var options = input["options"]?.AsObject();
                if (args is not { Count: > 0 } || options is null || !Path.IsPathFullyQualified(args[0]!.GetValue<string>()))
                    throw new ArgumentException("Configured subprocesses require an absolute executable path.");
                if (options["cwd"] is JsonNode cwd && !Path.IsPathFullyQualified(cwd.GetValue<string>()))
                    throw new ArgumentException("The working directory must be absolute.");
                // Do not inherit the host's bearer keys, vault credentials or CI environment. A script may explicitly pass ordinary variables.
                options["clear_env"] = true;
                if (options["env"] is JsonObject env && env.Any(pair => pair.Key.StartsWith("ROYALTERMINAL_",StringComparison.OrdinalIgnoreCase) || pair.Key.Equals("LUA_INIT",StringComparison.OrdinalIgnoreCase)))
                    throw new ArgumentException("Reserved subprocess environment name.");
                bool detached = options["detach"]?.GetValue<bool>() ?? false;
                options["detach"] = false; // Ownership is transferred to our bounded broker supervisor, never abandoned.
                using var sanitized = JsonDocument.Parse(input.ToJsonString());
                if (!detached) return Result(await _owned.DispatchAsync(sanitized.RootElement,ct));
                var lease = supervisor.Reserve();
                try
                {
                    var receipt = (await lease.Process.DispatchAsync(sanitized.RootElement,ct)).AsObject();
                    lease.Handle = receipt["id"]!.GetValue<int>();
                    int handle = checked(--_serial); _detached.Add(handle,lease);receipt["id"] = handle;
                    return Result(receipt);
                }
                catch { lease.Dispose(); throw; }
            }
            int id = frame.GetProperty("id").GetInt32();
            if (id > 0) return Result(await _owned.DispatchAsync(frame,ct));
            if (!_detached.TryGetValue(id,out var job)) throw new ArgumentException("Unknown detached handle.");
            if (operation == "release")
            {
                _detached.Remove(id);job.Transfer();return Result(JsonValue.Create(true)!);
            }
            var mapped = JsonNode.Parse(frame.GetRawText())!.AsObject();mapped["id"] = job.Handle;
            using var forwarded = JsonDocument.Parse(mapped.ToJsonString());
            return Result(await job.Process.DispatchAsync(forwarded.RootElement,ct));
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or Win32Exception or IOException or FormatException or JsonException or KeyNotFoundException)
        { return new(false,null,e.Message); }
    }
    private static LuaReply Result(JsonNode node)
    {
        using var doc=JsonDocument.Parse(node.ToJsonString());return new(true,doc.RootElement.Clone());
    }
    public void Dispose()
    {
        try { _owned.Dispose(); }
        finally { foreach (var job in _detached.Values) job.Transfer();_detached.Clear(); }
    }
}

/// <summary>Detached means surviving the action, not surviving the broker. Admission is reserved before the process starts.</summary>
internal sealed class DetachedConfigJobs
{
    private readonly Lock _gate = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly TaskCompletionSource _empty = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _count; private bool _stopped;
    internal CancellationToken Stopping => _stop.Token;
    internal Lease Reserve()
    {
        lock (_gate)
        {
            if (_stopped || _count >= 16) throw new InvalidOperationException("Detached process capacity is unavailable.");
            ++_count;return new Lease(this);
        }
    }
    private void Release()
    {
        lock (_gate) { --_count;if (_stopped && _count==0) _empty.TrySetResult(); }
    }
    internal async Task StopAsync(CancellationToken ct)
    {
        lock (_gate) { _stopped=true;if (_count==0) _empty.TrySetResult(); }
        await _stop.CancelAsync();await _empty.Task.WaitAsync(ct);
    }
    internal sealed class Lease(DetachedConfigJobs host) : IDisposable
    {
        internal LuaLocalProcesses Process { get; } = new();
        internal int Handle { get; set; }
        internal void Transfer() => _ = ReapAsync();
        private async Task ReapAsync()
        {
            // Do not inherit a released worker's still-open stdin; send EOF. Read to completion without retaining a second transcript.
            try
            {
                using var close=JsonDocument.Parse($$"""{"operation":"close","id":{{Handle}}}""");
                await Process.DispatchAsync(close.RootElement,host.Stopping);
                using var poll=JsonDocument.Parse($$"""{"operation":"poll","id":{{Handle}},"ms":250}""");
                while (!host.Stopping.IsCancellationRequested)
                {
                    var page=await Process.DispatchAsync(poll.RootElement,host.Stopping);
                    if (page["code"] is not null) break;
                }
            }
            catch (Exception e) when (e is OperationCanceledException or IOException or InvalidOperationException or Win32Exception) { }
            finally { Dispose(); }
        }
        public void Dispose() { try { Process.Dispose(); } finally { host.Release(); } }
    }
}
