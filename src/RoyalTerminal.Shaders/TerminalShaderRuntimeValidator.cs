// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Shaders - Full terminal shader runtime validation.

namespace RoyalTerminal.Shaders;

/// <summary>
/// Validates shader package requirements against runtime backend capabilities.
/// </summary>
public static class TerminalShaderRuntimeValidator
{
    /// <summary>
    /// Validates that a package can run on a backend with the supplied capabilities.
    /// </summary>
    /// <param name="package">Shader package.</param>
    /// <param name="capabilities">Backend capabilities.</param>
    /// <returns>The validation result.</returns>
    public static TerminalShaderPackageValidationResult ValidateCapabilities(
        TerminalShaderPackage package,
        TerminalShaderBackendCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(capabilities);

        List<TerminalShaderDiagnostic> diagnostics = [];
        for (int i = 0; i < package.Passes.Count; i++)
        {
            TerminalShaderPass pass = package.Passes[i];
            if (pass.Stage == TerminalShaderStage.Pixel && !capabilities.SupportsPixelShaders)
            {
                diagnostics.Add(new TerminalShaderDiagnostic(
                    TerminalShaderDiagnosticSeverity.Error,
                    "RTSHADERRUNTIME020",
                    $"Backend '{capabilities.BackendKind}' does not support pixel shader pass '{pass.Name}'."));
            }

            if (pass.Stage == TerminalShaderStage.Compute && !capabilities.SupportsComputeShaders)
            {
                diagnostics.Add(new TerminalShaderDiagnostic(
                    TerminalShaderDiagnosticSeverity.Error,
                    "RTSHADERRUNTIME021",
                    $"Backend '{capabilities.BackendKind}' does not support compute shader pass '{pass.Name}'."));
            }

            for (int outputIndex = 0; outputIndex < pass.Outputs.Count; outputIndex++)
            {
                TerminalShaderPassOutput output = pass.Outputs[outputIndex];
                if (IsUav(output.Kind) && !capabilities.SupportsUavResources)
                {
                    diagnostics.Add(new TerminalShaderDiagnostic(
                        TerminalShaderDiagnosticSeverity.Error,
                        "RTSHADERRUNTIME022",
                        $"Backend '{capabilities.BackendKind}' does not support UAV output '{output.Name}'."));
                }
            }
        }

        for (int i = 0; i < package.Resources.Count; i++)
        {
            TerminalShaderResourceBinding resource = package.Resources[i];
            if (IsUav(resource.Kind) && !capabilities.SupportsUavResources)
            {
                diagnostics.Add(new TerminalShaderDiagnostic(
                    TerminalShaderDiagnosticSeverity.Error,
                    "RTSHADERRUNTIME023",
                    $"Backend '{capabilities.BackendKind}' does not support UAV resource '{resource.Name}'."));
            }
        }

        return diagnostics.Count == 0
            ? TerminalShaderPackageValidationResult.Success
            : new TerminalShaderPackageValidationResult(diagnostics);
    }

    private static bool IsUav(TerminalShaderResourceKind kind)
    {
        return kind is TerminalShaderResourceKind.UavTexture2D or TerminalShaderResourceKind.UavBuffer;
    }
}
