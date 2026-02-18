# Transport SSH Credentials And Trust

## Table Of Contents

- [Credential Resolution Pipeline](#credential-resolution-pipeline)
- [Credential Provider Implementations](#credential-provider-implementations)
- [Secret Store Implementations](#secret-store-implementations)
- [Secret Protection Implementations](#secret-protection-implementations)
- [Host-Key Trust Implementations](#host-key-trust-implementations)
- [Fingerprint Pinning Behavior](#fingerprint-pinning-behavior)
- [Recommended Setup Profiles](#recommended-setup-profiles)
- [Troubleshooting](#troubleshooting)

## Credential Resolution Pipeline

Runtime flow in `SshNetTerminalTransport.StartAsync`:

1. Validate SSH endpoint and auth options.
2. Build `SshCredentialRequest` from `SshTransportOptions`.
3. Resolve `SshResolvedCredentials` via `ISshCredentialProvider`.
4. Build authentication methods:
   - password method when requested
   - private-key method from resolved key material/paths
   - optional contributor methods (SSH agent)
5. Connect SSH client.
6. Validate host key (pin first if provided, otherwise validator).

Primary file anchors:
- `src/RoyalTerminal.Terminal.Transport.Ssh.SshNet/SshNetTerminalTransport.cs`
- `src/RoyalTerminal.Terminal/Terminal/SshAuthContracts.cs`

## Credential Provider Implementations

`ISshCredentialProvider` implementations:

- `NullSshCredentialProvider`
  - file: `src/RoyalTerminal.Terminal.Transport.Ssh.SshNet/NullSshCredentialProvider.cs`
  - behavior: always throws, used as explicit "not configured" default

- `SecretStoreSshCredentialProvider`
  - file: `src/RoyalTerminal.Terminal/Terminal/SshCredentialProviders.cs`
  - behavior: resolves password/private key secrets by ID from `ISshSecretStore`
  - validation: throws when required secret IDs are empty or missing

- `DemoSshCredentialProvider` (sample-local)
  - file: `samples/RoyalTerminal.Demo/Services/MainWindowController.cs`
  - behavior: maps view-model fields to well-known secret IDs for demo flow

## Secret Store Implementations

`ISshSecretStore` implementations in `SshCredentialProviders.cs`:

- `InMemorySshSecretStore`
  - test/ephemeral use

- `EnvironmentVariableSshSecretStore`
  - process-level env var persistence via optional prefix

- `JsonFileSshSecretStore`
  - plaintext JSON store
  - atomic writes through `SshSecretFileIo`

- `ProtectedJsonFileSshSecretStore`
  - encrypted payload JSON store with protector metadata

- `CompositeSshSecretStore`
  - load from first store returning value
  - save to first store only

Operational details:
- file-based stores use atomic temp-file replacement
- Unix file mode hardening is applied best-effort

## Secret Protection Implementations

`ISshSecretProtector` implementations:

- `NoOpSshSecretProtector`
  - for tests/explicit plaintext compatibility

- `DpapiSshSecretProtector`
  - Windows-only, `CurrentUser` or `LocalMachine` scope

- `AesGcmSshSecretProtector`
  - cross-platform encrypted payloads with persisted per-user key file

Factory helpers:
- `SshSecretProtectionFactory.CreateDefaultProtector(...)`
- `SshSecretProtectionFactory.CreateDefaultSecretStore(...)`
- file: `src/RoyalTerminal.Terminal/Terminal/SshSecretProtectionDefaults.cs`

Default selection:
- Windows: DPAPI
- non-Windows: AES-GCM keyfile in local app storage

## Host-Key Trust Implementations

`ISshHostKeyValidator` implementations:

- `KnownHostsSshHostKeyValidator`
  - parses OpenSSH known_hosts entries
  - supports hashed host entries (`|1|...`) and `@revoked`
  - default known_hosts paths include user and system locations

- `ExpectedFingerprintSshHostKeyValidator`
  - trusts only one normalized SHA-256 fingerprint

- `RejectAllSshHostKeyValidator`
  - hard deny validator

Files:
- `src/RoyalTerminal.Terminal.Transport.Ssh.Abstractions/SshHostKeyValidation.cs`
- `src/RoyalTerminal.Terminal.Transport.Ssh.Abstractions/KnownHostsSshHostKeyValidator.cs`

## Fingerprint Pinning Behavior

Option:
- `SshTransportOptions.ExpectedHostKeyFingerprintSha256`

Transport behavior:
- if provided, it overrides validator decision path
- normalized comparison removes optional `SHA256:` prefix and trailing `=` padding
- mismatch yields explicit host-key mismatch message and fails startup

Use pinning when deterministic trust is required (CI, production managed endpoints).

## Recommended Setup Profiles

Local/dev profile:
- credential provider: demo provider or protected local secret store
- trust: known_hosts validator
- optional: no fingerprint pinning for rotating development keys

Production profile:
- credential provider: protected secret store + rotation workflow
- trust: explicit fingerprint pinning per endpoint or strict known_hosts policy
- do not use `NullSshCredentialProvider` or `NoOpSshSecretProtector`

CI integration profile:
- credential provider: environment-backed store
- trust: explicit fingerprint pinning via options
- configure required env vars before test execution

## Troubleshooting

| Symptom | Likely Cause | What to inspect |
|---|---|---|
| `No SSH credential provider was configured` | default null provider still in use | `TerminalControl` wiring / DI setup |
| `No SSH authentication methods are available` | auth mode requested but no credentials resolved | `BuildSshAuthenticationOptions`, provider output |
| host key mismatch message | wrong pinned fingerprint | `ExpectedHostKeyFingerprintSha256` value normalization |
| host key not trusted | missing known_hosts entry or unsupported marker | `KnownHostsSshHostKeyValidator` input files |
| private key auth fails | key content/path invalid for SSH.NET | resolved key list in credential provider |

## Code Examples

### Secure default credential provider setup

```csharp
using RoyalTerminal.Terminal;

ISshSecretStore secretStore = SshSecretProtectionFactory.CreateDefaultSecretStore();
ISshCredentialProvider credentialProvider = new SecretStoreSshCredentialProvider(secretStore);

await secretStore.SaveSecretAsync("ssh-password", "super-secret");
await secretStore.SaveSecretAsync("ssh-key", File.ReadAllText("~/.ssh/id_ed25519"));
```

### Build SSH options with pinning + mixed auth

```csharp
SshAuthenticationOptions auth = new(
    UsePassword: true,
    PasswordSecretId: "ssh-password",
    PrivateKeySecretIds: new[] { "ssh-key" },
    UseAgent: false);

SshTransportOptions options = new(
    Endpoint: new SshEndpointOptions("prod.example.com", 22, "deploy"),
    RequestPty: true,
    TerminalType: "xterm-256color",
    InitialCommand: null,
    Authentication: auth,
    Dimensions: new TerminalSessionDimensions(140, 45, 1400, 900))
{
    ExpectedHostKeyFingerprintSha256 = "SHA256:abc..."
};
```

### Known-hosts validator with explicit file list

```csharp
ISshHostKeyValidator validator = new KnownHostsSshHostKeyValidator(
    knownHostsFiles: new[]
    {
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh", "known_hosts"),
        "/etc/ssh/ssh_known_hosts"
    });
```

### Enable SSH agent contributor

```csharp
var provider = new SshNetTerminalTransportProvider(
    credentialProvider,
    validator,
    authContributors: new ISshNetAuthenticationMethodContributor[]
    {
        new SshNetAgentAuthenticationMethodContributor()
    });
```
