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

    /// <summary>
    /// Validates runtime frame resources against package requirements and optional backend limits.
    /// </summary>
    /// <param name="package">Shader package.</param>
    /// <param name="frame">Frame request.</param>
    /// <param name="capabilities">Optional backend capabilities.</param>
    /// <returns>The validation result.</returns>
    public static TerminalShaderPackageValidationResult ValidateFrameResources(
        TerminalShaderPackage package,
        TerminalShaderFrameRequest frame,
        TerminalShaderBackendCapabilities? capabilities = null)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(frame);

        List<TerminalShaderDiagnostic> diagnostics = [];
        Dictionary<string, TerminalShaderResourceValue> values = new(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < frame.Resources.Count; i++)
        {
            TerminalShaderResourceValue value = frame.Resources[i];
            if (!values.TryAdd(value.Name, value))
            {
                diagnostics.Add(new TerminalShaderDiagnostic(
                    TerminalShaderDiagnosticSeverity.Error,
                    "RTSHADERRUNTIME030",
                    $"Frame contains duplicate runtime resource '{value.Name}'."));
            }

            ValidateResourceSize(value, capabilities, diagnostics);
        }

        for (int i = 0; i < package.Resources.Count; i++)
        {
            TerminalShaderResourceBinding resource = package.Resources[i];
            if (resource.Source != TerminalShaderResourceSource.External)
            {
                continue;
            }

            if (!values.TryGetValue(resource.Name, out TerminalShaderResourceValue? value))
            {
                if (!resource.Optional)
                {
                    diagnostics.Add(new TerminalShaderDiagnostic(
                        TerminalShaderDiagnosticSeverity.Error,
                        "RTSHADERRUNTIME031",
                        $"Required external shader resource '{resource.Name}' was not supplied for the frame."));
                }

                continue;
            }

            if (!IsCompatibleResourceKind(resource.Kind, value.Kind))
            {
                diagnostics.Add(new TerminalShaderDiagnostic(
                    TerminalShaderDiagnosticSeverity.Error,
                    "RTSHADERRUNTIME032",
                    $"Runtime resource '{value.Name}' has kind '{value.Kind}', but package resource '{resource.Name}' requires '{resource.Kind}'."));
            }
        }

        if (capabilities is not null)
        {
            ValidateOutputSizes(package, frame, capabilities, diagnostics);
        }

        return diagnostics.Count == 0
            ? TerminalShaderPackageValidationResult.Success
            : new TerminalShaderPackageValidationResult(diagnostics);
    }

    private static bool IsUav(TerminalShaderResourceKind kind)
    {
        return kind is TerminalShaderResourceKind.UavTexture2D or TerminalShaderResourceKind.UavBuffer;
    }

    private static bool IsCompatibleResourceKind(
        TerminalShaderResourceKind expected,
        TerminalShaderResourceKind actual)
    {
        if (expected == actual)
        {
            return true;
        }

        return expected switch
        {
            TerminalShaderResourceKind.Texture2D => actual is
                TerminalShaderResourceKind.TerminalFramebuffer or
                TerminalShaderResourceKind.RenderTarget or
                TerminalShaderResourceKind.HistoryTexture,
            TerminalShaderResourceKind.StructuredBuffer => actual == TerminalShaderResourceKind.UavBuffer,
            TerminalShaderResourceKind.UavBuffer => actual == TerminalShaderResourceKind.StructuredBuffer,
            _ => false,
        };
    }

    private static void ValidateResourceSize(
        TerminalShaderResourceValue value,
        TerminalShaderBackendCapabilities? capabilities,
        List<TerminalShaderDiagnostic> diagnostics)
    {
        if (capabilities is null)
        {
            return;
        }

        if (value.Width > capabilities.MaxTextureSize || value.Height > capabilities.MaxTextureSize)
        {
            diagnostics.Add(new TerminalShaderDiagnostic(
                TerminalShaderDiagnosticSeverity.Error,
                "RTSHADERRUNTIME033",
                $"Runtime resource '{value.Name}' is {value.Width}x{value.Height}, which exceeds backend '{capabilities.BackendKind}' max texture size {capabilities.MaxTextureSize}."));
        }
    }

    private static void ValidateOutputSizes(
        TerminalShaderPackage package,
        TerminalShaderFrameRequest frame,
        TerminalShaderBackendCapabilities capabilities,
        List<TerminalShaderDiagnostic> diagnostics)
    {
        for (int passIndex = 0; passIndex < package.Passes.Count; passIndex++)
        {
            TerminalShaderPass pass = package.Passes[passIndex];
            for (int outputIndex = 0; outputIndex < pass.Outputs.Count; outputIndex++)
            {
                TerminalShaderPassOutput output = pass.Outputs[outputIndex];
                int width = Math.Max(1, (int)MathF.Ceiling(frame.Width * output.WidthScale));
                int height = Math.Max(1, (int)MathF.Ceiling(frame.Height * output.HeightScale));
                if (width > capabilities.MaxTextureSize || height > capabilities.MaxTextureSize)
                {
                    diagnostics.Add(new TerminalShaderDiagnostic(
                        TerminalShaderDiagnosticSeverity.Error,
                        "RTSHADERRUNTIME034",
                        $"Pass '{pass.Name}' output '{output.Name}' would allocate {width}x{height}, which exceeds backend '{capabilities.BackendKind}' max texture size {capabilities.MaxTextureSize}."));
                }
            }
        }
    }
}
