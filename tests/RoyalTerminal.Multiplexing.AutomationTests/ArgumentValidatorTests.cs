// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Multiplexing.Automation;
using Xunit;
using static RoyalTerminal.Multiplexing.AutomationTests.AutomationTestData;

namespace RoyalTerminal.Multiplexing.AutomationTests;

public sealed class ArgumentValidatorTests
{
    [Theory]
    [InlineData("null", "{\"type\":[\"string\",\"null\"]}")]
    [InlineData("\"😀\"", "{\"type\":\"string\",\"minLength\":1,\"maxLength\":1}")]
    [InlineData("[1,2,3]", "{\"type\":\"array\",\"items\":{\"type\":\"integer\"},\"minItems\":3,\"maxItems\":3}")]
    [InlineData("1.000", "{\"type\":\"integer\"}")]
    [InlineData("1e3", "{\"type\":\"integer\"}")]
    [InlineData("120e-1", "{\"type\":\"integer\"}")]
    [InlineData("0.0e-100", "{\"type\":\"integer\"}")]
    [InlineData("9007199254740993", "{\"type\":\"integer\",\"minimum\":9007199254740993}")]
    [InlineData("{\"name\":\"a\"}", "{\"type\":\"object\",\"additionalProperties\":{\"type\":\"string\"},\"maxProperties\":1}")]
    [InlineData("\"a\"", "{\"enum\":[\"a\",\"b\"]}")]
    public void ValidArgumentsRespectTypedConstraints(string value, string schema)
        => MultiplexerArgumentValidator.Validate(Json(value), Json(schema));

    [Theory]
    [InlineData("1.000000000000000001", "{\"type\":\"integer\"}")]
    [InlineData("12e-1", "{\"type\":\"integer\"}")]
    [InlineData("9007199254740993", "{\"type\":\"integer\",\"maximum\":9007199254740992}")]
    [InlineData("{\"name\":1}", "{\"type\":\"object\",\"additionalProperties\":false}")]
    [InlineData("{}", "{\"type\":\"object\",\"required\":[\"name\"]}")]
    [InlineData("[1,\"x\"]", "{\"type\":\"array\",\"items\":{\"type\":\"integer\"}}")]
    [InlineData("\"ab\"", "{\"type\":\"string\",\"maxLength\":1}")]
    [InlineData("\"c\"", "{\"enum\":[\"a\",\"b\"]}")]
    [InlineData("2", "{\"const\":1}")]
    [InlineData("2", "false")]
    [InlineData("1e999", "{\"type\":\"number\"}")]
    [InlineData("{\"a\":1,\"a\":2}", "true")]
    public void InvalidArgumentsAreRejected(string value, string schema)
        => Assert.Equal(MultiplexerAutomationError.InvalidConfiguration,
            Assert.Throws<MultiplexerAutomationException>(() => MultiplexerArgumentValidator.Validate(Json(value), Json(schema))).Code);

    [Theory]
    [InlineData("{\"type\":\"unknown\"}")]
    [InlineData("{\"type\":[\"string\",\"string\"]}")]
    [InlineData("{\"$ref\":\"https://example.com/schema\"}")]
    [InlineData("{\"required\":[\"a\",\"a\"]}")]
    [InlineData("{\"minLength\":\"invalid\"}")]
    [InlineData("{\"minimum\":1e999}")]
    [InlineData("{\"properties\":[]}")]
    [InlineData("{\"additionalProperties\":\"yes\"}")]
    [InlineData("{\"enum\":[]}")]
    public void UnsupportedOrMalformedSchemaNeverSilentlyWeakensValidation(string schema)
        => Assert.Throws<MultiplexerAutomationException>(() => MultiplexerArgumentValidator.Validate(Json("{}"), Json(schema)));

    [Fact]
    public void DepthAndByteBudgetsAreCheckedBeforeRecursiveValidation()
    {
        string deep = new string('[', 34) + "0" + new string(']', 34);
        Assert.Throws<MultiplexerAutomationException>(() => MultiplexerArgumentValidator.Validate(Json(deep), Json("true")));
        Assert.Throws<MultiplexerAutomationException>(() => MultiplexerArgumentValidator.Validate(Json("\"ąąąą\""), Json("true"), 8));
    }
}
