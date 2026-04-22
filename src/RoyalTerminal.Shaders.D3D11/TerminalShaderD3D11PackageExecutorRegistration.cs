// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Shaders.D3D11 - Direct3D 11 shader runtime registration.

using RoyalTerminal.Shaders;
using SharpGen.Runtime;

namespace RoyalTerminal.Shaders.D3D11;

/// <summary>
/// Creates package-executor registrations for the Direct3D 11 shader backend.
/// </summary>
public static class TerminalShaderD3D11PackageExecutorRegistration
{
    /// <summary>
    /// Creates a Direct3D 11 package-executor registration.
    /// </summary>
    /// <returns>A D3D11 package-executor registration.</returns>
    public static TerminalShaderPackageExecutorRegistration Create()
    {
        return new TerminalShaderPackageExecutorRegistration(
            TerminalShaderBackendKind.D3D11,
            TerminalShaderCompilerKind.D3DCompiler,
            "Direct3D 11 / D3DCompiler",
            TerminalShaderD3D11Compiler.IsSupported && TerminalShaderD3D11Runtime.IsSupported,
            static context =>
            {
                try
                {
                    TerminalShaderCompilationOptions options = new(
                        TerminalShaderBackendKind.D3D11,
                        TerminalShaderCompilerKind.D3DCompiler,
                        context.Options.Defines,
                        context.Options.DebugName);
                    return new TerminalShaderCompilerRuntimePackageExecutor(
                        new TerminalShaderD3D11Compiler(),
                        new TerminalShaderD3D11Runtime(),
                        options,
                        context.IncludeProvider);
                }
                catch (Exception ex) when (ex is PlatformNotSupportedException or
                                           InvalidOperationException or
                                           SharpGenException)
                {
                    return null;
                }
            });
    }
}
