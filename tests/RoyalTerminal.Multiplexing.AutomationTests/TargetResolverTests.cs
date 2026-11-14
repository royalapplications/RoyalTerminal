// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Multiplexing.Automation;
using Xunit;

namespace RoyalTerminal.Multiplexing.AutomationTests;

public sealed class TargetResolverTests
{
    [Fact]
    public void PrecedenceIsExactIdThenLabelThenIndexThenSuffix()
    {
        MultiplexerTarget[] items = [new("2", "first"), new("session:abcd1234", "one"), new("session:abcd5678", "1")];
        Assert.Equal(items[0], MultiplexerTargetResolver.Resolve("2", items));
        Assert.Equal(items[2], MultiplexerTargetResolver.Resolve("1", items));
        Assert.Equal(items[1], MultiplexerTargetResolver.Resolve("ONE", items));
        Assert.Equal(items[2], MultiplexerTargetResolver.Resolve("3", items));
        Assert.Equal(items[1], MultiplexerTargetResolver.Resolve("1234", items));
    }

    [Fact]
    public void AmbiguousLabelsNeverFallThroughToAnIndex()
    {
        MultiplexerTarget[] items = [new("first", "2"), new("second", "2")];
        MultiplexerAutomationException error = Assert.Throws<MultiplexerAutomationException>(() => MultiplexerTargetResolver.Resolve("2", items));
        Assert.Equal(MultiplexerAutomationError.AmbiguousTarget, error.Code);
        Assert.Equal("first", items[0].Id);
        Assert.Equal("second", items[1].Id);
    }

    [Fact]
    public void SuffixRequiresFourCharactersAndUniqueMatching()
    {
        MultiplexerTarget[] items = [new("session:aaaaABCD", "a"), new("session:bbbbabcd", "b")];
        Assert.Equal(MultiplexerAutomationError.TargetNotFound,
            Assert.Throws<MultiplexerAutomationException>(() => MultiplexerTargetResolver.Resolve("bcd", items)).Code);
        Assert.Equal(MultiplexerAutomationError.AmbiguousTarget,
            Assert.Throws<MultiplexerAutomationException>(() => MultiplexerTargetResolver.Resolve("abcd", items)).Code);
        Assert.Equal(items[1], MultiplexerTargetResolver.Resolve("bbbbabcd", items));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("+1")]
    [InlineData("2")]
    [InlineData("missing")]
    public void InvalidOrUnknownSelectorsFail(string selector)
    {
        Assert.Equal(MultiplexerAutomationError.TargetNotFound,
            Assert.Throws<MultiplexerAutomationException>(() => MultiplexerTargetResolver.Resolve(selector, [new("abcd", "one")])).Code);
    }

    [Fact]
    public void DuplicateIdentitiesAndCandidateBudgetAreRejected()
    {
        MultiplexerTarget[] duplicates = [new("abcde", "one"), new("abcde", "two")];
        Assert.Equal(MultiplexerAutomationError.AmbiguousTarget,
            Assert.Throws<MultiplexerAutomationException>(() => MultiplexerTargetResolver.Resolve("abcde", duplicates)).Code);
        Assert.Equal(MultiplexerAutomationError.InvalidConfiguration,
            Assert.Throws<MultiplexerAutomationException>(() => MultiplexerTargetResolver.Resolve("one", duplicates, 1)).Code);
    }
}
