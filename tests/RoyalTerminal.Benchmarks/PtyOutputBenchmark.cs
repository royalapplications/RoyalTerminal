// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Diagnostics;
using System.Runtime.Versioning;
using RoyalTerminal.Terminal;

/// <summary>
/// Real Unix PTY throughput and request/response latency. The consumer does the same
/// byte checksum in both builds, isolating IO/gather changes from VT parser changes.
/// Compile the same file without ROYALTERMINAL_OUTPUT_LEASES against the baseline.
/// </summary>
internal static class PtyOutputBenchmark
{
    public static void Run()
    {
        if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsLinux())
        {
            Console.WriteLine("The PTY output benchmark requires Unix.");
            return;
        }

        RunUnix();
    }

    [SupportedOSPlatform("macos")]
    [SupportedOSPlatform("linux")]
    private static void RunUnix()
    {
        const int bytes = 16 * 1024 * 1024;
        Console.WriteLine($"parser_priority={Environment.GetEnvironmentVariable("ROYALTERMINAL_BENCHMARK_PRIORITY") ?? "normal"}");
        Console.WriteLine("iteration,bytes,MiB_per_second,allocated_bytes,callbacks,mean_batch_bytes,parser_qos_request_applied");
        for (int iteration = 0; iteration < 6; iteration++)
        {
            using Pipeline pipeline = new(bytes);
            using UnixPty pty = new();
            pipeline.Attach(pty);
            long before = GC.GetTotalAllocatedBytes(precise: true);
            pty.Start(shell: "/bin/sh", arguments:
                ["-c", $"head -c {bytes} /dev/zero"]);
            pipeline.WaitForBytes();
            pty.Stop();
            long allocated = GC.GetTotalAllocatedBytes(precise: true) - before;
            double seconds = Stopwatch.GetElapsedTime(pipeline.FirstByteTimestamp, pipeline.LastByteTimestamp).TotalSeconds;
            Console.WriteLine(FormattableString.Invariant(
                $"{iteration},{pipeline.Bytes},{bytes / 1048576d / seconds:F3},{allocated},{pipeline.Callbacks},{bytes / (double)pipeline.Callbacks:F1},{pipeline.QosRequestApplied?.ToString() ?? "not_requested"}"));
        }

        using Pipeline interactive = new(long.MaxValue);
        using UnixPty shell = new();
        interactive.Attach(shell);
        shell.Start(shell: "/bin/sh", arguments:
            ["-c", "stty -echo; printf R; while IFS= read -r line; do printf '%s' \"$line\"; done"]);
        interactive.WaitForInteractiveByte();
        double[] samples = new double[100];
        for (int index = 0; index < samples.Length; index++)
        {
            Thread.Sleep(2);
            long started = Stopwatch.GetTimestamp();
            shell.Write("x\n");
            interactive.WaitForInteractiveByte();
            samples[index] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        }

        Array.Sort(samples);
        Console.WriteLine(FormattableString.Invariant(
            $"idle_roundtrip_ms_p50={samples[49]:F3},p95={samples[94]:F3},max={samples[^1]:F3}"));
    }

    private readonly record struct Chunk(ReadOnlyMemory<byte> Data
#if ROYALTERMINAL_OUTPUT_LEASES
        , TerminalOutputLease Lease
#endif
    );

    private sealed class Pipeline : IDisposable
    {
        private readonly object _sync = new();
        private readonly Queue<Chunk> _queue = new(256);
        private readonly Thread _thread;
        private readonly ManualResetEventSlim _done = new(false);
        private readonly AutoResetEvent _interactiveByte = new(false);
        private readonly long _target;
        private int _queuedBytes;
        private bool _stopping;
        private ulong _checksum;
        public long Bytes { get; private set; }
        public long Callbacks { get; private set; }
        public long FirstByteTimestamp { get; private set; }
        public long LastByteTimestamp { get; private set; }
        public bool? QosRequestApplied { get; private set; }

        public Pipeline(long target)
        {
            _target = target;
            _thread = new Thread(Parse) { IsBackground = true, Name = "Benchmark.Parser" };
            if (Environment.GetEnvironmentVariable("ROYALTERMINAL_BENCHMARK_PRIORITY") == "below")
            {
                _thread.Priority = ThreadPriority.BelowNormal;
            }
            _thread.Start();
        }

        [SupportedOSPlatform("macos")]
        [SupportedOSPlatform("linux")]
        public void Attach(UnixPty pty)
        {
#if ROYALTERMINAL_OUTPUT_LEASES
            pty.OutputLeaseCallback = lease => Enqueue(new Chunk(lease.Data, lease));
#else
            pty.DataReceived += (data, length) => Enqueue(new Chunk(data.AsMemory(0, length).ToArray()));
#endif
        }

        private void Enqueue(Chunk chunk)
        {
            if (FirstByteTimestamp == 0) FirstByteTimestamp = Stopwatch.GetTimestamp();
            Callbacks++;
            lock (_sync)
            {
                while (_queuedBytes >= 256 * 1024 && !_stopping) Monitor.Wait(_sync);
                _queue.Enqueue(chunk);
                _queuedBytes += chunk.Data.Length;
                Monitor.PulseAll(_sync);
            }
        }

        private void Parse()
        {
#if ROYALTERMINAL_OUTPUT_LEASES
            if (OperatingSystem.IsMacOS() && Environment.GetEnvironmentVariable("ROYALTERMINAL_BENCHMARK_PRIORITY") == "initiated")
            {
                QosRequestApplied = UnixThreadScheduling.TrySetCurrentThreadUserInitiated();
            }
#endif
            while (true)
            {
                Chunk chunk;
                lock (_sync)
                {
                    while (_queue.Count == 0 && !_stopping) Monitor.Wait(_sync);
                    if (_queue.Count == 0) return;
                    chunk = _queue.Dequeue();
                    _queuedBytes -= chunk.Data.Length;
                    Monitor.PulseAll(_sync);
                }

                foreach (byte value in chunk.Data.Span) _checksum += value;
                Bytes += chunk.Data.Length;
                LastByteTimestamp = Stopwatch.GetTimestamp();
#if ROYALTERMINAL_OUTPUT_LEASES
                chunk.Lease.Dispose();
#endif
                if (Bytes >= _target) _done.Set();
                if (_target == long.MaxValue) _interactiveByte.Set();
            }
        }

        public void WaitForBytes()
        {
            if (!_done.Wait(TimeSpan.FromSeconds(60))) throw new TimeoutException("PTY benchmark output timed out.");
            if (_checksum != 0) throw new InvalidOperationException("PTY benchmark payload was corrupted.");
        }

        public void WaitForInteractiveByte()
        {
            if (!_interactiveByte.WaitOne(TimeSpan.FromSeconds(5))) throw new TimeoutException("PTY interactive output timed out.");
        }

        public void Dispose()
        {
            lock (_sync)
            {
                _stopping = true;
                Monitor.PulseAll(_sync);
            }
            _thread.Join();
            _done.Dispose();
            _interactiveByte.Dispose();
        }
    }
}
