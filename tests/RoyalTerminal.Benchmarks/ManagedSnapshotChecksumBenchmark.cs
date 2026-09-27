// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers.Binary;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;
using RoyalTerminal.Terminal.Snapshots;

internal static class ManagedSnapshotChecksumBenchmark
{
    internal static void Run()
    {
        Console.WriteLine($"Snapshot checksum: hardware CRC available = {Sse42.IsSupported || Crc32.IsSupported}; median of 7 samples; setup excluded.");
        Console.WriteLine("Use DOTNET_EnableHWIntrinsic=0 to compare the previous runtime-word fallback with the sliced/interleaved software backend.");
        Console.WriteLine("| Payload bytes | Backend | Iterations | Milliseconds | Allocated bytes |");
        Console.WriteLine("|---:|---|---:|---:|---:|");
        foreach (int length in new[] { 0, 6, 15, 16, 17, 256, 4095, 4096, 4097, 65567, 1024 * 1024 })
        {
            byte[] bytes = new byte[length];
            new Random(0x35171A3B).NextBytes(bytes);
            int iterations = Math.Clamp(4 * 1024 * 1024 / Math.Max(length, 1), 20, 200000);
            foreach (string backend in new[] { "runtime-word", "forced-software", "automatic" })
            {
                _ = Measure(bytes, backend, iterations);
                double[] times = new double[7];
                long[] allocations = new long[7];
                for (int sample = 0; sample < times.Length; sample++)
                    (times[sample], allocations[sample]) = Measure(bytes, backend, iterations);
                Array.Sort(times); Array.Sort(allocations);
                Console.WriteLine(FormattableString.Invariant($"| {length} | {backend} | {iterations} | {times[3]:F3} | {allocations[3]} |"));
            }
        }
    }

    private static (double Milliseconds, long Allocated) Measure(byte[] bytes, string backend, int iterations)
    {
        Span<byte> header = stackalloc byte[6];
        BinaryPrimitives.WriteUInt16LittleEndian(header, (ushort)GhosttySnapshotRecordTag.Page);
        BinaryPrimitives.WriteUInt32LittleEndian(header[2..], (uint)bytes.Length);
        uint checksum = 0;
        long before = GC.GetAllocatedBytesForCurrentThread();
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < iterations; i++)
        {
            // Vary input to keep the checksum work observable in every loop.
            header[0] = (byte)i;
            checksum ^= backend switch
            {
                "runtime-word" => ~AppendRuntimeWord(AppendRuntimeWord(uint.MaxValue, header), bytes),
                "forced-software" => ~GhosttySnapshotSoftwareCrc32C.Append(
                    GhosttySnapshotSoftwareCrc32C.Append(uint.MaxValue, header), bytes),
                _ => GhosttySnapshotFraming.ComputeChecksum(header, bytes),
            };
        }
        double milliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        GC.KeepAlive(checksum);
        return (milliseconds, allocated);
    }

    // Previous production implementation, retained only as the paired baseline.
    private static uint AppendRuntimeWord(uint crc, ReadOnlySpan<byte> bytes)
    {
        while (bytes.Length >= 8)
        {
            crc = BitOperations.Crc32C(crc, BinaryPrimitives.ReadUInt64LittleEndian(bytes));
            bytes = bytes[8..];
        }
        if (bytes.Length >= 4)
        {
            crc = BitOperations.Crc32C(crc, BinaryPrimitives.ReadUInt32LittleEndian(bytes));
            bytes = bytes[4..];
        }
        foreach (byte value in bytes) crc = BitOperations.Crc32C(crc, value);
        return crc;
    }
}
