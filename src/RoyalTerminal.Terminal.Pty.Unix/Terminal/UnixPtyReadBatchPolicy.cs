// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Diagnostics;

namespace RoyalTerminal.Terminal;

internal static class UnixPtyReadBatchPolicy
{
    public const int BufferCount = 4;
    public const int BufferCapacity = 64 * 1024;
    public const int BridgeThreshold = 1024;
    public const int BridgeSpinMax = 16;
    public const int BridgePollTimeoutMilliseconds = 1;
    public const int IdlePollTimeoutMilliseconds = 100;

    private static readonly long GatherBudgetTicks =
        Math.Max(1, (long)(Stopwatch.Frequency * 0.003));

    public static bool ShouldDispatchAfterRead(int bytesRead)
    {
        return bytesRead > 0 && bytesRead < BridgeThreshold;
    }

    public static bool ShouldBridgeAfterWouldBlock(int gatheredBytes)
    {
        return gatheredBytes >= BridgeThreshold;
    }

    public static bool IsGatherBudgetExpired(long startTimestamp, long currentTimestamp)
    {
        return currentTimestamp - startTimestamp >= GatherBudgetTicks;
    }
}
