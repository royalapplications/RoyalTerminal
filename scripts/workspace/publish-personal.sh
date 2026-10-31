#!/usr/bin/env bash
# Produce a relocatable, framework-dependent Linux personal-server bundle. No credentials or signing keys.
set -euo pipefail
[[ $# == 1 ]] || { echo 'Usage: publish-personal.sh NEW_OUTPUT_DIRECTORY' >&2; exit 2; }
output=$(realpath -m -- "$1"); [[ ! -e "$output" ]] || { echo 'Output directory already exists' >&2; exit 2; }
cd "$(dirname "$0")/../.."; mkdir -p -- "$(dirname "$output")"
stage=$(mktemp -d "$(dirname "$output")/.mux-build.XXXXXXXX"); trap 'rm -rf -- "$stage"' EXIT
npm --prefix samples/RoyalTerminal.Multiplexing.Server ci
npm --prefix samples/RoyalTerminal.Multiplexing.Server run build
dotnet publish samples/RoyalTerminal.Multiplexing.Server -c Release --self-contained false -p:UseAppHost=false -o "$stage/server"
dotnet publish samples/RoyalTerminal.Multiplexing.Cli -c Release --self-contained false -p:UseAppHost=false -o "$stage/cli"
install -m755 scripts/workspace/{install-personal.sh,launch-personal.sh,personal-cli.sh} "$stage/"
install -m644 LICENSE "$stage/LICENSE"
(cd "$stage" && find . -type f ! -name SHA256SUMS -print0 | sort -z | xargs -0 sha256sum >SHA256SUMS)
mv -T -- "$stage" "$output"; trap - EXIT
