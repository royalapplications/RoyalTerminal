// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.GhosttySharp.Native;
using Xunit;

namespace RoyalTerminal.Tests;

public class GhosttyColorPaletteMaskTests
{
    [Fact]
    public void SetClearAndIsSet_WorkAcrossWordBoundaries()
    {
        byte[] indices = [0, 63, 64, 127, 128, 191, 192, 255];
        GhosttyVtNative.GhosttyColorPaletteMask mask = default;

        foreach (byte index in indices)
        {
            Assert.False(mask.IsSet(index));
            mask.Set(index);
            Assert.True(mask.IsSet(index));
        }

        foreach (byte index in indices)
        {
            mask.Clear(index);
            Assert.False(mask.IsSet(index));

            foreach (byte remainingIndex in indices)
            {
                if (remainingIndex > index)
                {
                    Assert.True(mask.IsSet(remainingIndex));
                }
            }
        }
    }
}
