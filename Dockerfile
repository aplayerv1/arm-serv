FROM debian:bullseye

ARG DEBIAN_FRONTEND="noninteractive"
ARG S6_VERSION="v3.1.3.0"
# Auto-detect architecture
ARG TARGETARCH

ARG LANG="en_US.UTF-8"
ARG LC_ALL="C.UTF-8"
ARG LANGUAGE="en_US.UTF-8"
ARG TERM="xterm-256color"

ENV S6_BEHAVIOUR_IF_STAGE2_FAILS=2

LABEL maintainer "Aplayerv1"

RUN apt-get update && apt-get install -y -q wget mono-devel mono-mcs make git pacman libcairo2-dev libjpeg62-turbo-dev libpango1.0-dev libgif-dev build-essential g++ git xz-utils dos2unix gettext-base curl unzip dirmngr gnupg apt-transport-https ca-certificates

# Download and install s6-overlay
RUN if [ "$TARGETARCH" = "arm64" ]; then \
      S6_ARCH="aarch64"; \
    else \
      S6_ARCH="x86_64"; \
    fi && \
    wget -O /tmp/s6-overlay-noarch.tar.xz "https://github.com/just-containers/s6-overlay/releases/download/${S6_VERSION}/s6-overlay-noarch.tar.xz" && \
    tar -C / -Jxpf /tmp/s6-overlay-noarch.tar.xz && \
    wget -O /tmp/s6-overlay-arch.tar.xz "https://github.com/just-containers/s6-overlay/releases/download/${S6_VERSION}/s6-overlay-${S6_ARCH}.tar.xz" && \
    tar -C / -Jxpf /tmp/s6-overlay-arch.tar.xz

# Install additional S6 components
RUN if [ "$TARGETARCH" = "arm64" ]; then \
      S6_ARCH="aarch64"; \
    else \
      S6_ARCH="x86_64"; \
    fi && \
    wget -O /tmp/s6-overlay-symlinks.tar.xz "https://github.com/just-containers/s6-overlay/releases/download/${S6_VERSION}/s6-overlay-symlinks-${S6_ARCH}.tar.xz" && \
    tar -C / -Jxpf /tmp/s6-overlay-symlinks.tar.xz && \
    wget -O /tmp/s6-overlay-syslogng.tar.xz "https://github.com/just-containers/s6-overlay/releases/download/${S6_VERSION}/syslogng-overlay-noarch.tar.xz" && \
    tar -C / -Jxpf /tmp/s6-overlay-syslogng.tar.xz

EXPOSE 2593

# Use the full URL from Microsoft for the dotnet-install script
RUN wget -O /opt/dotnet-install.sh "https://dotnet.microsoft.com/download/dotnet/scripts/v1/dotnet-install.sh" && \
    chmod +x /opt/dotnet-install.sh && \
    cd /opt && ./dotnet-install.sh

# Copy rootfs content
COPY rootfs/ /

# Fix permissions and line endings for all scripts
RUN chmod -R 755 /etc/cont-init.d && \
    chmod -R 755 /etc/services.d && \
    find /etc/cont-init.d -type f -exec dos2unix {} \; && \
    find /etc/services.d -type f -exec dos2unix {} \; && \
    chmod -R 755 /opt/scripts && \
    find /opt/scripts -type f -exec dos2unix {} \;

# Fix shebang lines in scripts
RUN sed -i 's|#!/usr/bin/with-contenv bash|#!/bin/bash|g' /etc/cont-init.d/* && \
    sed -i 's|#!/usr/bin/with-contenv bash|#!/bin/bash|g' /etc/services.d/servuo/* && \
    echo "Modified shebang lines to use standard bash"

# Verify the changes
RUN head -n 1 /etc/cont-init.d/* && \
    echo "Script permissions after fix:" && \
    ls -la /etc/cont-init.d/

RUN yes | perl -MCPAN -e 'install Text::MicroMason'

ENTRYPOINT [ "/init" ]
