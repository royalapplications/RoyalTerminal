// Copyright (c) Royal Apps. Licensed under the MIT license.
using System.Buffers;
using System.Globalization;
using RoyalTerminal.Terminal;

namespace RoyalTerminal.Multiplexing.Inspection;

public sealed partial class InspectingTerminalTransport
{
    /// <summary>Encode named presses using the canonical parser's current keyboard protocol, under its input lock. Does not acquire control or write to a process.</summary>
    public byte[] EncodeKeyPresses(IReadOnlyList<string> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        if(keys.Count is <1 or >64) throw new ArgumentException("Send between one and 64 key presses.",nameof(keys));
        var requests=keys.Select(ParsePress).ToArray();var output=new ArrayBufferWriter<byte>();
        lock(_gate)
        {
            ThrowIfDisposed();
            foreach(var request in requests)
            {
                if(!_processor.TryEncodeKey(request,out var bytes) || bytes.Length==0)throw new ArgumentException("Cannot encode a requested key in the current terminal mode.");
                output.Write(bytes);
            }
        }
        return output.WrittenSpan.ToArray();
    }
    private static TerminalKeyEncodingRequest ParsePress(string value)
    {
        if(string.IsNullOrWhiteSpace(value) || value.Length>64)throw new ArgumentException("Invalid named key.");
        string[] parts=value.ToLowerInvariant().Split('+');TerminalModifiers mods=TerminalModifiers.None;
        foreach(string part in parts[..^1])
        {
            var mod=part switch {"ctrl" or "control"=>TerminalModifiers.Control,"alt" or "option"=>TerminalModifiers.Alt,"shift"=>TerminalModifiers.Shift,"cmd" or "meta" or "super"=>TerminalModifiers.Meta,_=>throw new ArgumentException("Unknown key modifier.")};
            if((mods&mod)!=0)throw new ArgumentException("Duplicate key modifier."); mods|=mod;
        }
        string key=parts[^1];string? text=null; uint cp=0;bool shifted=(mods&TerminalModifiers.Shift)!=0;
        if(key.Length==1 && key[0] is >= 'a' and <= 'z') {cp=key[0];text=shifted?key.ToUpperInvariant():key;key=key.ToUpperInvariant();}
        else if(key.Length==1 && key[0] is >= '0' and <= '9') {cp=key[0];text=shifted?")!@#$%^&*("[key[0]-'0'].ToString():key;key="D"+key;}
        else if(key.Length>=2 && key[0]=='f' && int.TryParse(key.AsSpan(1),NumberStyles.None,CultureInfo.InvariantCulture,out var fn) && fn is >=1 and <=24) key="F"+fn.ToString(CultureInfo.InvariantCulture);
        else if(key=="space") { text=" ";cp=' ';key="Space"; }
        else {key=key switch {"esc" or "escape"=>"Escape","enter" or "return"=>"Return","tab"=>"Tab","back" or "backspace"=>"Back","del" or "delete"=>"Delete","insert"=>"Insert","left"=>"Left","right"=>"Right","up"=>"Up","down"=>"Down","home"=>"Home","end"=>"End","pageup"=>"PageUp","pagedown"=>"PageDown",_=>throw new ArgumentException("Unknown named key. Use send for arbitrary literal text.")};}
        return new(key,TerminalInputAction.Press,text,mods,UnshiftedCodepoint:cp,ConsumedModifiers:shifted&&text!=null?TerminalModifiers.Shift:TerminalModifiers.None);
    }
}
