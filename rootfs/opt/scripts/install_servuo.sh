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

    echo "================================================================================"
    echo "Copying custom TelnetConsole scripts"
    echo "================================================================================"
    mkdir -p /opt/ServUO/Scripts/Custom/TelnetConsole
    cp /opt/scripts/TelnetConsole/*.cs /opt/ServUO/Scripts/Custom/TelnetConsole/

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

    # dotnet build
    mcs -sdk:4.5 -out:Ultima.dll -optimize+ -unsafe -target:library -r:System,System.Configuration.Install,System.Data,System.Drawing,System.EnterpriseServices,System.Management,System.Security,System.ServiceProcess,System.Web,System.Web.Services,System.Windows.Forms,System.Xml -nowarn:219,414,618 -recurse:Ultima/*.cs
    csc -out:ServUO.exe -optimize+ -unsafe -target:exe -recurse:Server/*.cs -r:Ultima.dll,Scripts.dll
    chmod -R 777 /opt/ServUO/

else
    echo "ServUO already built — skipping clone and build steps."
fi

echo ""
echo "================================================================================"
echo "Install complete"
echo "================================================================================"

exit 0
