#!/usr/bin/env bash
# Installs only user-owned files. Upgrades switch the next start, never restart an active service.
set -euo pipefail; umask 077
[[ $(uname -s) == Linux && $EUID != 0 ]] || { echo 'Run as a regular Linux user, not root.' >&2; exit 2; }
base="$HOME/.local/share/royalterminal-mux"; bin="$HOME/.local/bin"; conf="$HOME/.config/royalterminal-mux"; units="${XDG_CONFIG_HOME:-$HOME/.config}/systemd/user"; unit="$units/royalterminal-mux.service"
managed() { [[ ! -L "$1" ]] && { [[ ! -e "$1" ]] || grep -Fq 'royalterminal-managed-personal-v1' "$1"; }; }
for path in "$bin/royalterminal-mux" "$unit"; do managed "$path" || { echo "Refusing to replace unmanaged file: $path" >&2; exit 1; }; done
case ${1-install} in
  uninstall)
    if [[ ${2-} == --stop ]]; then systemctl --user disable --now royalterminal-mux.service; elif systemctl --user is-active --quiet royalterminal-mux.service; then echo 'Active terminals would stop. Use uninstall --stop explicitly.' >&2; exit 2; fi
    if systemctl --user is-enabled --quiet royalterminal-mux.service; then systemctl --user disable royalterminal-mux.service; fi
    rm -f -- "$bin/royalterminal-mux" "$unit"; echo "Removed unit and CLI launcher. Preserved releases and credentials: $base, $conf"; exit 0;;
  install) ;;
  *) echo 'Usage: install-personal.sh install [--start] | uninstall [--stop]' >&2; exit 2;;
esac
[[ $# -le 2 && ( -z ${2-} || ${2-} == --start ) ]] || { echo 'Unexpected install option' >&2; exit 2; }
payload=$(cd "$(dirname "$0")" && pwd -P)
[[ -f "$payload/SHA256SUMS" && -f "$payload/server/RoyalTerminal.Multiplexing.Server.dll" && -f "$payload/cli/royalterminal-mux.dll" && -f "$payload/server/wwwroot/vendor/xterm.js" ]] || { echo 'Use install-personal.sh inside a published bundle.' >&2; exit 2; }
(cd "$payload" && sha256sum --quiet --check SHA256SUMS)
mkdir -p -- "$bin" "$units" "$base/releases" "$conf"
[[ -O "$conf" && ! -L "$conf" ]] || { echo 'Unsafe configuration directory' >&2; exit 1; }; chmod 700 "$conf"
# Link a complete credential atomically; never truncate or replace a concurrently created token.
tmp=$(mktemp "$conf/.token.XXXXXXXX"); next=$(mktemp -d "$base/.stage.XXXXXXXX"); trap 'rm -f -- "$tmp"; rm -rf -- "$next"' EXIT
openssl rand -hex 32 >"$tmp"; ln "$tmp" "$conf/token" 2>/dev/null || [[ -f "$conf/token" ]]
[[ -O "$conf/token" && ! -L "$conf/token" && $(stat -c %a "$conf/token") == 600 ]] || { echo 'Existing token is insecure; refusing install.' >&2; exit 1; }
release=$(mktemp -d "$base/releases/build.XXXXXXXX"); cp -a -- "$payload/." "$release/"
cat >"$next/unit" <<'UNIT'
# royalterminal-managed-personal-v1
[Unit]
Description=RoyalTerminal experimental personal workspace
[Service]
Type=simple
RuntimeDirectory=royalterminal-mux
RuntimeDirectoryMode=0700
UMask=0077
NoNewPrivileges=yes
ExecStart="%h/.local/share/royalterminal-mux/current/launch-personal.sh"
Restart=no
[Install]
WantedBy=default.target
UNIT
ln -s -- "$release" "$next/current"; mv -Tf -- "$next/current" "$base/current"
install -m644 "$next/unit" "$unit"; install -m755 "$release/personal-cli.sh" "$bin/royalterminal-mux"
if [[ ${2-} == --start ]]; then systemctl --user daemon-reload; systemctl --user enable --now royalterminal-mux.service; fi
printf 'Installed %s. Active sessions were not restarted.\n' "$release"
