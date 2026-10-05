// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Globalization;
using System.Text;

namespace RoyalTerminal.Terminal;

public sealed partial class BasicVtProcessor
{
    // Upstream Kind.max = maxInt(u8) + eight special colors (not 256 + 8).
    private const int MaximumKittyColorRequests = (255 + 8) * 2;
    private readonly record struct KittyColorRequest(int Key, byte Operation, uint Color);

    private void HandleKittyColors(ReadOnlySpan<char> payload, bool bellTerminator)
    {
        Span<KittyColorRequest> requests = stackalloc KittyColorRequest[MaximumKittyColorRequests];
        int count = 0;
        // Parse first: exceeding the accepted-request limit discards the entire
        // OSC, even if the extra token is empty/invalid. No partial mutations.
        foreach (Range range in payload.Split(';'))
        {
            if (count == requests.Length) return;
            ReadOnlySpan<char> item = payload[range];
            int equal = item.IndexOf('=');
            ReadOnlySpan<char> name = equal < 0 ? item : item[..equal];
            if (!TryKittyColorKey(name, out int key)) continue;
            ReadOnlySpan<char> value = equal < 0 ? [] : item[(equal + 1)..].Trim(' ');
            if (value.IsEmpty) requests[count++] = new(key, 0, 0);
            else if (value.SequenceEqual("?")) requests[count++] = new(key, 1, 0);
            else if (ManagedColorParser.TryParse(value, out uint color)) requests[count++] = new(key, 2, color);
        }

        StringBuilder? response = null;
        bool changed = false;
        foreach (KittyColorRequest request in requests[..count])
        {
            if (request.Key > 258) continue; // Valid, but not stored by libghostty-vt.
            bool palette = request.Key < 256;
            int selector = request.Key - 256 + 10;
            if (request.Operation == 1)
            {
                if (ResponseCallback is null) continue;
                response ??= new("\u001b]21");
                response.Append(';');
                if (palette) response.Append(request.Key.ToString(CultureInfo.InvariantCulture));
                else response.Append(selector switch { 10 => "foreground", 11 => "background", _ => "cursor" });
                response.Append('=');
                // Unlike OSC 12, Kitty's missing cursor does not fall back to FG.
                uint? color = palette ? _colors.GetPalette(request.Key) : _colors.GetDynamic(selector);
                if (color is uint value)
                    response.Append(CultureInfo.InvariantCulture, $"rgb:{(value >> 16) & 255:x2}/{(value >> 8) & 255:x2}/{value & 255:x2}");
                continue;
            }
            if (palette)
            {
                if (request.Operation == 0) _colors.ResetPalette(request.Key);
                else _colors.SetPalette(request.Key, request.Color);
            }
            else _colors.SetDynamic(selector, request.Operation == 0 ? null : request.Color);
            changed = true;
        }
        // Resolve/recolor once per OSC, while every query observes its ordered
        // position within the batch. Held output remains on its unpublished screen.
        if (changed) ApplyEffectiveTheme(_colors.GetEffectiveTheme());
        if (response is not null)
        {
            response.Append(bellTerminator ? "\a" : "\u001b\\");
            ResponseCallback?.Invoke(Encoding.ASCII.GetBytes(response.ToString()));
        }
    }

    private static bool TryKittyColorKey(ReadOnlySpan<char> name, out int key)
    {
        key = name switch
        {
            "foreground" => 256, "background" => 257, "cursor" => 258,
            "selection_foreground" => 259, "selection_background" => 260,
            "cursor_text" => 261, "visual_bell" => 262, "second_transparent_background" => 263,
            _ => -1,
        };
        return key >= 0 || ManagedColorParser.Unsigned(name, 10, 255, out key);
    }
}
