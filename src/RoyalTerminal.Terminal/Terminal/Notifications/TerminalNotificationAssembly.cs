// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers;

namespace RoyalTerminal.Terminal;

internal sealed class TerminalNotificationAssembly
{
    private readonly ArrayBufferWriter<byte> _title = new(), _body = new(), _buttons = new(), _icon = new();
    private readonly List<string> _names = new(), _types = new();
    internal string? IconId, ApplicationName;
    internal string Occasion = "always", Sound = "system";
    internal bool Focus = true, Report, ReportClose, Bell;
    internal int Urgency = 1, Expiry = -1;
    internal int RetainedBytes => _title.WrittenCount + _body.WrittenCount + _buttons.WrittenCount + _icon.WrittenCount + MetadataBytes;
    private int MetadataBytes
    {
        get
        {
            int count = (IconId?.Length ?? 0) + (ApplicationName?.Length ?? 0) + Occasion.Length + Sound.Length;
            foreach (string name in _names) count += name.Length;
            foreach (string type in _types) count += type.Length;
            return count * 2;
        }
    }

    internal bool Append(TerminalNotificationMetadata metadata, ReadOnlySpan<byte> data, bool bell)
    {
        ArrayBufferWriter<byte> target = metadata.Payload switch
        {
            "body" => _body, "buttons" => _buttons, "icon" => _icon, _ => _title,
        };
        int limit = metadata.Payload == "icon" ? 1024 * 1024 : 64 * 1024;
        if (data.Length > limit - target.WrittenCount) return false;
        target.Write(data);
        if (metadata['a'] is { } actions) (Focus, Report) = TerminalNotificationMetadata.Actions(actions);
        if (metadata['c'] is { } close) ReportClose = close == "1";
        if (metadata['f'] is { } application) ApplicationName = TerminalNotificationMetadata.DecodeText(application);
        if (metadata['g'] is { } icon) IconId = TerminalNotificationMetadata.Identifier(icon);
        if (metadata['o'] is { } occasion) Occasion = occasion is "invisible" or "unfocused" ? occasion : "always";
        if (metadata['s'] is { } sound) Sound = TerminalNotificationMetadata.DecodeText(sound) ?? "system";
        if (metadata['u'] is { } urgency) Urgency = urgency == "0" ? 0 : urgency == "2" ? 2 : 1;
        if (metadata['w'] is { } expiry) Expiry = TerminalNotificationMetadata.Expiry(expiry);
        foreach (string name in metadata.IconNames) if (_names.Count < 32) _names.Add(name);
        foreach (string type in metadata.Types) if (_types.Count < 32) _types.Add(type);
        Bell = bell;
        return MetadataBytes <= 64 * 1024;
    }

    internal byte[] CopyIcon() => _icon.WrittenSpan.ToArray();

    internal TerminalNotificationRequest? CreateRequest(Guid? replaces, ReadOnlyMemory<byte> icon)
    {
        string? title = TerminalNotificationMetadata.PlainText(_title.WrittenSpan);
        string? body = TerminalNotificationMetadata.PlainText(_body.WrittenSpan);
        string? buttons = TerminalNotificationMetadata.PlainText(_buttons.WrittenSpan);
        if (title is null || body is null || buttons is null || title.Length == 0 && body.Length == 0) return null;
        if (title.Length == 0) { title = body; body = string.Empty; }
        // Bound the OS action count without changing the one-based index mapping.
        string[] labels = buttons.Length == 0 ? [] : buttons.Split('\u2028', 33);
        if (labels.Length > 32) Array.Resize(ref labels, 32);
        return new(Guid.NewGuid(), replaces, title, body, ApplicationName,
            _types.AsReadOnly(), _names.AsReadOnly(), icon, Array.AsReadOnly(labels), Sound, Urgency, Expiry);
    }
}
