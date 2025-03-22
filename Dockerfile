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

EXPOSE 2593

# Use the full URL from Microsoft for the dotnet-install script
RUN wget -O /opt/dotnet-install.sh "https://dotnet.microsoft.com/download/dotnet/scripts/v1/dotnet-install.sh" && \
    chmod +x /opt/dotnet-install.sh && \
    cd /opt && ./dotnet-install.sh

COPY rootfs/ /

RUN ls /etc/cont-init.d/ && sleep 5

RUN mkdir -p /var/run/s6/etc/cont-init.d/ 

RUN for file in /etc/cont-init.d/*; do \
    dos2unix $file; \
    chmod a+xwr $file; \
    done && \
    dos2unix /opt/scripts/*

RUN for file in /etc/services.d/servuo/*; do \
    dos2unix $file; \
    chmod a+xwr $file; \
    done

RUN chmod +x -R /opt/scripts

RUN yes | perl -MCPAN -e 'install Text::MicroMason'

ENTRYPOINT [ "/init" ]
