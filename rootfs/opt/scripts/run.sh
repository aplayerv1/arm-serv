#!/usr/bin/with-contenv bash

echo "================================================================================"
echo "Running ServUO Data Setup"
echo "================================================================================"
echo ""

# Check if fonts.mul exists in the correct location
if [ ! -f /opt/data/fonts.mul ]; then
    echo "Downloading data files..."
    wget https://other.mvia.ca/data.zip -P /tmp
    
    echo "Extracting data files..."
    cd /tmp
    
    # Create the target directory
    mkdir -p /opt/data
    
    # Extract the zip file
    unzip -o data.zip -d /tmp
    
    # Move files from /tmp/data to /opt/data
    if [ -d /tmp/data ]; then
        mv /tmp/data/* /opt/data/
        echo "Data files moved to /opt/data/"
    else
        echo "Warning: Expected /tmp/data directory not found after extraction"
        # Try direct extraction
        unzip -o data.zip -d /opt/data
    fi
    
    # Clean up
    rm -f /tmp/data.zip
    rm -rf /tmp/data
    
    # Verify files were extracted
    if [ -f /opt/data/fonts.mul ]; then
        echo "Data files successfully installed"
    else
        echo "Warning: fonts.mul still not found after extraction"
    fi
fi

echo "================================================================================"
echo "ALL DONE PANCAKES!"
echo "================================================================================"
echo ""

exit 0
