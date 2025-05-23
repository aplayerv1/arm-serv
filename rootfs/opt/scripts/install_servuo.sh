#!/usr/bin/with-contenv bash
set -e

echo "================================================================================"
echo "Running install_serv.sh as $(whoami)"
echo "================================================================================"

apt-get update
apt-get install -y --no-install-recommends mono-complete rsync nano mono-devel wget gnupg

SERVUO_DIR="/opt/ServUO"

if [ ! -f "$SERVUO_DIR/ServUO.exe" ]; then
    echo "Cloning ServUO repository..."
    TMPDIR=$(mktemp -d)
    git clone https://github.com/ServUO/ServUO.git "$TMPDIR"
    rsync -av "$TMPDIR/" "$SERVUO_DIR/"
    rm -rf "$TMPDIR"

    mkdir -p "$SERVUO_DIR/Scripts/Custom/TelnetConsole"
    mkdir -p "$SERVUO_DIR/Scripts/Custom/Webserver"
    cp /opt/scripts/Startup/*.cs "$SERVUO_DIR/Scripts/Custom/"
    cp /opt/scripts/TelnetConsole/*.cs "$SERVUO_DIR/Scripts/Custom/TelnetConsole/"
    cp /opt/scripts/Webserver/*.cs "$SERVUO_DIR/Scripts/Custom/Webserver/"
    cp /opt/scripts/Webserver/map.html "$SERVUO_DIR/index.html"

    echo "Installing dotnet SDK"
    if ! command -v dotnet &> /dev/null; then
        wget https://packages.microsoft.com/config/debian/11/packages-microsoft-prod.deb
        dpkg -i packages-microsoft-prod.deb
        apt-get update
        apt-get install -y dotnet-sdk-7.0
    fi

    echo "Building ServUO..."
    export DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
    export PATH="$PATH:$HOME/.dotnet:$HOME/.dotnet/tools"
    cd "$SERVUO_DIR"
    nuget install Newtonsoft.Json
    DOTNET_CLI_HOME="$SERVUO_DIR" dotnet build --self-contained true -p:PublishSingleFile=false

    chmod -R 755 "$SERVUO_DIR"
    chmod -R 700 "$SERVUO_DIR/Saves"
else
    echo "ServUO already built — skipping clone and build steps."
fi

echo ""
echo "================================================================================"
echo "Install complete"
echo "================================================================================"

exit 0
