#!/usr/bin/with-contenv bash

echo "================================================================================"
echo "Running install_serv.sh as $(whoami)".
echo "================================================================================"
apt-get update 
apt-get install -y mono-devel 
echo ""
echo "================================================================================"
echo "ServUO git check"
echo "================================================================================"
echo ""
if [ ! -d "${_PATH_INSTALL_ROOT}" ] ; then
    git clone https://github.com/ServUO/ServUO.git ${_PATH_INSTALL_ROOT}
else
    cd "${_PATH_INSTALL_ROOT}"
    git pull https://github.com/ServUO/ServUO.git
fi
echo ""
echo "================================================================================"
echo "BAKING"
echo "================================================================================"
echo ""
cd ${_PATH_INSTALL_ROOT} 
export DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
export PATH=$PATH:$HOME/.dotnet:$HOME/.dotnet/tools
echo ""
echo "================================================================================"
echo "ORANGE"
echo "================================================================================"
dotnet build || exit 6;
exit 0;
