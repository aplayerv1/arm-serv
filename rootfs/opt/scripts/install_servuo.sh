#!/usr/bin/with-contenv bash

echo "================================================================================"
echo "Running install_serv.sh as $(whoami)"
echo "================================================================================"

# Ensure system is updated and mono is installed
apt-get update 
apt-get install -y mono-complete rsync nano mono-devel

echo ""
echo "================================================================================"
echo "Checking for existing ServUO build"
echo "================================================================================"

# If ServUO.exe doesn't exist, clone and build
if [ ! -f /opt/ServUO/ServUO.exe ]; then
    echo "Cloning ServUO repository..."
    cd /root/
    git clone https://github.com/ServUO/ServUO.git

    echo "Copying ServUO to /opt..."
    rsync -av /root/ServUO/ /opt/ServUO/
    rm -rf /root/ServUO
    mkdir -p /opt/ServUO/Scripts/Cu
    echo "================================================================================"
    echo "Copying custom Startup scripts"
    echo "================================================================================"
    cp /opt/scripts/Startup/*.cs /opt/ServUO/Scripts/Custom/

    echo "================================================================================"
    echo "Copying custom TelnetConsole scripts"
    echo "================================================================================"
    mkdir -p /opt/ServUO/Scripts/Custom/TelnetConsole
    cp /opt/scripts/TelnetConsole/*.cs /opt/ServUO/Scripts/Custom/TelnetConsole/

    echo "================================================================================"
    echo "Copying custom Webserver scripts"
    echo "================================================================================"

    mkdir -p /opt/ServUO/Scripts/Custom/Webserver
    cp /opt/scripts/Webserver/*.cs /opt/ServUO/Scripts/Custom/Webserver/
    cp /opt/scripts/Webserver/map.html /opt/ServUO/index.html

    echo "================================================================================"
    echo "Building Microsoft stuff scripts..."
    echo "================================================================================"

    cd /tmp
    wget https://packages.microsoft.com/config/debian/11/packages-microsoft-prod.deb
    dpkg -i packages-microsoft-prod.deb
    apt update
    apt install -y dotnet-sdk-7.0

    echo "================================================================================"
    echo "Building ServUO scripts..."
    echo "================================================================================"
    export DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
    export PATH=$PATH:$HOME/.dotnet:$HOME/.dotnet/tools
    cd /opt/ServUO
    nuget install Newtonsoft.Json
    DOTNET_CLI_HOME=/opt/ServUO dotnet build --self-contained true -p:PublishSingleFile=false

    chmod -R 777 /opt/ServUO/
    chmod -R 777 /opt/ServUO/Saves
else
    echo "ServUO already built — skipping clone and build steps."
fi

echo ""
echo "================================================================================"
echo "Install complete"
echo "================================================================================"

exit 0
