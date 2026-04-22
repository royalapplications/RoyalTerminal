// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Demo - Full shader package runtime composition.

using RoyalTerminal.Shaders;
using RoyalTerminal.Shaders.D3D11;

namespace RoyalTerminal.Demo.Services;

internal static class TerminalShaderDemoExecutorFactory
{
    public static ITerminalShaderPackageExecutor? TryCreate(TerminalShaderPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);

        if (!OperatingSystem.IsWindows() ||
            !TerminalShaderD3D11Compiler.IsSupported ||
            !TerminalShaderD3D11Runtime.IsSupported)
        {
            return null;
        }

        try
        {
            return new TerminalShaderCompilerRuntimePackageExecutor(
                new TerminalShaderD3D11Compiler(),
                new TerminalShaderD3D11Runtime(),
                new TerminalShaderCompilationOptions(TerminalShaderBackendKind.D3D11));
        }
        catch (Exception ex) when (ex is PlatformNotSupportedException or InvalidOperationException ||
                                   string.Equals(ex.GetType().FullName, "SharpGen.Runtime.SharpGenException", StringComparison.Ordinal))
        {
            return null;
        }
    }
}
