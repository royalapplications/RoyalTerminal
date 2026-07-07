#!/usr/bin/env bash
# run-ubuntu-ci-docker.sh — Reproduce the Ubuntu CI build/test job locally using Docker Desktop.
#
# Usage:
#   ./scripts/run-ubuntu-ci-docker.sh
#   ./scripts/run-ubuntu-ci-docker.sh --shell
#   ./scripts/run-ubuntu-ci-docker.sh --platform linux/arm64
#   ./scripts/run-ubuntu-ci-docker.sh --skip-image-build

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
ROOT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
DOCKERFILE="$ROOT_DIR/scripts/docker/ubuntu-ci.Dockerfile"
IMAGE_TAG="${IMAGE_TAG:-royalterminal-ubuntu-ci:local}"
PLATFORM="${PLATFORM:-linux/amd64}"
SHELL_ONLY=false
SKIP_IMAGE_BUILD=false
BUILD_ARCH=""

usage() {
    cat <<'EOF'
Usage: ./scripts/run-ubuntu-ci-docker.sh [options]

Reproduce the Ubuntu GitHub Actions build/test job locally on macOS using Docker Desktop.

Options:
  --platform <platform>   Docker platform to use (default: linux/amd64)
  --image-tag <tag>       Docker image tag to build/run (default: royalterminal-ubuntu-ci:local)
  --skip-image-build      Reuse an existing image instead of rebuilding it
  --shell                 Start an interactive shell in the prepared container
  --help                  Show this help message
EOF
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        --platform)
            PLATFORM="$2"
            shift 2
            ;;
        --image-tag)
            IMAGE_TAG="$2"
            shift 2
            ;;
        --skip-image-build)
            SKIP_IMAGE_BUILD=true
            shift
            ;;
        --shell)
            SHELL_ONLY=true
            shift
            ;;
        --help)
            usage
            exit 0
            ;;
        *)
            echo "Unknown option: $1" >&2
            usage >&2
            exit 1
            ;;
    esac
done

case "$PLATFORM" in
    linux/amd64)
        BUILD_ARCH="amd64"
        BUILD_RID="linux-x64"
        ZIG_TARGET="x86_64-linux-gnu"
        ZIG_CPU="x86_64-vzeroupper"
        ;;
    linux/arm64)
        BUILD_ARCH="arm64"
        BUILD_RID="linux-arm64"
        ZIG_TARGET="aarch64-linux-gnu"
        ZIG_CPU="baseline"
        ;;
    *)
        echo "Unsupported platform: $PLATFORM" >&2
        exit 1
        ;;
esac

if ! command -v docker >/dev/null 2>&1; then
    echo "docker is not installed. Install Docker Desktop first." >&2
    exit 1
fi

if ! docker info >/dev/null 2>&1; then
    echo "docker is installed but the Docker daemon is not available. Start Docker Desktop first." >&2
    exit 1
fi

if [ ! -f "$DOCKERFILE" ]; then
    echo "Dockerfile not found: $DOCKERFILE" >&2
    exit 1
fi

if [ "$SKIP_IMAGE_BUILD" = false ]; then
    docker build \
        --platform "$PLATFORM" \
        --build-arg "TARGETARCH=$BUILD_ARCH" \
        -f "$DOCKERFILE" \
        -t "$IMAGE_TAG" \
        "$ROOT_DIR"
fi

DOCKER_RUN_ARGS=(--rm)
if [ -t 0 ] && [ -t 1 ]; then
    DOCKER_RUN_ARGS+=(-it)
fi

HOST_UID="$(id -u)"
HOST_GID="$(id -g)"

read -r -d '' INNER_COMMAND <<'EOF' || true
set -euo pipefail

git config --global --add safe.directory /work
git submodule update --init --recursive

if [ "${ROYALTERMINAL_SHELL_ONLY:-0}" = "1" ]; then
    exec bash
fi

mkdir -p \
  "artifacts/${ROYALTERMINAL_BUILD_RID}/native" \
  "native/${ROYALTERMINAL_BUILD_RID}" \
  "src/RoyalTerminal.GhosttySharp.Native.Linux64/runtimes/${ROYALTERMINAL_BUILD_RID}/native" \
  "src/RoyalTerminal.Rendering.Interop.Ghostty/runtimes/${ROYALTERMINAL_BUILD_RID}/native" \
  test-results

