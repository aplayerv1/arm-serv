#!/usr/bin/with-contenv bash

echo "================================================================================"
echo "Running install_serv.sh as $(whoami)"
echo "================================================================================"

# Ensure system is updated and mono is installed
apt-get update 
apt-get install -y mono-complete rsync nano

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

    echo "================================================================================"
    echo "Copying custom TelnetConsole scripts"
    echo "================================================================================"
    mkdir -p /opt/ServUO/Scripts/Custom/TelnetConsole
    cp /opt/scripts/TelnetConsole/*.cs /opt/ServUO/Scripts/Custom/TelnetConsole/

    echo "================================================================================"
    echo "Building ServUO scripts..."
    echo "================================================================================"
    cd /opt/ServUO

    export DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
    export PATH=$PATH:$HOME/.dotnet:$HOME/.dotnet/tools

    # Correct build command for Mono
    dotnet build || exit 6;

    chmod -R 777 /opt/ServUO/
else
    echo "ServUO already built — skipping clone and build steps."
fi

echo ""
echo "================================================================================"
echo "Install complete"
echo "================================================================================"

exit 0
