# Protected workspace archives

Raw RTMUX archives contain terminal screens and history in plaintext. Their internal SHA-256 detects corruption but does not protect secrets or authenticate a sender. The optional protected form is **a standard binary age file containing the same RTMUX bytes**. No new cryptographic primitive, key server, or broker configuration is introduced.

```sh
# On trusted machines, distribute only the public recipient (starts with age1).
age-keygen -o operator.key
recipient=$(age-keygen -y operator.key)
royalterminal-mux archive save Work Work.rtw.age --recipient "$recipient"
royalterminal-mux archive list Work.rtw.age --identity operator.key
royalterminal-mux archive view Work.rtw.age "$block_id" --identity operator.key
royalterminal-mux restore-layout Work.rtw.age --plan --identity operator.key
royalterminal-mux restore-layout Work.rtw.age Recovered --fresh-shells --identity operator.key
```

The identity argument names a trusted, local identity **file**, never a key string. One to sixteen distinct X25519 recipients are supported; any selected recipient can open the file independently. The writer pipes plaintext directly into the local age executable. Its private, same-directory temporary file contains only ciphertext. Conversion publishes by rename after both capture and encryption finish and the broker's layout revision is rechecked. Failed captures, interrupted writes and helper failures do not replace an existing archive. `--replace` is explicit. Using an `.age` destination without recipients fails before capture; other destinations retain the existing plaintext default when no recipients are provided.

Opening an encrypted workspace requires explicit `--identity`, even when the filename has no `.age` suffix. A plaintext file does not become an accepted encrypted file by changing its name or supplying an identity. A native identity is read into a bounded buffer, checked for native (non-plugin) keys, sent over a private pipe, then cleared on return. The helper receives neither the broker token nor the caller's environment. It must be installed in a conventional system location or configured with a trusted absolute `ROYALTERMINAL_MUX_AGE` path. It is never downloaded or started by the broker; no shell is used. No passphrase, plugin, smartcard or remote identity service is enabled by this profile.

The reader waits for the **entire** inner archive (including its size limits, checksum and EOF) and a successful exit from the decryption process before displaying a screen, contacting the destination broker, or creating a session. A valid plaintext prefix is insufficient. `archive view` and `restore-layout --plan` work with the old broker stopped. `--fresh-shells` intentionally starts new processes with new identities, not archived commands or memory.

Things this does not protect: live server memory, filenames, approximate archive sizes, the terminal history on an authorized viewer's screen, OS swap or crash dumps. Public recipients provide confidentiality and authenticated ciphertext, **not publisher identity**: anyone who knows a recipient's public key can encrypt a different archive to them. Verify provenance separately. Keep the identity file private and backed up; there is no escrow. The existing WebSocket/HTTP transport and plain archives are unchanged.

References: [age format](https://age-encryption.org/v1), [official command manual](https://github.com/FiloSottile/age/blob/main/doc/age.1.ronn). [Executable acceptance test](../../scripts/workspace/smoke-protected-archive.sh) exercises two real recipients, stock age interoperability in both directions, wrong keys, truncated/tampered files, failed atomic replacement and a decryption helper returning failure after emitting a complete valid inner archive.
