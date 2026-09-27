// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.GhosttySharp.Native;
using Xunit;
using static RoyalTerminal.GhosttySharp.Native.GhosttyVtNative;

namespace RoyalTerminal.Tests;

public sealed class GhosttyRenderOverscanTypesTests
{
    [Fact]
    public unsafe void NewRenderTypesMatchHeaderStorageAndSelectors()
    {
        GhosttyRenderStateOverscan overscan = default;
        GhosttyRenderStateRowId id = default;
        Assert.Equal(4, sizeof(GhosttyRenderStateOverscan));
        Assert.Equal(16, sizeof(GhosttyRenderStateRowId));
        Assert.Equal(0, (byte*)&overscan.Above - (byte*)&overscan);
        Assert.Equal(2, (byte*)&overscan.Below - (byte*)&overscan);
        Assert.Equal(0, (byte*)id.Bits - (byte*)&id);
        Assert.Equal(20, (int)GhosttyRenderStateData.Overscan);
        Assert.Equal(21, (int)GhosttyRenderStateData.OverscanRequest);
        Assert.Equal(1, (int)GhosttyRenderStateOption.Overscan);
        Assert.Equal(6, (int)GhosttyRenderStateRowData.ViewportY);
        Assert.Equal(7, (int)GhosttyRenderStateRowData.Id);
    }

    [Fact]
    public unsafe void RowIdentityUsesBothWordsAndValueCopyOwnership()
    {
        GhosttyRenderStateRowId zero = default;
        Assert.False(zero.IsValid);
        GhosttyRenderStateRowId id = default;
        id.Bits[0] = 17;
        id.Bits[1] = 29;
        GhosttyRenderStateRowId copy = id;
        Assert.True(copy.IsValid);
        Assert.True(id == copy);
        Assert.True(id.Equals((object)copy));
        Assert.False(id.Equals(null));
        Assert.False(id.Equals(new object()));
        Assert.Equal(id.GetHashCode(), copy.GetHashCode());
        Dictionary<GhosttyRenderStateRowId, int> cache = new() { [id] = 73 };
        Assert.Equal(73, cache[copy]);
        copy.Bits[0]++;
        Assert.True(id != copy);
        copy = id;
        copy.Bits[1]++;
        Assert.True(id != copy);
        Assert.Equal(17ul, id.Bits[0]);
        Assert.Equal(29ul, id.Bits[1]);
        zero.Bits[1] = 1;
        Assert.True(zero.IsValid);
    }

    [Fact]
    public unsafe void TypedIdentityComparisonAndHashingDoNotAllocate()
    {
        GhosttyRenderStateRowId id = default;
        id.Bits[0] = 17;
        id.Bits[1] = 29;
        GhosttyRenderStateRowId copy = id;
        _ = id.Equals(copy);
        _ = id.GetHashCode();
        bool equal = true;
        int checksum = 0;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
        {
            equal &= id == copy;
            checksum ^= id.GetHashCode();
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(equal);
        Assert.Equal(0, checksum);
        Assert.Equal(0, allocated);
    }
}
