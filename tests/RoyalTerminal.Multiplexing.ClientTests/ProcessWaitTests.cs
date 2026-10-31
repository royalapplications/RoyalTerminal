// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Net;
using System.Text.Json;
using RoyalTerminal.Multiplexing.Cli;
using Xunit;

namespace RoyalTerminal.Multiplexing.ClientTests;

public sealed class ProcessWaitTests
{
    [Theory]
    [InlineData(0)] [InlineData(37)] [InlineData(130)]
    public async Task FastExitPropagatesCodeAndNeverRelaunchesOrClaimsControl(int code)
    {
        Guid s = Guid.NewGuid(), b = Guid.NewGuid(); int posts = 0;
        using HttpClient http = new(new Handler(async (request, cancellation) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method); ++posts;
            using var argv = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellation));
            Assert.Equal("--wait", argv.RootElement.GetProperty("command").GetProperty("arguments")[0].GetString());
            return Json($$"""{"id":"{{b}}","sessionId":"{{s}}","state":"Exited","exitCode":{{code}}}""");
        }));
        using var output = new StringWriter();
        Assert.Equal(code, await WorkspaceCli.RunAsync(["run", s.ToString(), "--wait", "--", "tool", "--wait"], "secret", null, TextReader.Null, output, TextWriter.Null, http, default));
        using var receipt = JsonDocument.Parse(output.ToString());
        Assert.Equal(code, receipt.RootElement.GetProperty("exitCode").GetInt32()); Assert.Equal(1, posts);
    }

    [Fact]
    public async Task WaitAcrossHistoryEvictionNeedsOnlyReadAccess()
    {
        Guid s = Guid.NewGuid(), b = Guid.NewGuid(); int calls = 0;
        using HttpClient http = new(new Handler((r, _) =>
        {
            Assert.Equal(HttpMethod.Get, r.Method); Assert.Equal($"/api/sessions/{s:D}", r.RequestUri!.AbsolutePath);
            string state = ++calls == 1 ? "\"Running\"" : "\"Exited\",\"exitCode\":23";
            return Task.FromResult(Json($$"""{"blocks":[{"id":"{{b}}","firstRetainedSequence":4000,"state":{{state}}}]}"""));
        }));
        Assert.Equal(23, await WorkspaceCli.RunAsync(["wait", s.ToString(), b.ToString()], "observer", null, TextReader.Null, TextWriter.Null, TextWriter.Null, http, default));
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task RemovedBlockIsNotAZeroExit()
    {
        using HttpClient http = new(new Handler((_, _) => Task.FromResult(Json("{\"blocks\":[]}"))));
        using var output = new StringWriter();
        Assert.Equal(1, await WorkspaceCli.RunAsync(["wait", Guid.NewGuid().ToString(), Guid.NewGuid().ToString()], "observer", null, TextReader.Null, output, TextWriter.Null, http, default));
        Assert.Equal(string.Empty, output.ToString());
    }
    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json) };
    private sealed class Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> handle) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => handle(request, cancellationToken);
    }
}
