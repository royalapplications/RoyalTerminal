# Personal workspace on Linux (experimental)

This installation runs under one ordinary Linux account. Its HTTP management and WebSocket routes are served over a Unix domain socket; no TCP listener is needed. The CLI uses HTTP framing over `AF_UNIX` with an explicit socket connection callback. The synthetic `http://localhost` authority is not resolved or dialled. Requests retain the normal server bearer authorization, regardless of filesystem permissions. Ordinary browsers cannot open the socket without a separately configured bridge.

From a source checkout with the .NET 10 SDK, Node/npm and the system `openssl` command, build the framework-dependent payload. The target machine needs the .NET 10 **ASP.NET Core runtime** and a systemd user manager. This bundle includes browser assets and their licenses, the host, CLI and Lua adapter; Lua 5.1 and `dkjson` are optional external prerequisites for the `lua` command.

```sh
bash scripts/workspace/publish-personal.sh /tmp/royalterminal-personal
/tmp/royalterminal-personal/install-personal.sh install --start
~/.local/bin/royalterminal-mux new Work
~/.local/bin/royalterminal-mux run Work --label Editor -- /bin/bash
~/.local/bin/royalterminal-mux capture Work Editor
```

A read-only workflow uploads the unsigned tarball **after** its isolated-home smoke test succeeds. `SHA256SUMS` detects payload corruption; a checksum shipped alongside an archive is **not** provenance or a signature. The package is not published as a stable release. Review the files before installing.

## Locations and lifetime

| Item | Location |
| --- | --- |
| Service | `$XDG_CONFIG_HOME/systemd/user/royalterminal-mux.service` (defaults to `~/.config/systemd/user`) |
| Socket | `$XDG_RUNTIME_DIR/royalterminal-mux/host.sock`; parent is owned by the user, mode 0700 |
| Token and JSON automation | `~/.config/royalterminal-mux/`; token is created once, mode 0600 |
| Immutable release directories | `~/.local/share/royalterminal-mux/releases/` |
| Next launch | `~/.local/share/royalterminal-mux/current` |

The installed wrapper reads the local token **only** for the personal endpoint. Explicit `--endpoint` and `--socket` selections require their own credential in the environment. The unwrapped CLI supports `--socket /path/to/socket` or `ROYALTERMINAL_MUX_SOCKET`. A missing socket is an error; it never falls back to TCP.

Run `install` without `--start` to stage files only. Staging another build preserves the credential, the running process and all its live terminals. The service launcher resolves its release directory at startup so an upgrade cannot replace a lazily loaded dependency. Only the next deliberate start selects the new release. Restarting or stopping this host still ends its processes; there is **no** daemon live-upgrade or reboot recovery. Keep previous releases until they are no longer in use.

```sh
systemctl --user status royalterminal-mux.service
# Explicitly stop the service and its ongoing work, disable it, and remove the managed launchers:
~/.local/share/royalterminal-mux/current/install-personal.sh uninstall --stop
```

Uninstall preserves release directories, configuration, and the token. The installer refuses to take ownership of an unrelated CLI launcher or unit. There is no root service, PAM authentication, systemd socket activation, Tailscale identity mapping, or automatic process launch on the first client request. Logging out may stop the user manager, depending on system configuration.
