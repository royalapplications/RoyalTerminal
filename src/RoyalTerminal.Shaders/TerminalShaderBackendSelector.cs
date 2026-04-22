// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Shaders - Full terminal shader backend selection.

using System.Runtime.InteropServices;

namespace RoyalTerminal.Shaders;

/// <summary>
/// Selects backend kinds for compiler-backed full shader package execution.
/// </summary>
public static class TerminalShaderBackendSelector
{
    /// <summary>
    /// Resolves a backend kind from a host preference.
    /// </summary>
    /// <param name="preference">Backend preference.</param>
    /// <returns>The selected backend kind.</returns>
    public static TerminalShaderBackendKind SelectBackend(TerminalShaderBackendPreference preference)
    {
        return preference switch
        {
            TerminalShaderBackendPreference.D3D11 => TerminalShaderBackendKind.D3D11,
            TerminalShaderBackendPreference.D3D12 => TerminalShaderBackendKind.D3D12,
            TerminalShaderBackendPreference.Vulkan => TerminalShaderBackendKind.Vulkan,
            TerminalShaderBackendPreference.Metal => TerminalShaderBackendKind.Metal,
            _ => SelectPlatformDefaultBackend(),
        };
    }

    /// <summary>
    /// Creates a deterministic unavailable runtime for a selected backend.
    /// </summary>
    /// <param name="preference">Backend preference.</param>
    /// <param name="reason">Optional reason text.</param>
    /// <returns>An unavailable diagnostic runtime.</returns>
    public static TerminalShaderUnavailableRuntime CreateUnavailableRuntime(
        TerminalShaderBackendPreference preference,
        string? reason = null)
    {
        TerminalShaderBackendKind backendKind = SelectBackend(preference);
        string message = string.IsNullOrWhiteSpace(reason)
            ? $"Compiler-backed shader runtime backend '{backendKind}' is not available."
            : reason.Trim();
        return new TerminalShaderUnavailableRuntime(backendKind, message);
    }

    private static TerminalShaderBackendKind SelectPlatformDefaultBackend()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return TerminalShaderBackendKind.D3D11;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return TerminalShaderBackendKind.Metal;
        }

        return TerminalShaderBackendKind.Vulkan;
    }
}
