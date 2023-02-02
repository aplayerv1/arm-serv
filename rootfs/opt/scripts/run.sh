#!/usr/bin/with-contenv bash

echo "================================================================================"
echo "Running ServUO"
echo "================================================================================"
echo ""
if [ ! -f /opt/data/fonts.mul ]; then
wget  https://mvia.ca/data.zip -P /tmp
cd /tmp
unzip data.zip -d /opt/data
else 
echo "data exists"
fi
echo "================================================================================"
echo "ALL DONE PANCAKES?"
echo "================================================================================"
echo ""
exit 0;

