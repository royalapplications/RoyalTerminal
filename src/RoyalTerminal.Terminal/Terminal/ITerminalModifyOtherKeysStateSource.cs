// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Terminal;

/// <summary>Live xterm modifyOtherKeys mode-2 state, independent of DEC and Kitty mode banks.</summary>
public interface ITerminalModifyOtherKeysStateSource
{
    /// <summary>Gets whether legacy modified keys use CSI 27 numeric reporting.</summary>
    bool ModifyOtherKeys2 { get; }
}
