#!/usr/bin/with-contenv bash

echo "================================================================================"
echo "Running install_serv.sh as $(whoami)".
echo "================================================================================"
#export DOTNET_ROOT=$HOME/dotnet-64 && export PATH=$HOME/dotnet-64:$PATH 
sudo apt-key adv --keyserver hkp://keyserver.ubuntu.com:80 --recv-keys 3FA7E0328081BFF6A14DA29AA6A19B38D3D831EF
echo "deb http://download.mono-project.com/repo/debian wheezy main" | sudo tee /etc/apt/sources.list.d/mono-xamarin.list
echo "deb http://download.mono-project.com/repo/debian wheezy-libtiff-compat main" | sudo tee -a /etc/apt/sources.list.d/mono-xamarin.list
sudo apt-get update
sudo apt-get install -y mono-complete
echo "================================================================================"
echo "CLONING"
echo "================================================================================"
git clone https://github.com/ServUO/ServUO.git ${_PATH_INSTALL_ROOT} || exit 5;
echo "================================================================================"
echo "BAKING"
echo "================================================================================"
cd ${_PATH_INSTALL_ROOT} 
export PATH=$PATH:$HOME/.dotnet:$HOME/.dotnet/tools
dotnet build || exit 6;


exit 0;
