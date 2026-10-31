// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Terminal;

namespace RoyalTerminal.Multiplexing;

/// <summary>Identities for a process launched in a local terminal. The launch window is historical: a block may subsequently move.</summary>
public readonly record struct MultiplexerProcessContext(Guid SessionId, Guid WindowId, Guid BlockId)
{
    internal ITerminalTransportOptions Bind(ITerminalTransportOptions options) => options switch
    {
        PtyTransportOptions p => p with { Environment = BindEnvironment(p.Environment) },
        PipeTransportOptions p => p with { Environment = BindEnvironment(p.Environment) },
        _ => options,
    };

    private Dictionary<string, string> BindEnvironment(IReadOnlyDictionary<string, string>? source)
    {
        var env = new Dictionary<string,string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        if (source is not null) foreach (var pair in source) env[pair.Key] = pair.Value;
        env["ROYALTERMINAL_MUX_SESSION"] = SessionId.ToString("D");
        env["ROYALTERMINAL_MUX_BLOCK"] = BlockId.ToString("D");
        env["ROYALTERMINAL_MUX_WINDOW"] = WindowId.ToString("D");
        env["ROYALTERMINAL_MUX_TOKEN"] = string.Empty;
        return env;
    }
}
