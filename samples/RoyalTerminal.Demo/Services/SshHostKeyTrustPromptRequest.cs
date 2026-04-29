// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Demo - SSH host-key prompt request model.

namespace RoyalTerminal.Demo.Services;

internal sealed record SshHostKeyTrustPromptRequest(
    string Host,
    int Port,
    string Username,
    string HostKeyAlgorithm,
    string FingerprintSha256,
    string FingerprintMd5,
    int KeyLengthBits,
    string? HostKeyBase64,
    bool WillPersistTrust,
    string KnownHostsFilePath);
