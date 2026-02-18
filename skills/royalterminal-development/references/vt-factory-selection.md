# VT Factory Selection

## Table Of Contents

- [Factory Implementation](#factory-implementation)
- [Selection Algorithm](#selection-algorithm)
- [Preference Outcomes](#preference-outcomes)
- [Provider Ordering Rules](#provider-ordering-rules)
- [Failure Semantics](#failure-semantics)
- [Extension Checklist](#extension-checklist)

## Factory Implementation

Type:
- `DefaultVtProcessorFactory`
- `src/RoyalTerminal.Terminal.Vt.Default/Terminal/DefaultVtProcessorFactory.cs`

Inputs:
- `TerminalScreen screen`
- `VtProcessorPreference preference`
- configured `INativeVtProcessorProvider[]`

## Selection Algorithm

Algorithm used by `Create(screen, preference)`:

1. If preference is `Managed`, return `new BasicVtProcessor(screen)`.
2. For `Auto` or `Native`, iterate configured native providers in order:
   - skip provider when `IsAvailable=false`
   - attempt `provider.Create(screen)` when available
3. If provider creation throws:
   - `Auto`: swallow and continue searching/fallback
   - `Native`: do not swallow final failure path; native requirement must fail
4. If no native instance was created:
   - `Native`: throw `InvalidOperationException`
   - `Auto`: return `new BasicVtProcessor(screen)`

## Preference Outcomes

| Preference | Native available | Native create succeeds | Outcome |
|---|---|---|---|
| `Managed` | any | any | `BasicVtProcessor` |
| `Auto` | no | n/a | `BasicVtProcessor` |
| `Auto` | yes | yes | native processor |
| `Auto` | yes | no | `BasicVtProcessor` |
| `Native` | no | n/a | throw |
| `Native` | yes | yes | native processor |
| `Native` | yes | no | throw |

## Provider Ordering Rules

- first available provider that successfully creates wins.
- provider registration order is policy; keep it deterministic.
- if multiple native providers are introduced later, put most preferred provider first.

## Failure Semantics

Guaranteed behavior:
- `Auto` should preserve app startup resilience.
- `Native` should provide strict configuration behavior and explicit failure.
- failure message for strict native mode should remain actionable:
  - "Native VT processor was requested but no native VT provider is available."

## Extension Checklist

When changing factory logic:

1. Preserve strict `Native` vs resilient `Auto` semantics.
2. Preserve deterministic provider ordering.
3. Keep provider exception swallowing only in `Auto` path.
4. Add/update tests for each branch in the preference matrix.
5. Re-verify demo mode fallback behavior (`TerminalModeResolver` + controller mapping).

## Code Examples

### Preference-driven factory selection

```csharp
IVtProcessorFactory factory = new DefaultVtProcessorFactory(
    new INativeVtProcessorProvider[] { new GhosttyVtProcessorProvider() });

IVtProcessor autoProcessor = factory.Create(screen, VtProcessorPreference.Auto);
IVtProcessor managedProcessor = factory.Create(screen, VtProcessorPreference.Managed);
IVtProcessor nativeProcessor = factory.Create(screen, VtProcessorPreference.Native);
```

### Resilient `Auto` fallback pattern

```csharp
try
{
    IVtProcessor processor = factory.Create(screen, VtProcessorPreference.Auto);
}
catch
{
    // Unexpected only if managed creation path fails.
}
```

### Strict `Native` failure test pattern

```csharp
IVtProcessorFactory noNativeFactory = new DefaultVtProcessorFactory(Array.Empty<INativeVtProcessorProvider>());
Assert.Throws<InvalidOperationException>(() => noNativeFactory.Create(screen, VtProcessorPreference.Native));
```
