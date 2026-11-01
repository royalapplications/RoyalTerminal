// Copyright (c) Royal Apps. Licensed under the MIT license.
namespace RoyalTerminal.Terminal;

/// <summary>Optional capability: the identity of the actual running child, not a guessed foreground job.</summary>
public interface ITerminalChildIdentity
{
    /// <summary>Gets the current live process ID; null for unstarted, stopped or exited transports.</summary>
    int? LiveChildId { get; }
}
