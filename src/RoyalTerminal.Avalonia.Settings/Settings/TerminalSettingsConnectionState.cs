// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Generic;
using RoyalTerminal.Terminal;

namespace RoyalTerminal.Avalonia.Settings;

public sealed class TerminalSettingsConnectionState : TerminalSettingsCategoryStateBase
{
    internal TerminalSettingsConnectionState(TerminalSettingsPanelState owner)
        : base(
            owner,
            nameof(TerminalSettingsPanelState.WorkingDirectory),
            nameof(TerminalSettingsPanelState.ShellPath),
            nameof(TerminalSettingsPanelState.PipeCommandText),
            nameof(TerminalSettingsPanelState.PipeMergeStdErrIntoStdOut),
            nameof(TerminalSettingsPanelState.RawTcpHost),
            nameof(TerminalSettingsPanelState.RawTcpPort),
            nameof(TerminalSettingsPanelState.TelnetHost),
            nameof(TerminalSettingsPanelState.TelnetPort),
            nameof(TerminalSettingsPanelState.TelnetTerminalType),
            nameof(TerminalSettingsPanelState.TelnetInitialCommand),
            nameof(TerminalSettingsPanelState.SerialPortName),
            nameof(TerminalSettingsPanelState.SerialBaudRate),
            nameof(TerminalSettingsPanelState.SerialDataBits),
            nameof(TerminalSettingsPanelState.SelectedSerialParity),
            nameof(TerminalSettingsPanelState.SelectedSerialStopBits),
            nameof(TerminalSettingsPanelState.SelectedSerialHandshake),
            nameof(TerminalSettingsPanelState.SerialNewLine),
            nameof(TerminalSettingsPanelState.SshHost),
            nameof(TerminalSettingsPanelState.SshPort),
            nameof(TerminalSettingsPanelState.SshUsername))
    {
    }

    public string WorkingDirectory
    {
        get => Owner.WorkingDirectory;
        set => Owner.WorkingDirectory = value;
    }

    public string ShellPath
    {
        get => Owner.ShellPath;
        set => Owner.ShellPath = value;
    }

    public string PipeCommandText
    {
        get => Owner.PipeCommandText;
        set => Owner.PipeCommandText = value;
    }

    public bool PipeMergeStdErrIntoStdOut
    {
        get => Owner.PipeMergeStdErrIntoStdOut;
        set => Owner.PipeMergeStdErrIntoStdOut = value;
    }

    public string RawTcpHost
    {
        get => Owner.RawTcpHost;
        set => Owner.RawTcpHost = value;
    }

    public string RawTcpPort
    {
        get => Owner.RawTcpPort;
        set => Owner.RawTcpPort = value;
    }

    public string TelnetHost
    {
        get => Owner.TelnetHost;
        set => Owner.TelnetHost = value;
    }

    public string TelnetPort
    {
        get => Owner.TelnetPort;
        set => Owner.TelnetPort = value;
    }

    public string TelnetTerminalType
    {
        get => Owner.TelnetTerminalType;
        set => Owner.TelnetTerminalType = value;
    }

    public string TelnetInitialCommand
    {
        get => Owner.TelnetInitialCommand;
        set => Owner.TelnetInitialCommand = value;
    }

    public string SerialPortName
    {
        get => Owner.SerialPortName;
        set => Owner.SerialPortName = value;
    }

    public string SerialBaudRate
    {
        get => Owner.SerialBaudRate;
        set => Owner.SerialBaudRate = value;
    }

    public string SerialDataBits
    {
        get => Owner.SerialDataBits;
        set => Owner.SerialDataBits = value;
    }

    public TerminalSerialParity SelectedSerialParity
    {
        get => Owner.SelectedSerialParity;
        set => Owner.SelectedSerialParity = value;
    }

    public TerminalSerialStopBits SelectedSerialStopBits
    {
        get => Owner.SelectedSerialStopBits;
        set => Owner.SelectedSerialStopBits = value;
    }

    public TerminalSerialHandshake SelectedSerialHandshake
    {
        get => Owner.SelectedSerialHandshake;
        set => Owner.SelectedSerialHandshake = value;
    }

    public string SerialNewLine
    {
        get => Owner.SerialNewLine;
        set => Owner.SerialNewLine = value;
    }

    public IReadOnlyList<TerminalSerialParity> SerialParityOptions => Owner.SerialParityOptions;

    public IReadOnlyList<TerminalSerialStopBits> SerialStopBitsOptions => Owner.SerialStopBitsOptions;

    public IReadOnlyList<TerminalSerialHandshake> SerialHandshakeOptions => Owner.SerialHandshakeOptions;

    public string SshHost
    {
        get => Owner.SshHost;
        set => Owner.SshHost = value;
    }

    public string SshPort
    {
        get => Owner.SshPort;
        set => Owner.SshPort = value;
    }

    public string SshUsername
    {
        get => Owner.SshUsername;
        set => Owner.SshUsername = value;
    }
}
