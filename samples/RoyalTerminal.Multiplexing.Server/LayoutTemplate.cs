// Copyright (c) Royal Apps. Licensed under the MIT license.
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using RoyalTerminal.Terminal;

namespace RoyalTerminal.Multiplexing.Server;

internal sealed record LayoutReceipt(Guid WindowId, Guid[] BlockIds, long Revision);
[JsonSourceGenerationOptions(PropertyNamingPolicy=JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(LayoutReceipt))]
internal partial class LayoutTemplateJson : JsonSerializerContext;

internal abstract record LayoutTemplate
{
    internal sealed record Leaf(NewBlock Block, string? InitialInput) : LayoutTemplate;
    internal sealed record Split(MultiplexerSplitOrientation Orientation, double Ratio, LayoutTemplate First, LayoutTemplate Second) : LayoutTemplate;

    internal static LayoutTemplate Parse(Guid session, JsonElement root)
    {
        int count=0;
        return Visit(root,0);
        LayoutTemplate Visit(JsonElement json,int depth)
        {
            if (depth>16 || json.ValueKind!=JsonValueKind.Object) throw new ArgumentException("Invalid layout depth or node.");
            var a=new ApiArguments(json);
            if (json.TryGetProperty("flavor",out _))
            {
                a.Validate("flavor","options","label");
                if (++count>16 || a.String("flavor")!="com.superlogical.terminal.shell") throw new ArgumentException("A template supports at most 16 shell leaves.");
                var leaf=ReadLeaf(a);
                _=WorkspaceOperations.Prepare(session,leaf.Block);
                return leaf;
            }
            a.Validate("orientation","ratio","first","second");
            double ratio=a.Number("ratio",.5);
            if (!(ratio>0 && ratio<1)) throw new ArgumentException("A split ratio must be strictly between 0 and 1.");
            return new Split(ParseAxis(a.String("orientation")),ratio,Visit(a.Object("first"),depth+1),Visit(a.Object("second"),depth+1));
        }
    }
    private static Leaf ReadLeaf(ApiArguments a)
    {
        TerminalCommandSpec? command=null; string? cwd=null,input=null;
        if(a.Value.TryGetProperty("options",out _))
        {
            var opt=new ApiArguments(a.Object("options")); opt.Validate("command","cwd","initial_input","shell","exit");
            if(opt.Value.TryGetProperty("cwd",out _)) cwd=opt.String("cwd");
            if(opt.Value.TryGetProperty("initial_input",out _)) { input=opt.String("initial_input"); if(Encoding.UTF8.GetByteCount(input)>65536) throw new ArgumentException("Initial input exceeds 64 KiB."); }
            if(opt.Value.TryGetProperty("command",out var argv))
            {
                if(argv.ValueKind!=JsonValueKind.Array || argv.GetArrayLength() is <1 or >257 || argv.EnumerateArray().Any(x=>x.ValueKind!=JsonValueKind.String)) throw new ArgumentException("Specify an argv array, not shell text.");
                var parts=argv.EnumerateArray().Select(x=>x.GetString()!).ToArray(); command=new(parts[0],parts[1..]);
            }
            if(opt.Value.TryGetProperty("shell",out _) && (opt.String("shell")!="none" || command is null)) throw new ArgumentException("Use shell='none' with command, or omit shell.");
            if(opt.Value.TryGetProperty("exit",out _)) { var exit=new ApiArguments(opt.Object("exit")); exit.Validate("on_completion"); if(!exit.Value.TryGetProperty("on_completion",out var value)||value.ValueKind!=JsonValueKind.False) throw new ArgumentException("This host retains exited terminals. Set on_completion=false or omit exit."); }
        }
        return new(new NewBlock(Label:a.String("label","Shell"),Command:command,WorkingDirectory:cwd),input);
    }
    internal static MultiplexerSplitOrientation ParseAxis(string value)=>value switch {"horizontal"=>MultiplexerSplitOrientation.Horizontal,"vertical"=>MultiplexerSplitOrientation.Vertical,_=>throw new ArgumentException("Invalid orientation.")};
}
