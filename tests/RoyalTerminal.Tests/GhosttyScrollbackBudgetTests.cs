// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public class GhosttyScrollbackBudgetTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void FromRows_DisablesNativeScrollback_ForNonPositiveLimits(int scrollbackRows)
    {
        Assert.Equal((nuint)0, GhosttyScrollbackBudget.FromRows(80, 24, scrollbackRows));
    }

    [Theory]
    [InlineData(80, 573)]
    [InlineData(400, 115)]
    [InlineData(46_439, 1)]
    [InlineData(int.MaxValue, 1)]
    public void RowsPerPage_ModelsPinnedGhosttyStandardPage(int columns, ulong expectedRows)
    {
        Assert.Equal(expectedRows, GhosttyScrollbackBudget.RowsPerPage(columns));
    }

    [Fact]
    public void LineLimitFromRows_AddsOnePageOfPruningSlack()
    {
        nuint limit = GhosttyScrollbackBudget.LineLimitFromRows(400, 30_000);

        Assert.Equal((nuint)30_115, limit);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void LineLimitFromRows_DisablesNativeScrollback_ForNonPositiveLimits(int scrollbackRows)
    {
        Assert.Equal((nuint)0, GhosttyScrollbackBudget.LineLimitFromRows(80, scrollbackRows));
    }

    [Fact]
    public void FromRows_ConvertsOrdinaryTerminalRowsToPageBudget()
    {
        nuint budget = GhosttyScrollbackBudget.FromRows(80, 24, 10_000);

        Assert.Equal((nuint)11_128_832, budget);
    }

    [Fact]
    public void FromRows_ConvertsWideThirtyThousandRowLimitToPageBudget()
    {
        nuint budget = GhosttyScrollbackBudget.FromRows(400, 24, 30_000);

        Assert.Equal((nuint)154_046_464, budget);
    }

    [Fact]
    public void FromRows_AddsGuardPageAcrossExactPageBoundary()
    {
        const int viewportRows = 24;
        int scrollbackRows = checked((int)GhosttyScrollbackBudget.RowsPerPage(80) - viewportRows);

        nuint budget = GhosttyScrollbackBudget.FromRows(80, viewportRows, scrollbackRows);

        Assert.Equal((nuint)1_171_456, budget);
    }

    [Fact]
    public void FromRows_HandlesMaximumInputsWithoutOverflow()
    {
        nuint budget = GhosttyScrollbackBudget.FromRows(46_439, int.MaxValue, int.MaxValue);

        if (nuint.Size == sizeof(uint))
        {
            Assert.Equal(nuint.MaxValue, budget);
            return;
        }

        ulong requiredPages = (ulong)int.MaxValue * 2UL + 1UL;
        Assert.Equal((nuint)(requiredPages * GhosttyScrollbackBudget.StandardPageBytes), budget);
    }
}
