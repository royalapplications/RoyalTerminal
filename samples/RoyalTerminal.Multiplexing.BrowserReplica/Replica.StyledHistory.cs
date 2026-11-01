// Copyright (c) Royal Apps. Licensed under the MIT license.
using System.Buffers;
using System.Runtime.InteropServices.JavaScript;
using System.Text;
using System.Text.Json;
using RoyalTerminal.Avalonia.Rendering;

namespace RoyalTerminal.BrowserReplica;

public static partial class Replica
{
    private readonly record struct PaintStyle(uint Foreground,uint Background,int Flags,int Underline,int UnderlineColor,bool Overline)
    {
        internal static PaintStyle From(in TerminalCell c,uint defaultBackground)
        {
            uint fg=c.Foreground & 0xffffff, bg=(c.HasBackground ? c.Background : defaultBackground) & 0xffffff;
            if((c.Attributes & CellAttributes.Inverse)!=0)(fg,bg)=(bg,fg);
            return new(fg,bg,(int)c.Attributes,(int)c.UnderlineStyle,c.HasUnderlineColor?(int)(c.UnderlineColor&0xffffff):-1,(c.Decorations&CellDecorations.Overline)!=0);
        }
    }

    /// <summary>Exports bounded runs of text and numeric paint properties. No terminal markup, URI or escape program crosses this boundary.</summary>
    [JSExport]
    public static string StyledHistory(int id)
    {
        const int maxBytes=4<<20,maxRuns=32768,maxCells=1<<20,maxRunChars=4096;
        var state=States[id];var screen=state.Screen;
        bool alternate=state.Processor.AlternateScreen;
        int rows=alternate?screen.ViewportRows:screen.TotalRows;
        int cells=0,runs=0;
        var bytes=new ArrayBufferWriter<byte>();using var w=new Utf8JsonWriter(bytes);
        w.WriteStartObject();w.WriteNumber("background",screen.DefaultBackground & 0xffffff);w.WriteStartArray("rows");
        var text=new StringBuilder(128); Span<char> scalar=stackalloc char[2];
        for(int y=0;y<rows;y++)
        {
            var row=alternate?screen.GetViewportRow(y):screen.GetRow(y);
            cells+=row.ReadOnlyCells.Length;if(cells>maxCells)throw TooLarge();
            w.WriteStartArray();PaintStyle? active=null;
            foreach(ref readonly var cell in row.ReadOnlyCells)
            {
                if(cell.Width==0||cell.IsWideSpacerHead)continue;
                var style=PaintStyle.From(cell,screen.DefaultBackground);
                int length=cell.Grapheme?.Length??0;
                if(length>maxRunChars)throw TooLarge();
                if(active!=style||text.Length+Math.Max(2,length)>maxRunChars){if(active is {} before)Flush(before);active=style;}
                if(length>0)text.Append(cell.Grapheme);
                else if(cell.Codepoint==0)text.Append(' ');
                else {var r=Rune.TryCreate(cell.Codepoint,out var valid)?valid:Rune.ReplacementChar;text.Append(scalar[..r.EncodeToUtf16(scalar)]);}
            }
            if(active is {} last)Flush(last);
            w.WriteEndArray();
        }
        w.WriteEndArray();w.WriteEndObject();w.Flush();if(bytes.WrittenCount>maxBytes)throw TooLarge();
        return Encoding.UTF8.GetString(bytes.WrittenSpan);

        void Flush(PaintStyle s)
        {
            if(++runs>maxRuns || w.BytesCommitted+w.BytesPending>maxBytes)throw TooLarge();
            w.WriteStartArray();w.WriteNumberValue(s.Foreground);w.WriteNumberValue(s.Background);w.WriteNumberValue(s.Flags);w.WriteNumberValue(s.Underline);
            w.WriteNumberValue(s.UnderlineColor);w.WriteBooleanValue(s.Overline);w.WriteStringValue(text.ToString());w.WriteEndArray();text.Clear();
        }
        static InvalidDataException TooLarge()=>new("Formatting exceeds the history view budget. The plain transcript remains available.");
    }
}
