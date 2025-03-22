#!/usr/bin/with-contenv bash

FILE=/opt/ServUO/ServUO.exe

echo "================================================================================"
echo "Running install_serv.sh as $(whoami)".
echo "================================================================================"
echo ""
echo "================================================================================"
echo "ServUO git check"
echo "================================================================================"
echo ""
echo "tmp"
cd /root/

# Set up environment variables for .NET SDK
export HOME=/root
export DOTNET_CLI_HOME=/root
export DOTNET_ROOT=/root/.dotnet
export DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
export PATH=$PATH:/root/.dotnet:/root/.dotnet/tools

# Save environment variables to S6 container_environment
mkdir -p /var/run/s6/container_environment
echo "/root" > /var/run/s6/container_environment/HOME
echo "/root" > /var/run/s6/container_environment/DOTNET_CLI_HOME
echo "/root/.dotnet" > /var/run/s6/container_environment/DOTNET_ROOT
echo "1" > /var/run/s6/container_environment/DOTNET_SYSTEM_GLOBALIZATION_INVARIANT
echo "$PATH" > /var/run/s6/container_environment/PATH

# Verify environment variables
echo "Environment variables set:"
echo "HOME=$HOME"
echo "DOTNET_CLI_HOME=$DOTNET_CLI_HOME"
echo "DOTNET_ROOT=$DOTNET_ROOT"
echo "PATH=$PATH"
echo "DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=$DOTNET_SYSTEM_GLOBALIZATION_INVARIANT"

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
    ln -s /root/.dotnet/dotnet /usr/local/bin/dotnet
    export DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
    export PATH=$PATH:$HOME/.dotnet:$HOME/.dotnet/tools
    echo ""
    echo "================================================================================"
    echo "ORANGE"
    echo "================================================================================"
    dotnet build || exit 6;
fi

echo "================================================================================"
echo "Setting up ServUO service for S6 Overlay..."

# Check if mono exists and is executable
if ! command -v mono &> /dev/null; then
    echo "WARNING: mono command not found in PATH"
    # Try to find mono
    MONO_PATH=$(find / -name mono -type f -executable 2>/dev/null | head -1)
    if [ -n "$MONO_PATH" ]; then
        echo "Found mono at: $MONO_PATH"
    else
        echo "ERROR: Cannot find mono executable. ServUO will not run!"
        exit 1
    fi
else
    MONO_PATH=$(which mono)
    echo "Found mono at: $MONO_PATH"
fi

# Check if ServUO.exe exists
if [ ! -f "/opt/ServUO/ServUO.exe" ]; then
    echo "ERROR: ServUO.exe not found in /opt/ServUO"
    # List contents of ServUO directory
    ls -la /opt/ServUO/
    exit 1
else
    echo "ServUO.exe found at /opt/ServUO/ServUO.exe"
fi

# Method 1: Traditional Service Directory (Legacy Method)
echo "Creating traditional S6 service..."
mkdir -p /etc/services.d/servuo

cat > /etc/services.d/servuo/run << EOF
#!/usr/bin/with-contenv bash
cd /opt/ServUO || exit 1
exec ${MONO_PATH} ServUO.exe -noconsole
EOF

chmod 755 /etc/services.d/servuo/run

echo "Created traditional S6 service in /etc/services.d/servuo"
ls -la /etc/services.d/servuo/run

# Method 2: S6-RC Source Definition (Recommended New Method)
echo "Creating S6-RC service definition..."
mkdir -p /etc/s6-overlay/s6-rc.d/servuo

echo "longrun" > /etc/s6-overlay/s6-rc.d/servuo/type

cat > /etc/s6-overlay/s6-rc.d/servuo/run << 'EOF'
#!/command/execlineb -P
# Import environment variables
with-contenv
# Change to ServUO directory
cd /opt/ServUO
# Run ServUO
/usr/bin/mono ServUO.exe -noconsole
EOF

chmod 755 /etc/s6-overlay/s6-rc.d/servuo/run

mkdir -p /etc/s6-overlay/s6-rc.d/servuo/dependencies.d
touch /etc/s6-overlay/s6-rc.d/servuo/dependencies.d/base

mkdir -p /etc/s6-overlay/s6-rc.d/user/contents.d
touch /etc/s6-overlay/s6-rc.d/user/contents.d/servuo

echo "Created S6-RC service definition in /etc/s6-overlay/s6-rc.d/servuo"
ls -la /etc/s6-overlay/s6-rc.d/servuo/

# Create a bash version as fallback
echo "Creating bash fallback version..."
cat > /etc/services.d/servuo/run.bash << 'EOF'
#!/usr/bin/with-contenv bash
cd /opt/ServUO || exit 1
exec /usr/bin/mono ServUO.exe -noconsole
EOF

chmod 755 /etc/services.d/servuo/run.bash

# Create init script to ensure environment variables are set at container startup
mkdir -p /etc/cont-init.d
cat > /etc/cont-init.d/00-set-dotnet-env << 'EOF'
#!/usr/bin/with-contenv bash

# Ensure HOME is set
if [ -z "$HOME" ]; then
  export HOME=/root
  echo "Setting HOME to /root"
  echo "/root" > /var/run/s6/container_environment/HOME
fi

# Ensure DOTNET_CLI_HOME is set
if [ -z "$DOTNET_CLI_HOME" ]; then
  export DOTNET_CLI_HOME=/root
  echo "Setting DOTNET_CLI_HOME to /root"
  echo "/root" > /var/run/s6/container_environment/DOTNET_CLI_HOME
fi

# Set DOTNET_ROOT
export DOTNET_ROOT=/root/.dotnet
echo "Setting DOTNET_ROOT to /root/.dotnet"
echo "/root/.dotnet" > /var/run/s6/container_environment/DOTNET_ROOT

# Set PATH to include .NET CLI
export PATH=$PATH:/root/.dotnet:/root/.dotnet/tools
echo "Adding .NET CLI to PATH"
echo "$PATH" > /var/run/s6/container_environment/PATH

# Set DOTNET_SYSTEM_GLOBALIZATION_INVARIANT
export DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
echo "Setting DOTNET_SYSTEM_GLOBALIZATION_INVARIANT to 1"
echo "1" > /var/run/s6/container_environment/DOTNET_SYSTEM_GLOBALIZATION_INVARIANT

# Verify environment variables
echo "Environment variables:"
echo "HOME=$HOME"
echo "DOTNET_CLI_HOME=$DOTNET_CLI_HOME"
echo "DOTNET_ROOT=$DOTNET_ROOT"
echo "PATH=$PATH"
echo "DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=$DOTNET_SYSTEM_GLOBALIZATION_INVARIANT"
EOF

chmod 755 /etc/cont-init.d/00-set-dotnet-env

echo "ServUO service setup complete!"
echo "To restart S6 services, run: s6-svscanctl -r /var/run/s6/services"

exit 0;
