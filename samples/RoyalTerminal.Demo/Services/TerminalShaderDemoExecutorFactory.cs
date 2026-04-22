// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Demo - Full shader package runtime composition.

using RoyalTerminal.Shaders;
using RoyalTerminal.Shaders.D3D11;

namespace RoyalTerminal.Demo.Services;

internal static class TerminalShaderDemoExecutorFactory
{
    private static readonly TerminalShaderPackageExecutorRegistry Registry = CreateRegistry();

    public static ITerminalShaderPackageExecutor? TryCreate(TerminalShaderPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);

        _ = package;
        TerminalShaderPackageExecutorCreationResult result = Registry.TryCreate(TerminalShaderBackendPreference.D3D11);
        return result.Executor;
    }

    private static TerminalShaderPackageExecutorRegistry CreateRegistry()
    {
        TerminalShaderPackageExecutorRegistry registry = new();
        registry.Register(TerminalShaderD3D11PackageExecutorRegistration.Create());
        return registry;
    }
}
