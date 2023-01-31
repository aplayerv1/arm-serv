#!/usr/bin/with-contenv bash

echo "================================================================================"
echo "Running ServUO"
echo "================================================================================"
echo ""
export DOTNET_ROOT=$HOME/dotnet-64 && export PATH=$HOME/dotnet-64:$PATH 
cd $_DATA_PATH

wget https://mvia.ca/data.zip
unzip data.zip -d /opt/data
rm -r data.zip

cd $_PATH_INSTALL_ROOT

 mono ServUO.exe << 'EOF'
 y
 admin
 admin
 EOF

