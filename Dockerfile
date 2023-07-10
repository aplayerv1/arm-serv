FROM torizon/debian:2-bullseye
ARG DEBIAN_FRONTEND="noninteractive"
ARG S6_VERSION="v3.1.3.0"
ARG S6_ARCH="amd64"
ARG LANG="en_US.UTF-8"
ARG LC_ALL="C.UTF-8"
ARG LANGUAGE="en_US.UTF-8"
ARG TERM="xterm-256color"
ENV S6_BEHAVIOUR_IF_STAGE2_FAILS=2
LABEL maintainer "Aplayerv1"
RUN apt-get update && apt-get install -y -q  wget make git pacman libcairo2-dev libjpeg62-turbo-dev libpango1.0-dev libgif-dev build-essential g++ git xz-utils dos2unix gettext-base curl unzip dirmngr gnupg apt-transport-https ca-certificates
ADD https://github.com/just-containers/s6-overlay/releases/download/v3.1.3.0/s6-overlay-noarch.tar.xz /tmp
RUN tar -C / -Jxpf /tmp/s6-overlay-noarch.tar.xz
ADD "https://github.com/just-containers/s6-overlay/releases/download/v1.19.1.1/s6-overlay-aarch64.tar.gz" "/tmp/s6.tar.gz" 
RUN cd /tmp && tar xfz /tmp/s6.tar.gz -C /
EXPOSE 2593
ADD "https://dot.net/v1/dotnet-install.sh" "/opt/dotnet-install.sh" 
RUN cd /opt && chmod +x dotnet-install.sh && ls
RUN cd /opt  && ./dotnet-install.sh --version 5.0.400
COPY rootfs/ /
RUN mkdir -p /var/run/s6/etc/cont-init.d/ 
RUN for file in /etc/cont-init.d/*; do \
 dos2unix $file; \
 chmod a+xwr $file;\
 done && \
 dos2unix /opt/scripts/* 
RUN  for file in /etc/services.d/servuo/*; do \
 dos2unix $file; \
 chmod a+xwr $file; \
 done
RUN chmod +x -R /opt/scripts
RUN yes | perl -MCPAN -e 'install Text::MicroMason'


ENTRYPOINT [ "/init" ]
