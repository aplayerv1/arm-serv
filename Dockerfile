FROM --platform=linux/arm64 ubuntu:18.04
ARG DEBIAN_FRONTEND="noninteractive"
ARG S6_VERSION="v3.1.3.0"
ARG S6_ARCH="aarch64"
ARG LANG="en_US.UTF-8"
ARG LC_ALL="C.UTF-8"
ARG LANGUAGE="en_US.UTF-8"
ARG TERM="xterm-256color"
ENV S6_BEHAVIOUR_IF_STAGE2_FAILS=2
LABEL maintainer "Aplayerv1"
RUN apt-get update && apt-get install -y dirmngr gnupg apt-transport-https ca-certificates
RUN apt-key adv --keyserver hkp://keyserver.ubuntu.com:80 --recv-keys 3FA7E0328081BFF6A14DA29AA6A19B38D3D831EF

RUN sh -c 'echo "deb https://download.mono-project.com/repo/ubuntu stable-bionic main" > /etc/apt/sources.list.d/mono-official-stable.list'
RUN apt-get update && apt-get install -y wget git mono-complete pacman git xz-utils dos2unix gettext-base curl unzip

ADD https://github.com/just-containers/s6-overlay/releases/download/v3.1.3.0/s6-overlay-noarch.tar.xz /tmp
RUN tar -C / -Jxpf /tmp/s6-overlay-noarch.tar.xz
ADD "https://github.com/just-containers/s6-overlay/releases/download/v1.19.1.1/s6-overlay-aarch64.tar.gz" "/tmp/s6.tar.gz" 
RUN cd /tmp && tar xfz /tmp/s6.tar.gz -C /
EXPOSE 2593
ADD "https://dot.net/v1/dotnet-install.sh" "/opt/dotnet-install.sh" 
RUN cd /opt && chmod +x dotnet-install.sh 
RUN cd /opt  && ./dotnet-install.sh --version 5.0.400
COPY rootfs/ /
RUN mkdir -p /var/run/s6/etc/cont-init.d/ 
RUN for file in /etc/cont-init.d/*; do \
 dos2unix $file; \
 chmod a+xwr $file;\
 done && \
 dos2unix /opt/scripts/* 
RUN chmod +x -R /opt/scripts
RUN yes | perl -MCPAN -e 'install Text::MicroMason'

ENTRYPOINT [ "/init" ]