(
  cd external/ghostty
  build_args=(
    build
    -Doptimize=ReleaseFast
    -Dapp-runtime=none
    -Demit-lib-vt=true
    -Demit-xcframework=false
    "-Dtarget=${ROYALTERMINAL_ZIG_TARGET}"
    -Dsimd=false
    -fsys=freetype
    -fsys=fontconfig
    -fsys=libpng
    -fsys=zlib
    -fsys=oniguruma
    -fsys=glslang
    -fsys=spirv-cross
  )
  if [ -n "${ROYALTERMINAL_ZIG_CPU}" ]; then
    build_args+=("-Dcpu=${ROYALTERMINAL_ZIG_CPU}")
  fi
  zig "${build_args[@]}"
)

(
  cd native/ghostty-renderer-capi
  build_args=(build -Doptimize=ReleaseFast "-Dtarget=${ROYALTERMINAL_ZIG_TARGET}")
  if [ -n "${ROYALTERMINAL_ZIG_CPU}" ]; then
    build_args+=("-Dcpu=${ROYALTERMINAL_ZIG_CPU}")
  fi
  zig "${build_args[@]}"
)

find external/ghostty/zig-out -name "libghostty-vt.so*" -exec cp -L {} "artifacts/${ROYALTERMINAL_BUILD_RID}/native/" \;
find native/ghostty-renderer-capi/zig-out -name "libghostty-renderer-capi.so*" -exec cp -L {} "artifacts/${ROYALTERMINAL_BUILD_RID}/native/" \; || true

for base in libghostty-vt libghostty-renderer-capi; do
  if [ ! -f "artifacts/${ROYALTERMINAL_BUILD_RID}/native/${base}.so" ]; then
    candidate="$(find "artifacts/${ROYALTERMINAL_BUILD_RID}/native" -maxdepth 1 -type f -name "${base}.so*" | head -n1 || true)"
    if [ -n "${candidate}" ]; then
      cp -f "${candidate}" "artifacts/${ROYALTERMINAL_BUILD_RID}/native/${base}.so"
    fi
  fi
done

cp -f "artifacts/${ROYALTERMINAL_BUILD_RID}/native/"* "native/${ROYALTERMINAL_BUILD_RID}/"
cp -f "artifacts/${ROYALTERMINAL_BUILD_RID}/native/"* "src/RoyalTerminal.GhosttySharp.Native.Linux64/runtimes/${ROYALTERMINAL_BUILD_RID}/native/"
cp -f "artifacts/${ROYALTERMINAL_BUILD_RID}/native/"* "src/RoyalTerminal.Rendering.Interop.Ghostty/runtimes/${ROYALTERMINAL_BUILD_RID}/native/"

dotnet restore
dotnet build -c Release --no-restore

slice_names=(
  "docker-linux-slice-1"
  "docker-linux-slice-2"
  "docker-linux-slice-3"
  "docker-linux-slice-4"
)

