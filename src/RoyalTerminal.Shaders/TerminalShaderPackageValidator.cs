// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Shaders - Full terminal shader package validation.

using System.Globalization;
using System.Text;

namespace RoyalTerminal.Shaders;

/// <summary>
/// Validates full compiler-backed terminal shader packages before compilation.
/// </summary>
public static class TerminalShaderPackageValidator
{
    /// <summary>
    /// Validates a shader package.
    /// </summary>
    /// <param name="package">Package to validate.</param>
    /// <returns>The validation result.</returns>
    public static TerminalShaderPackageValidationResult Validate(TerminalShaderPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);

        List<TerminalShaderDiagnostic> diagnostics = [];
        ValidateFiles(package, diagnostics);
        ValidateResources(package, diagnostics);
        ValidatePasses(package, diagnostics);
        return diagnostics.Count == 0
            ? TerminalShaderPackageValidationResult.Success
            : new TerminalShaderPackageValidationResult(diagnostics);
    }

    private static void ValidateFiles(
        TerminalShaderPackage package,
        List<TerminalShaderDiagnostic> diagnostics)
    {
        if (package.Files.Count == 0)
        {
            AddError(diagnostics, "RTSHADER001", "Shader package must contain at least one source file.");
            return;
        }

        if (package.Files.Count > package.Options.MaxFiles)
        {
            AddError(
                diagnostics,
                "RTSHADER002",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Shader package contains {package.Files.Count} files, which exceeds the configured limit of {package.Options.MaxFiles}."));
        }

        int sourceBytes = 0;
        HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < package.Files.Count; i++)
        {
            TerminalShaderFile file = package.Files[i];
            if (!paths.Add(file.VirtualPath))
            {
                AddError(
                    diagnostics,
                    "RTSHADER003",
                    $"Duplicate shader source path '{file.VirtualPath}'.",
                    file.VirtualPath);
            }

            if (TerminalShaderVirtualPath.HasTraversal(file.VirtualPath))
            {
                AddError(
                    diagnostics,
                    "RTSHADER004",
                    $"Shader source path '{file.VirtualPath}' must not contain path traversal segments.",
                    file.VirtualPath);
            }

            if (file.Source.Length == 0)
            {
                AddWarning(
                    diagnostics,
                    "RTSHADER005",
                    $"Shader source file '{file.VirtualPath}' is empty.",
                    file.VirtualPath);
            }

            sourceBytes += Encoding.UTF8.GetByteCount(file.Source);
        }

        if (sourceBytes > package.Options.MaxSourceBytes)
        {
            AddError(
                diagnostics,
                "RTSHADER006",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Shader package source size is {sourceBytes} bytes, which exceeds the configured limit of {package.Options.MaxSourceBytes} bytes."));
        }
    }

    private static void ValidateResources(
        TerminalShaderPackage package,
        List<TerminalShaderDiagnostic> diagnostics)
    {
        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, TerminalShaderResourceBinding> registers = new(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < package.Resources.Count; i++)
        {
            TerminalShaderResourceBinding resource = package.Resources[i];
            if (!names.Add(resource.Name))
            {
                AddError(
                    diagnostics,
                    "RTSHADER020",
                    $"Duplicate shader resource name '{resource.Name}'.");
            }

            if (resource.RegisterIndex < 0)
            {
                continue;
            }

            string? registerClass = GetRegisterClass(resource.Kind);
            if (registerClass is null)
            {
                continue;
            }

            string key = $"{registerClass}{resource.RegisterIndex}:space{resource.RegisterSpace}";
            if (registers.TryGetValue(key, out TerminalShaderResourceBinding? existing))
            {
                AddError(
                    diagnostics,
                    "RTSHADER021",
                    $"Resources '{existing.Name}' and '{resource.Name}' both bind to register {key}.");
            }
            else
            {
                registers.Add(key, resource);
            }
        }
    }

    private static void ValidatePasses(
        TerminalShaderPackage package,
        List<TerminalShaderDiagnostic> diagnostics)
    {
        if (package.Passes.Count == 0)
        {
            AddError(diagnostics, "RTSHADER040", "Shader package must contain at least one pass.");
            return;
        }

        if (package.Passes.Count > package.Options.MaxPasses)
        {
            AddError(
                diagnostics,
                "RTSHADER041",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Shader package contains {package.Passes.Count} passes, which exceeds the configured limit of {package.Options.MaxPasses}."));
        }

        HashSet<string> sourcePaths = package.Files
            .Select(static file => file.VirtualPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        HashSet<string> passNames = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> availableResources = package.Resources
            .Select(static resource => resource.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < package.Passes.Count; i++)
        {
            TerminalShaderPass pass = package.Passes[i];
            if (!passNames.Add(pass.Name))
            {
                AddError(diagnostics, "RTSHADER042", $"Duplicate shader pass name '{pass.Name}'.");
            }

            ValidatePassSource(pass, sourcePaths, diagnostics);
            ValidatePassStage(pass, diagnostics);
            ValidatePassInputs(pass, availableResources, diagnostics);
            ValidatePassOutputs(pass, availableResources, diagnostics);
        }
    }

    private static void ValidatePassSource(
        TerminalShaderPass pass,
        HashSet<string> sourcePaths,
        List<TerminalShaderDiagnostic> diagnostics)
    {
        if (pass.Stage is TerminalShaderStage.Pixel or TerminalShaderStage.Compute)
        {
            if (pass.SourcePath is null)
            {
                AddError(
                    diagnostics,
                    "RTSHADER050",
                    $"Shader pass '{pass.Name}' must specify a source file.");
            }
            else if (!sourcePaths.Contains(pass.SourcePath))
            {
                AddError(
                    diagnostics,
                    "RTSHADER051",
                    $"Shader pass '{pass.Name}' references missing source file '{pass.SourcePath}'.",
                    pass.SourcePath);
            }

            if (string.IsNullOrWhiteSpace(pass.EntryPoint))
            {
                AddError(
                    diagnostics,
                    "RTSHADER052",
                    $"Shader pass '{pass.Name}' must specify an entry point.");
            }
        }
        else if (pass.SourcePath is not null || pass.EntryPoint is not null)
        {
            AddWarning(
                diagnostics,
                "RTSHADER053",
                $"Shader pass '{pass.Name}' is a {pass.Stage} pass and will ignore source and entry-point settings.");
        }
    }

    private static void ValidatePassStage(
        TerminalShaderPass pass,
        List<TerminalShaderDiagnostic> diagnostics)
    {
        if (pass.Stage == TerminalShaderStage.Compute && pass.Dispatch is null)
        {
            AddError(
                diagnostics,
                "RTSHADER060",
                $"Compute shader pass '{pass.Name}' must specify dispatch dimensions.");
        }

        if (pass.Stage != TerminalShaderStage.Compute && pass.Dispatch is not null)
        {
            AddError(
                diagnostics,
                "RTSHADER061",
                $"Shader pass '{pass.Name}' specifies dispatch dimensions but is not a compute pass.");
        }

        if (pass.TargetProfile is null)
        {
            return;
        }

        string profile = pass.TargetProfile.Name;
        if (pass.Stage == TerminalShaderStage.Pixel &&
            !profile.StartsWith("ps_", StringComparison.OrdinalIgnoreCase))
        {
            AddError(
                diagnostics,
                "RTSHADER062",
                $"Pixel shader pass '{pass.Name}' uses non-pixel target profile '{profile}'.");
        }

        if (pass.Stage == TerminalShaderStage.Compute &&
            !profile.StartsWith("cs_", StringComparison.OrdinalIgnoreCase))
        {
            AddError(
                diagnostics,
                "RTSHADER063",
                $"Compute shader pass '{pass.Name}' uses non-compute target profile '{profile}'.");
        }
    }

    private static void ValidatePassInputs(
        TerminalShaderPass pass,
        HashSet<string> availableResources,
        List<TerminalShaderDiagnostic> diagnostics)
    {
        HashSet<string> passInputNames = new(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < pass.Inputs.Count; i++)
        {
            TerminalShaderPassInput input = pass.Inputs[i];
            if (!passInputNames.Add(input.BindingName))
            {
                AddError(
                    diagnostics,
                    "RTSHADER070",
                    $"Shader pass '{pass.Name}' declares duplicate input binding '{input.BindingName}'.");
            }

            if (!availableResources.Contains(input.ResourceName))
            {
                AddError(
                    diagnostics,
                    "RTSHADER071",
                    $"Shader pass '{pass.Name}' references unavailable input resource '{input.ResourceName}'.");
            }
        }
    }

    private static void ValidatePassOutputs(
        TerminalShaderPass pass,
        HashSet<string> availableResources,
        List<TerminalShaderDiagnostic> diagnostics)
    {
        HashSet<string> passOutputNames = new(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < pass.Outputs.Count; i++)
        {
            TerminalShaderPassOutput output = pass.Outputs[i];
            if (!passOutputNames.Add(output.Name))
            {
                AddError(
                    diagnostics,
                    "RTSHADER080",
                    $"Shader pass '{pass.Name}' declares duplicate output '{output.Name}'.");
            }

            if (!availableResources.Add(output.Name))
            {
                AddError(
                    diagnostics,
                    "RTSHADER081",
                    $"Shader pass '{pass.Name}' output '{output.Name}' conflicts with an existing resource.");
            }
        }
    }

    private static string? GetRegisterClass(TerminalShaderResourceKind kind)
    {
        return kind switch
        {
            TerminalShaderResourceKind.ConstantBuffer => "b",
            TerminalShaderResourceKind.Sampler => "s",
            TerminalShaderResourceKind.Texture2D or
                TerminalShaderResourceKind.TerminalFramebuffer or
                TerminalShaderResourceKind.StructuredBuffer or
                TerminalShaderResourceKind.ByteAddressBuffer or
                TerminalShaderResourceKind.HistoryTexture => "t",
            TerminalShaderResourceKind.UavTexture2D or TerminalShaderResourceKind.UavBuffer => "u",
            _ => null,
        };
    }

    private static void AddError(
        List<TerminalShaderDiagnostic> diagnostics,
        string code,
        string message,
        string? filePath = null)
    {
        diagnostics.Add(new TerminalShaderDiagnostic(TerminalShaderDiagnosticSeverity.Error, code, message, filePath));
    }

    private static void AddWarning(
        List<TerminalShaderDiagnostic> diagnostics,
        string code,
        string message,
        string? filePath = null)
    {
        diagnostics.Add(new TerminalShaderDiagnostic(TerminalShaderDiagnosticSeverity.Warning, code, message, filePath));
    }
}
