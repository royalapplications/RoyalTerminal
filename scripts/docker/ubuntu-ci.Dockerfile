FROM ubuntu:24.04

ARG TARGETARCH
ARG DOTNET_CHANNEL=10.0
ARG ZIG_VERSION=0.15.2

ENV DEBIAN_FRONTEND=noninteractive
ENV DOTNET_ROOT=/usr/share/dotnet
ENV DOTNET_NOLOGO=true
ENV DOTNET_CLI_TELEMETRY_OPTOUT=true
ENV PATH=/usr/share/dotnet:/opt/zig:/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin

RUN apt-get update && apt-get install -y --no-install-recommends \
    ca-certificates \
    curl \
    dpkg-dev \
    file \
    git \
    glslang-dev \
    libbz2-dev \
    libfontconfig1-dev \
    libfreetype6-dev \
    libharfbuzz-dev \
    libonig-dev \
    libpng-dev \
    libspirv-cross-c-shared-dev \
    pkg-config \
    xz-utils \
    zlib1g-dev \
    && multiarch="$(dpkg-architecture -qDEB_HOST_MULTIARCH)" \
    && if [ -f "/usr/lib/${multiarch}/libbz2.so" ] && [ ! -e "/usr/lib/${multiarch}/libbzip2.so" ]; then ln -s "/usr/lib/${multiarch}/libbz2.so" "/usr/lib/${multiarch}/libbzip2.so"; fi \
    && if [ -f "/usr/lib/${multiarch}/libbz2.a" ] && [ ! -e "/usr/lib/${multiarch}/libbzip2.a" ]; then ln -s "/usr/lib/${multiarch}/libbz2.a" "/usr/lib/${multiarch}/libbzip2.a"; fi \
    && rm -rf /var/lib/apt/lists/*

RUN curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh \
    && bash /tmp/dotnet-install.sh --channel "${DOTNET_CHANNEL}" --install-dir /usr/share/dotnet \
    && ln -sf /usr/share/dotnet/dotnet /usr/local/bin/dotnet \
    && rm -f /tmp/dotnet-install.sh

RUN target_arch="${TARGETARCH}" \
    && if [ -z "${target_arch}" ]; then \
        target_arch="$(dpkg --print-architecture)"; \
    fi \
    && case "${target_arch}" in \
        amd64) zig_arch="x86_64" ;; \
        arm64) zig_arch="aarch64" ;; \
        *) echo "Unsupported TARGETARCH: ${target_arch}" >&2; exit 1 ;; \
    esac \
    && curl -fsSL -o /tmp/zig.tar.xz "https://ziglang.org/download/${ZIG_VERSION}/zig-${zig_arch}-linux-${ZIG_VERSION}.tar.xz" \
    && tar -C /opt -xf /tmp/zig.tar.xz \
    && mv "/opt/zig-${zig_arch}-linux-${ZIG_VERSION}" /opt/zig \
    && ln -sf /opt/zig/zig /usr/local/bin/zig \
    && rm -f /tmp/zig.tar.xz

WORKDIR /work