slice_filters=(
  "(FullyQualifiedName~RoyalTerminal.Tests.GhosttyComponentTests.)|(FullyQualifiedName~RoyalTerminal.Tests.GhosttyUnsupportedWindowsSequenceSanitizerTests.)|(FullyQualifiedName~RoyalTerminal.Tests.HeadlessSkiaRenderingTests.)|(FullyQualifiedName~RoyalTerminal.Tests.NativeEnumTests.)|(FullyQualifiedName~RoyalTerminal.Tests.NativeTerminalControlTests.)|(FullyQualifiedName~RoyalTerminal.Tests.NcursesHarnessFlowTests.)|(FullyQualifiedName~RoyalTerminal.Tests.TerminalControlHeadlessInteractionTests.)|(FullyQualifiedName~RoyalTerminal.Tests.TerminalQueryTests.)"
  "(FullyQualifiedName~RoyalTerminal.Tests.MainWindowControllerModeStartupTests.)|(FullyQualifiedName~RoyalTerminal.Tests.MainWindowControllerSettingsPanelTests.)|(FullyQualifiedName~RoyalTerminal.Tests.MainWindowViewModelFlowTests.)|(FullyQualifiedName~RoyalTerminal.Tests.PackageBoundaryTests.)|(FullyQualifiedName~RoyalTerminal.Tests.RenderingAvaloniaAdapterTests.)|(FullyQualifiedName~RoyalTerminal.Tests.RenderingContractsTests.)|(FullyQualifiedName~RoyalTerminal.Tests.RenderingInteropTests.)|(FullyQualifiedName~RoyalTerminal.Tests.RenderingSkiaInteropTests.)|(FullyQualifiedName~RoyalTerminal.Tests.RenderingTests.)|(FullyQualifiedName~RoyalTerminal.Tests.TerminalControlTests.)|(FullyQualifiedName~RoyalTerminal.Tests.TerminalScreenTests.)|(FullyQualifiedName~RoyalTerminal.Tests.TerminalScrollDataTests.)|(FullyQualifiedName~RoyalTerminal.Tests.TerminalSettingsPanelStateTests.)|(FullyQualifiedName~RoyalTerminal.Tests.TerminalThemeTests.)|(FullyQualifiedName~RoyalTerminal.Tests.UnicodeWidthTests.)|(FullyQualifiedName~RoyalTerminal.Tests.WindowsNativePackagingTests.)"
  "(FullyQualifiedName~RoyalTerminal.Tests.KnownHostsSshHostKeyValidatorTests.)|(FullyQualifiedName~RoyalTerminal.Tests.PipeTerminalTransportTests.)|(FullyQualifiedName~RoyalTerminal.Tests.PtyContractTests.)|(FullyQualifiedName~RoyalTerminal.Tests.PtyTerminalTransportTests.)|(FullyQualifiedName~RoyalTerminal.Tests.RawTcpTerminalTransportTests.)|(FullyQualifiedName~RoyalTerminal.Tests.SerialTerminalTransportTests.)|(FullyQualifiedName~RoyalTerminal.Tests.SshCredentialProvidersTests.)|(FullyQualifiedName~RoyalTerminal.Tests.SshHostKeyValidationTests.)|(FullyQualifiedName~RoyalTerminal.Tests.SshNetAgentAuthenticationMethodContributorTests.)|(FullyQualifiedName~RoyalTerminal.Tests.SshNetTerminalTransportSecurityTests.)|(FullyQualifiedName~RoyalTerminal.Tests.SshShellBootstrapCommandBuilderTests.)|(FullyQualifiedName~RoyalTerminal.Tests.TelnetTerminalTransportTests.)|(FullyQualifiedName~RoyalTerminal.Tests.TerminalSessionProfileSerializerTests.)|(FullyQualifiedName~RoyalTerminal.Tests.TerminalSessionServiceTransportTests.)|(FullyQualifiedName~RoyalTerminal.Tests.TerminalTransportFactoryTests.)"
  "(FullyQualifiedName~RoyalTerminal.Tests.TerminalAbstractionsTests.)|(FullyQualifiedName~RoyalTerminal.Tests.TerminalBufferTests.)|(FullyQualifiedName~RoyalTerminal.Tests.TerminalCaptureRecorderTests.)|(FullyQualifiedName~RoyalTerminal.Tests.TerminalCaptureRuntimeTests.)|(FullyQualifiedName~RoyalTerminal.Tests.TerminalDataProcessorTests.)|(FullyQualifiedName~RoyalTerminal.Tests.TerminalInputAdapterTests.)|(FullyQualifiedName~RoyalTerminal.Tests.TerminalModeResolverTests.)|(FullyQualifiedName~RoyalTerminal.Tests.TerminalMouseProtocolTests.)|(FullyQualifiedName~RoyalTerminal.Tests.TerminalShortcutDispatcherTests.)|(FullyQualifiedName~RoyalTerminal.Tests.TerminalWin32InputModeTrackerTests.)"
)

for i in "${!slice_names[@]}"; do
  name="${slice_names[$i]}"
  filter="${slice_filters[$i]}"
  dotnet test tests/RoyalTerminal.Tests/RoyalTerminal.Tests.csproj \
    -c Release \
    --no-build \
    --filter "$filter" \
    --logger "trx;LogFileName=${name}.trx" \
    --results-directory ./test-results
done

dotnet test tests/RoyalTerminal.Tests/RoyalTerminal.Tests.csproj \
  -c Release \
  --no-build \
  --filter "(FullyQualifiedName~TerminalModeResolverTests)|(FullyQualifiedName~MainWindowControllerModeStartupTests)|(FullyQualifiedName~Headless_FallbackParity_AutoAndManaged_PreserveKeyboardMousePasteAndResize_WhenHostedInScrollViewer)" \
  --logger "trx;LogFileName=docker-mode-startup-smoke-results.trx" \
  --results-directory ./test-results
EOF

docker run \
    "${DOCKER_RUN_ARGS[@]}" \
    --platform "$PLATFORM" \
    --user "${HOST_UID}:${HOST_GID}" \
    -e HOME=/tmp \
    -e ROYALTERMINAL_SHELL_ONLY="$([ "$SHELL_ONLY" = true ] && echo 1 || echo 0)" \
    -e ROYALTERMINAL_BUILD_RID="$BUILD_RID" \
    -e ROYALTERMINAL_ZIG_TARGET="$ZIG_TARGET" \
    -e ROYALTERMINAL_ZIG_CPU="$ZIG_CPU" \
    -v "$ROOT_DIR:/work" \
    -w /work \
    "$IMAGE_TAG" \
    bash -lc "$INNER_COMMAND"
