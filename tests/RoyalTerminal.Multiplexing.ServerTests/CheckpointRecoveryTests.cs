// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using RoyalTerminal.Multiplexing.Inspection;
using RoyalTerminal.Multiplexing.Server;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Transport.Multiplexing;
using Xunit;

namespace RoyalTerminal.Multiplexing.ServerTests;

public sealed class CheckpointRecoveryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FreshObserverRecoversAfterCompleteJournalEviction(bool splitUtf8)
    {
        const string token = "checkpoint-integration-0123456789-abcdefghijk";
        Factory factory = new();
        await using var app = ServerHost.Build(["--Logging:LogLevel:Default=None", "--ROYALTERMINAL_MUX_COLD_REPLAY_MIB=0"],factory,token,"http://127.0.0.1:0");
        await app.StartAsync();
        try
        {
            Uri baseUri = new(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single());
            using HttpClient http = new() { BaseAddress = baseUri }; http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",token);
            using var makeSession = await http.PostAsJsonAsync("api/sessions",new NewSession("after-eviction"),ServerJsonContext.Default.NewSession);
            makeSession.EnsureSuccessStatusCode(); using var s = JsonDocument.Parse(await makeSession.Content.ReadAsStringAsync()); Guid session = s.RootElement.GetProperty("id").GetGuid();
            using var makeBlock = await http.PostAsJsonAsync($"api/sessions/{session}/blocks",new NewBlock("wire", "pipe", new("fake",[]), Dimensions:new(20,4,0,0)),ServerJsonContext.Default.NewBlock);
            makeBlock.EnsureSuccessStatusCode(); using var b = JsonDocument.Parse(await makeBlock.Content.ReadAsStringAsync()); Guid block = b.RootElement.GetProperty("id").GetGuid();
            // CR updates are real distinct transport callbacks with a bounded screen footprint.
            for (int i=0;i<4200;i++) factory.Wire.Emit("\r."u8.ToArray());
            factory.Wire.Emit("\u001b[2J\u001b[HREADY"u8.ToArray());
            factory.Wire.Emit(splitUtf8 ? new byte[]{0xe2,0x82} : "\u001b["u8.ToArray());
            var broker=app.Services.GetRequiredService<MultiplexerBroker>();
            Assert.True(broker.GetSession(session).Blocks[0].FirstRetainedSequence>1);
            Assert.Equal(MultiplexerErrorCode.HistoryGap,Assert.Throws<MultiplexerException>(()=>broker.Attach(session,block)).Code);
            var options=new MultiplexingTransportOptions(baseUri,token,session,block,new(20,4,0,0)){Observer=true};
            var cut=await MultiplexingCheckpointClient.ReadAsync(http,options);
            Assert.Equal(broker.GetSession(session).Blocks[0].LastSequence,cut.Sequence);
            // The suffix arrives *before* the WebSocket request. Correctness needs both the cut
            // and the original pending CSI/UTF-8 state, not a screen text dump and current cursor.
            factory.Wire.Emit(splitUtf8 ? new byte[]{0xac} : "3;7HZ"u8.ToArray());
            using var snapshot=ManagedTerminalSnapshot.Restore(cut.Binary,new(){DecodeLimits=InspectingTerminalTransport.WorkspaceCheckpointLimits()});
            await using var viewer=new MultiplexingTerminalTransport();
            viewer.DataReceived+=(data,n)=>snapshot.Processor.Process(data.AsSpan(0,n));
            viewer.AuthoritativeDimensionsChanged+=d=>snapshot.Processor.ResizeScreen(d.Columns,d.Rows,d.WidthPixels,d.HeightPixels);
            await viewer.StartAsync(options with {AfterSequence=cut.Sequence});
            Assert.False(viewer.HasControl); Assert.Equal(cut.Sequence+1,viewer.LastSequence);
            Assert.Equal((int)'R',snapshot.Screen.GetViewportRow(0)[0].Codepoint);
            if(splitUtf8) Assert.Equal(0x20ac,snapshot.Screen.GetViewportRow(0)[5].Codepoint);
            else Assert.Equal((int)'Z',snapshot.Screen.GetViewportRow(2)[6].Codepoint);
            await viewer.StopAsync();
            factory.Wire.Exit(23);
            var final=await MultiplexingCheckpointClient.ReadAsync(http,options); Assert.Equal(23,final.ExitCode);
            using HttpClient anonymous=new(){BaseAddress=baseUri};
            using var denied=await anonymous.GetAsync($"api/sessions/{session}/blocks/{block}/checkpoint"); Assert.Equal(HttpStatusCode.Unauthorized,denied.StatusCode);
        }
        finally { await app.StopAsync(); }
    }
    private sealed class Factory : ITerminalTransportFactory
    {
        internal WireTransport Wire {get;}=new();
        public ITerminalTransport Create(ITerminalTransportOptions options)=>Wire;
    }
    private sealed class WireTransport:ITerminalTransport
    {
        public event Action<byte[],int>? DataReceived; public event Action<int>? ProcessExited;
        public bool IsRunning{get;private set;}
        internal void Emit(byte[] bytes)=>DataReceived?.Invoke(bytes,bytes.Length);
        internal void Exit(int code){IsRunning=false;ProcessExited?.Invoke(code);}
        public ValueTask StartAsync(ITerminalTransportOptions options,CancellationToken token=default){IsRunning=true;return ValueTask.CompletedTask;}
        public ValueTask StopAsync(){IsRunning=false;return ValueTask.CompletedTask;}
        public void SendInput(ReadOnlySpan<byte> bytes){} public void Resize(TerminalSessionDimensions dimensions){} public void Dispose(){IsRunning=false;}
    }
}
