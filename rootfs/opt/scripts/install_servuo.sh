#!/usr/bin/with-contenv bash
FILE=/opt/ServUO/ServUO.exe

echo "================================================================================"
echo "Running install_serv.sh as $(whoami)".
echo "================================================================================"
apt-get update 
apt-get install -y mono-devel rsync nano
echo ""
echo "================================================================================"
echo "ServUO git check"
echo "================================================================================"
echo ""
echo "tmp"
cd /root/
if [ ! -f /opt/ServUO/ServUO.exe ]; then
    git clone https://github.com/ServUO/ServUO.git
    rsync -av /root/ServUO /opt
    rm -r /root/ServUO
    echo "tmp done"
    echo ""
    echo "================================================================================"
    echo "BAKING"
    echo "================================================================================"
    echo ""
    cd /opt/ServUO
    export DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
    export PATH=$PATH:$HOME/.dotnet:$HOME/.dotnet/tools
    echo ""
    echo "================================================================================"
    echo "ORANGE"
    echo "================================================================================"
    dotnet build || exit 6;
fi
exit 0;
