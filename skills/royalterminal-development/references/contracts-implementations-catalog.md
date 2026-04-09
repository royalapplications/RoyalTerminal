# Contracts And Implementations Catalog

This catalog maps every interface contract in `src/` + `samples/` to all in-repo implementations.

## Table Of Contents

- [Contract Scope](#contract-scope)
- [Interface Matrix](#interface-matrix)
- [Subsystem View](#subsystem-view)
- [How To Extend](#how-to-extend)
- [Notes](#notes)
- [Verification](#verification)

## Contract Scope

In scope:
- every `I*` interface declared in `src/` or `samples/`
- all implementation types in `src/` or `samples/`

Out of scope:
- external implementations in consuming applications
- test-only mock or fake implementations outside `src/` and `samples/`

## Interface Matrix

| Contract | Declaration | Implementations |
|---|---|---|
| `IAvaloniaD3D11TextureHandleProvider` | `src/RoyalTerminal.Avalonia.Rendering.GhosttyInterop/Interop/IAvaloniaD3D11TextureHandleProvider.cs` | `DefaultAvaloniaD3D11TextureHandleProvider` (`src/RoyalTerminal.Avalonia.Rendering.GhosttyInterop/Interop/DefaultAvaloniaD3D11TextureHandleProvider.cs`)<br>`NullAvaloniaD3D11TextureHandleProvider` (`src/RoyalTerminal.Avalonia.Rendering.GhosttyInterop/Interop/NullAvaloniaD3D11TextureHandleProvider.cs`) |
| `IAvaloniaD3D12TextureHandleProvider` | `src/RoyalTerminal.Avalonia.Rendering.GhosttyInterop/Interop/IAvaloniaD3D12TextureHandleProvider.cs` | `DefaultAvaloniaD3D12TextureHandleProvider` (`src/RoyalTerminal.Avalonia.Rendering.GhosttyInterop/Interop/DefaultAvaloniaD3D12TextureHandleProvider.cs`)<br>`NullAvaloniaD3D12TextureHandleProvider` (`src/RoyalTerminal.Avalonia.Rendering.GhosttyInterop/Interop/NullAvaloniaD3D12TextureHandleProvider.cs`) |
| `IAvaloniaMetalTextureHandleProvider` | `src/RoyalTerminal.Avalonia.Rendering.GhosttyInterop/Interop/IAvaloniaMetalTextureHandleProvider.cs` | `DefaultAvaloniaMetalTextureHandleProvider` (`src/RoyalTerminal.Avalonia.Rendering.GhosttyInterop/Interop/DefaultAvaloniaMetalTextureHandleProvider.cs`)<br>`NullAvaloniaMetalTextureHandleProvider` (`src/RoyalTerminal.Avalonia.Rendering.GhosttyInterop/Interop/NullAvaloniaMetalTextureHandleProvider.cs`) |
| `IAvaloniaOpenGlRenderTargetHandleProvider` | `src/RoyalTerminal.Avalonia.Rendering.GhosttyInterop/Interop/IAvaloniaOpenGlRenderTargetHandleProvider.cs` | `DefaultAvaloniaOpenGlRenderTargetHandleProvider` (`src/RoyalTerminal.Avalonia.Rendering.GhosttyInterop/Interop/DefaultAvaloniaOpenGlRenderTargetHandleProvider.cs`)<br>`NullAvaloniaOpenGlRenderTargetHandleProvider` (`src/RoyalTerminal.Avalonia.Rendering.GhosttyInterop/Interop/NullAvaloniaOpenGlRenderTargetHandleProvider.cs`) |
| `IAvaloniaSkiaRenderTargetProvider` | `src/RoyalTerminal.Avalonia.Rendering.GhosttyInterop/Interop/IAvaloniaSkiaRenderTargetProvider.cs` | `AvaloniaSkiaRenderTargetProvider` (`src/RoyalTerminal.Avalonia.Rendering.GhosttyInterop/Interop/AvaloniaSkiaRenderTargetProvider.cs`) |
| `IAvaloniaVulkanTextureHandleProvider` | `src/RoyalTerminal.Avalonia.Rendering.GhosttyInterop/Interop/IAvaloniaVulkanTextureHandleProvider.cs` | `DefaultAvaloniaVulkanTextureHandleProvider` (`src/RoyalTerminal.Avalonia.Rendering.GhosttyInterop/Interop/DefaultAvaloniaVulkanTextureHandleProvider.cs`)<br>`NullAvaloniaVulkanTextureHandleProvider` (`src/RoyalTerminal.Avalonia.Rendering.GhosttyInterop/Interop/NullAvaloniaVulkanTextureHandleProvider.cs`) |
| `INativeVtProcessorProvider` | `src/RoyalTerminal.Terminal/Terminal/INativeVtProcessorProvider.cs` | `GhosttyVtProcessorProvider` (`src/RoyalTerminal.Terminal.Vt.Ghostty/Terminal/GhosttyVtProcessorProvider.cs`) |
| `IPty` | `src/RoyalTerminal.Terminal/Terminal/IPty.cs` | `UnixPty` (`src/RoyalTerminal.Terminal.Pty.Unix/Terminal/UnixPty.cs`)<br>`WindowsPty` (`src/RoyalTerminal.Terminal.Pty.Windows/Terminal/WindowsPty.cs`) |
| `IPtyFactory` | `src/RoyalTerminal.Terminal/Terminal/IPtyFactory.cs` | `DefaultPtyFactory` (`src/RoyalTerminal.Terminal.Pty.Platform/Terminal/DefaultPtyFactory.cs`) |
| `IRenderBackend` | `src/RoyalTerminal.Rendering.Contracts/Contracts/IRenderBackend.cs` | none in `src/` or `samples/` |
| `IRenderSurface` | `src/RoyalTerminal.Rendering.Contracts/Contracts/IRenderSurface.cs` | `GhosttyRenderSurface` (`src/RoyalTerminal.Rendering.Interop.Ghostty/Interop/GhosttyRenderSurface.cs`) |
| `IShellProfileCatalog` | `src/RoyalTerminal.Terminal/Terminal/ShellProfiles.cs` | `DefaultShellProfileCatalog` (`src/RoyalTerminal.Terminal/Terminal/ShellProfiles.cs`) |
| `ISkiaRgbaFallbackRenderer` | `src/RoyalTerminal.Rendering.Interop.Ghostty.Skia/Interop/ISkiaRgbaFallbackRenderer.cs` | `GhosttyRenderSurfaceRgbaFallbackRenderer` (`src/RoyalTerminal.Rendering.Interop.Ghostty.Skia/Interop/GhosttyRenderSurfaceRgbaFallbackRenderer.cs`) |
| `ISshCredentialProvider` | `src/RoyalTerminal.Terminal/Terminal/SshAuthContracts.cs` | `DemoSshCredentialProvider` (`samples/RoyalTerminal.Demo/Services/MainWindowController.cs`)<br>`NullSshCredentialProvider` (`src/RoyalTerminal.Terminal.Transport.Ssh.SshNet/NullSshCredentialProvider.cs`)<br>`SecretStoreSshCredentialProvider` (`src/RoyalTerminal.Terminal/Terminal/SshCredentialProviders.cs`) |
| `ISshHostKeyValidator` | `src/RoyalTerminal.Terminal.Transport.Ssh.Abstractions/SshHostKeyValidation.cs` | `KnownHostsSshHostKeyValidator` (`src/RoyalTerminal.Terminal.Transport.Ssh.Abstractions/KnownHostsSshHostKeyValidator.cs`)<br>`ExpectedFingerprintSshHostKeyValidator` (`src/RoyalTerminal.Terminal.Transport.Ssh.Abstractions/SshHostKeyValidation.cs`)<br>`RejectAllSshHostKeyValidator` (`src/RoyalTerminal.Terminal.Transport.Ssh.Abstractions/SshHostKeyValidation.cs`) |
| `ISshNetAuthenticationMethodContributor` | `src/RoyalTerminal.Terminal.Transport.Ssh.SshNet/ISshNetAuthenticationMethodContributor.cs` | `SshNetAgentAuthenticationMethodContributor` (`src/RoyalTerminal.Terminal.Transport.Ssh.SshNet.Agent/SshNetAgentAuthenticationMethodContributor.cs`) |
| `ISshSecretProtector` | `src/RoyalTerminal.Terminal/Terminal/SshSecretProtectionContracts.cs` | `DpapiSshSecretProtector` (`src/RoyalTerminal.Terminal/Terminal/SshCredentialProviders.cs`)<br>`NoOpSshSecretProtector` (`src/RoyalTerminal.Terminal/Terminal/SshCredentialProviders.cs`)<br>`AesGcmSshSecretProtector` (`src/RoyalTerminal.Terminal/Terminal/SshSecretProtectionDefaults.cs`) |
| `ISshSecretStore` | `src/RoyalTerminal.Terminal/Terminal/SshAuthContracts.cs` | `CompositeSshSecretStore` (`src/RoyalTerminal.Terminal/Terminal/SshCredentialProviders.cs`)<br>`EnvironmentVariableSshSecretStore` (`src/RoyalTerminal.Terminal/Terminal/SshCredentialProviders.cs`)<br>`InMemorySshSecretStore` (`src/RoyalTerminal.Terminal/Terminal/SshCredentialProviders.cs`)<br>`JsonFileSshSecretStore` (`src/RoyalTerminal.Terminal/Terminal/SshCredentialProviders.cs`)<br>`ProtectedJsonFileSshSecretStore` (`src/RoyalTerminal.Terminal/Terminal/SshCredentialProviders.cs`) |
| `ITerminalEndpoint` | `src/RoyalTerminal.Terminal/Terminal/TerminalEndpointContracts.cs` | custom endpoint implementations in app/integration layers |
| `ITerminalInputAdapter` | `src/RoyalTerminal.Avalonia/Services/ITerminalInputAdapter.cs` | `DefaultTerminalInputAdapter` (`src/RoyalTerminal.Avalonia/Services/DefaultTerminalInputAdapter.cs`) |
| `ITerminalInputSink` | `src/RoyalTerminal.Terminal/Terminal/TerminalEndpointContracts.cs` | custom endpoint implementations in app/integration layers |
| `ITerminalModeCapabilityResolver` | `samples/RoyalTerminal.Demo/Services/TerminalModeResolver.cs` | `TerminalModeCapabilityResolver` (`samples/RoyalTerminal.Demo/Services/TerminalModeResolver.cs`) |
| `ITerminalModeResolver` | `samples/RoyalTerminal.Demo/Services/TerminalModeResolver.cs` | `TerminalModeResolver` (`samples/RoyalTerminal.Demo/Services/TerminalModeResolver.cs`) |
| `ITerminalModeSource` | `src/RoyalTerminal.Terminal/Terminal/TerminalEndpointContracts.cs` | `VtProcessorModeSource` (`src/RoyalTerminal.Terminal.Services/Services/TerminalSessionService.cs`) |
| `ITerminalPtyTransport` | `src/RoyalTerminal.Terminal/Terminal/TransportContracts.cs` | `PtyTerminalTransport` (`src/RoyalTerminal.Terminal.Transport.Pty/PtyTerminalTransport.cs`) |
| `ITerminalScaleSink` | `src/RoyalTerminal.Terminal/Terminal/TerminalEndpointContracts.cs` | custom endpoint implementations in app/integration layers |
| `ITerminalScrollService` | `src/RoyalTerminal.Avalonia/Services/ITerminalScrollService.cs` | `DefaultTerminalScrollService` (`src/RoyalTerminal.Avalonia/Services/DefaultTerminalScrollService.cs`) |
| `ITerminalSelectionService` | `src/RoyalTerminal.Avalonia/Services/ITerminalSelectionService.cs` | `DefaultTerminalSelectionService` (`src/RoyalTerminal.Avalonia/Services/DefaultTerminalSelectionService.cs`) |
| `ITerminalSelectionSource` | `src/RoyalTerminal.Terminal/Terminal/TerminalEndpointContracts.cs` | `VtProcessorSelectionSource` (`src/RoyalTerminal.Terminal.Services/Services/VtProcessorSelectionSource.cs`) when attached through session services |
| `ITerminalSessionService` | `src/RoyalTerminal.Terminal.Services.Contracts/Contracts/ITerminalSessionService.cs` | `TerminalSessionService` (`src/RoyalTerminal.Terminal.Services/Services/TerminalSessionService.cs`) |
| `ITerminalTransport` | `src/RoyalTerminal.Terminal/Terminal/TransportContracts.cs` | `PtyTerminalTransport` via `ITerminalPtyTransport` (`src/RoyalTerminal.Terminal.Transport.Pty/PtyTerminalTransport.cs`)<br>`PipeTerminalTransport` (`src/RoyalTerminal.Terminal.Transport.Pipe/PipeTerminalTransport.cs`)<br>`SshNetTerminalTransport` (`src/RoyalTerminal.Terminal.Transport.Ssh.SshNet/SshNetTerminalTransport.cs`) |
| `ITerminalTransportFactory` | `src/RoyalTerminal.Terminal/Terminal/TransportContracts.cs` | `CompositeTerminalTransportFactory` (`src/RoyalTerminal.Terminal/Terminal/CompositeTerminalTransportFactory.cs`) |
| `ITerminalTransportOptions` | `src/RoyalTerminal.Terminal/Terminal/TransportContracts.cs` | `PtyTransportOptions` (`src/RoyalTerminal.Terminal/Terminal/TransportOptions.cs`)<br>`PipeTransportOptions` (`src/RoyalTerminal.Terminal/Terminal/TransportOptions.cs`)<br>`SshTransportOptions` (`src/RoyalTerminal.Terminal/Terminal/TransportOptions.cs`) |
| `ITerminalTransportProvider` | `src/RoyalTerminal.Terminal/Terminal/TransportContracts.cs` | `PtyTerminalTransportProvider` (`src/RoyalTerminal.Terminal.Transport.Pty/PtyTerminalTransport.cs`)<br>`PipeTerminalTransportProvider` (`src/RoyalTerminal.Terminal.Transport.Pipe/PipeTerminalTransport.cs`)<br>`SshNetTerminalTransportProvider` (`src/RoyalTerminal.Terminal.Transport.Ssh.SshNet/SshNetTerminalTransport.cs`) |
| `ITextShaper` | `src/RoyalTerminal.Rendering.Text/TextShaping/HarfBuzzTextShaper.cs` | `HarfBuzzTextShaper` (`src/RoyalTerminal.Rendering.Text/TextShaping/HarfBuzzTextShaper.cs`) |
| `IVtProcessor` | `src/RoyalTerminal.Terminal/Terminal/IVtProcessor.cs` | `GhosttyVtProcessor` (`src/RoyalTerminal.Terminal.Vt.Ghostty/Terminal/GhosttyVtProcessor.cs`)<br>`BasicVtProcessor` (`src/RoyalTerminal.Terminal.Vt.Managed/Terminal/BasicVtProcessor.cs`) |
| `IVtProcessorFactory` | `src/RoyalTerminal.Terminal/Terminal/IVtProcessorFactory.cs` | `DefaultVtProcessorFactory` (`src/RoyalTerminal.Terminal.Vt.Default/Terminal/DefaultVtProcessorFactory.cs`) |

## Subsystem View

High-level contract grouping:

| Subsystem | Primary Contracts |
|---|---|
| Core transport/session | `ITerminalTransport*`, `ITerminalSessionService`, `IPty*` |
| VT processing | `IVtProcessor`, `IVtProcessorFactory`, `INativeVtProcessorProvider` |
| Endpoint abstraction | `ITerminalEndpoint`, `ITerminalInputSink`, `ITerminalSelectionSource`, `ITerminalModeSource`, `ITerminalScaleSink` |
| SSH auth/trust | `ISshCredentialProvider`, `ISshSecretStore`, `ISshSecretProtector`, `ISshHostKeyValidator`, `ISshNetAuthenticationMethodContributor` |
| Rendering/interop | `IRenderSurface`, `IRenderBackend`, `IAvalonia*HandleProvider`, `ISkiaRgbaFallbackRenderer`, `ITextShaper` |
| Demo mode orchestration | `ITerminalModeResolver`, `ITerminalModeCapabilityResolver` |

## How To Extend

When adding a new contract:

1. Add interface in the correct package layer (`src/` or sample-only when demo-specific).
2. Add at least one concrete implementation in-scope (or document intentional abstraction-only contract).
3. Register implementation in composition roots/factories where required.
4. Add tests for contract behavior and provider/factory selection.
5. Update this catalog row with declaration and implementation paths.

When adding a new implementation for existing contract:

1. Preserve existing contract invariants.
2. Update resolver/factory ordering semantics if selection behavior changes.
3. Add targeted tests for selection and runtime behavior.
4. Add implementation path to the existing row.

## Notes

`IRenderBackend` has no concrete implementation in this repository scope and serves as a backend contract abstraction.
`ITerminalModeSource` implementation is a nested private type (`VtProcessorModeSource`) in `TerminalSessionService`.
`ISshCredentialProvider` includes one demo-local implementation (`DemoSshCredentialProvider`) used by the sample app.

## Verification

Catalog completeness check command used for this file:

```bash
rg --glob 'src/**' --glob 'samples/**' '^\\s*(public|internal)\\s+interface\\s+I' -n
```

Compare the command output against matrix rows when interfaces change.

## Code Examples

### Find all interface declarations and compare with catalog

```bash
cd /Users/wieslawsoltes/GitHub/RoyalTerminal
rg --glob 'src/**' --glob 'samples/**' '^\s*(public|internal)\s+interface\s+I' -n
```

### Find implementations for a specific contract

```bash
# Example: ITerminalTransportProvider implementations
rg --line-number "class .*: .*ITerminalTransportProvider" src samples
```

### Contract + provider template

```csharp
public interface IFooService
{
    ValueTask ExecuteAsync(CancellationToken cancellationToken = default);
}

public sealed class DefaultFooService : IFooService
{
    public ValueTask ExecuteAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }
}
```
