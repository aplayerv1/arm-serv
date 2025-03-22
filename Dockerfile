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

# Download and install s6-overlay with all components
RUN if [ "$TARGETARCH" = "arm64" ]; then \
      S6_ARCH="aarch64"; \
    else \
      S6_ARCH="x86_64"; \
    fi && \
    wget -O /tmp/s6-overlay-noarch.tar.xz "https://github.com/just-containers/s6-overlay/releases/download/${S6_VERSION}/s6-overlay-noarch.tar.xz" && \
    tar -C / -Jxpf /tmp/s6-overlay-noarch.tar.xz && \
    wget -O /tmp/s6-overlay-arch.tar.xz "https://github.com/just-containers/s6-overlay/releases/download/${S6_VERSION}/s6-overlay-${S6_ARCH}.tar.xz" && \
    tar -C / -Jxpf /tmp/s6-overlay-arch.tar.xz && \
    wget -O /tmp/s6-overlay-symlinks-noarch.tar.xz "https://github.com/just-containers/s6-overlay/releases/download/${S6_VERSION}/s6-overlay-symlinks-noarch.tar.xz" && \
    tar -C / -Jxpf /tmp/s6-overlay-symlinks-noarch.tar.xz && \
    wget -O /tmp/s6-overlay-symlinks-arch.tar.xz "https://github.com/just-containers/s6-overlay/releases/download/${S6_VERSION}/s6-overlay-symlinks-arch.tar.xz" && \
    tar -C / -Jxpf /tmp/s6-overlay-symlinks-arch.tar.xz && \
    wget -O /tmp/syslogd-overlay-noarch.tar.xz "https://github.com/just-containers/s6-overlay/releases/download/${S6_VERSION}/syslogd-overlay-noarch.tar.xz" && \
    tar -C / -Jxpf /tmp/syslogd-overlay-noarch.tar.xz

EXPOSE 2593

# Use the full URL from Microsoft for the dotnet-install script
RUN wget -O /opt/dotnet-install.sh "https://dotnet.microsoft.com/download/dotnet/scripts/v1/dotnet-install.sh" && \
    chmod +x /opt/dotnet-install.sh && \
    cd /opt && ./dotnet-install.sh

# Copy rootfs content
COPY rootfs/ /

# Fix permissions and line endings for all scripts
RUN chmod -R 755 /etc/cont-init.d && \
    find /etc/cont-init.d -type f -exec dos2unix {} \; && \
    chmod -R 755 /opt/scripts && \
    find /opt/scripts -type f -exec dos2unix {} \;

# Create symlink for with-contenv if it doesn't exist in the expected location
RUN if [ ! -f /usr/bin/with-contenv ]; then \
      mkdir -p /usr/bin && \
      if [ -f /command/with-contenv ]; then \
        ln -sf /command/with-contenv /usr/bin/with-contenv; \
      elif [ -f /bin/with-contenv ]; then \
        ln -sf /bin/with-contenv /usr/bin/with-contenv; \
      elif [ -f /package/admin/s6-overlay-${S6_VERSION}/bin/with-contenv ]; then \
        ln -sf /package/admin/s6-overlay-${S6_VERSION}/bin/with-contenv /usr/bin/with-contenv; \
      else \
        find / -name with-contenv -type f -executable | head -1 | xargs -I{} ln -sf {} /usr/bin/with-contenv || \
        (echo '#!/bin/sh\nexec "$@"' > /usr/bin/with-contenv && chmod +x /usr/bin/with-contenv); \
      fi; \
    fi

# Verify the changes
RUN ls -la /usr/bin/with-contenv || echo "with-contenv not found in /usr/bin" && \
    find / -name with-contenv -type f -executable || echo "with-contenv not found anywhere"

RUN yes | perl -MCPAN -e 'install Text::MicroMason'

RUN DEBIAN_FRONTEND=noninteractive apt-get -yq install mono-devel rsync nano

ENTRYPOINT [ "/init" ]
