#!/usr/bin/env bash
# Install under new names only. Never edit site PAM policy, enable a socket, start a service, or change user accounts.
set -euo pipefail
umask 022
[[ $EUID == 0 && $# == 1 && -r /etc/debian_version ]] || { echo 'Usage (Debian/Ubuntu, root): install-debian.sh /absolute/path/to/trusted-publish' >&2; exit 2; }
base=$(cd -- "$(dirname -- "$0")" && pwd -P); src=$(realpath -e -- "$1")
[[ -x /usr/bin/python3 && -x /usr/bin/dotnet && -x /usr/bin/setfacl && -x /usr/lib/systemd/systemd-socket-proxyd && -f /etc/pam.d/common-account && -f /etc/pam.d/common-session-noninteractive ]] || { echo 'Install .NET 10 ASP.NET Core Runtime, Python 3, acl, systemd and Debian PAM first.' >&2; exit 1; }
[[ -f "$src/RoyalTerminal.Multiplexing.Server.dll" ]] || { echo 'Missing published workspace host.' >&2; exit 1; }
[[ -z $(find "$src" ! -type f ! -type d -print -quit) ]] || { echo 'Publish input must contain only regular files and directories.' >&2; exit 1; }
unit=/etc/systemd/system; helper=/usr/local/libexec/royalterminal-account; app=/opt/royalterminal-account
for p in "$app" "$helper" "$unit/royalterminal-account@.service" "$unit/royalterminal-login@.service" "$unit/royalterminal-login@.socket" /etc/pam.d/royalterminal-workspace /etc/royalterminal-login; do
  [[ ! -e $p && ! -L $p ]] || { echo "Refusing to overwrite $p" >&2; exit 1; }
done
install -d -m 0755 "$app/server" "$helper" /etc/royalterminal-login
cp -r --no-preserve=ownership -- "$src/." "$app/server/"; chmod -R go-w "$app/server"
install -m 0755 "$base/access.py" "$base/launch.py" "$helper/"
install -m 0644 "$base"/*.service "$base"/*.socket "$unit/"
install -m 0644 "$base/royalterminal-workspace.pam" /etc/pam.d/royalterminal-workspace
install -m 0600 "$base/accounts.example.json" /etc/royalterminal-login/accounts.json
systemctl daemon-reload
printf '%s\n' 'Installed. No accounts are enabled. Configure /etc/royalterminal-login/accounts.json before starting an entry socket.'
