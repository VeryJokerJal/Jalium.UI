FROM ubuntu:20.04

ARG JALIUM_INSTALL_DOTNET=0
ARG JALIUM_DOTNET_SDK_VERSION=10.0.301

ENV DEBIAN_FRONTEND=noninteractive
ENV PATH="/usr/share/dotnet:${PATH}"

RUN set -eux; \
    attempt=1; \
    while :; do \
      if apt-get -o Acquire::Retries=5 -o Acquire::ForceIPv4=true update \
          && apt-get -o Acquire::Retries=5 -o Acquire::ForceIPv4=true \
            install -y --no-install-recommends \
              bash binutils build-essential ca-certificates file git ninja-build pkg-config python3-pip \
              libx11-dev libxext-dev libxrandr-dev libxi-dev libxcursor-dev xclip xvfb \
              libwayland-dev wayland-protocols libxkbcommon-dev weston \
              libvulkan-dev mesa-vulkan-drivers \
              libfontconfig1-dev fonts-dejavu-core fonts-noto-cjk \
              libgstreamer1.0-dev libgstreamer-plugins-base1.0-dev \
              gstreamer1.0-tools gstreamer1.0-plugins-base gstreamer1.0-plugins-good \
              gstreamer1.0-plugins-bad gstreamer1.0-libav ffmpeg; then \
        break; \
      fi; \
      if [ "$attempt" -ge 5 ]; then exit 1; fi; \
      apt-get clean; \
      rm -rf /var/lib/apt/lists/*; \
      sleep "$((attempt * 10))"; \
      attempt="$((attempt + 1))"; \
    done; \
    python3 -m pip install --no-cache-dir cmake==3.31.10; \
    rm -rf /var/lib/apt/lists/*

RUN if [ "$JALIUM_INSTALL_DOTNET" = 1 ]; then \
      attempt=1; \
      while :; do \
        if apt-get -o Acquire::Retries=5 -o Acquire::ForceIPv4=true update \
            && apt-get -o Acquire::Retries=5 -o Acquire::ForceIPv4=true \
              install -y --no-install-recommends clang curl ca-certificates zlib1g-dev libicu66; then \
          break; \
        fi; \
        if [ "$attempt" -ge 5 ]; then exit 1; fi; \
        apt-get clean; \
        rm -rf /var/lib/apt/lists/*; \
        sleep "$((attempt * 10))"; \
        attempt="$((attempt + 1))"; \
      done; \
      curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh && \
      bash /tmp/dotnet-install.sh \
        --version "$JALIUM_DOTNET_SDK_VERSION" \
        --install-dir /usr/share/dotnet \
        --no-path && \
      ln -sf /usr/share/dotnet/dotnet /usr/local/bin/dotnet && \
      /usr/share/dotnet/dotnet --list-sdks && \
      rm -f /tmp/dotnet-install.sh && \
      rm -rf /var/lib/apt/lists/*; \
    fi
