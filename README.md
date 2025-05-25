# ServUO ARM Docker Container

[![GitHub Issues](https://img.shields.io/github/issues/aplayerv1/arm-serv)](https://github.com/aplayerv1/arm-serv/issues)
[![GitHub License](https://img.shields.io/github/license/aplayerv1/arm-serv)](https://github.com/aplayerv1/arm-serv/blob/main/LICENSE)

A Docker container for running ServUO (Ultima Online server emulator) on ARM architecture.

## 🚀 Features

- 🐳 Easy deployment with Docker Compose
- 🌍 Configurable timezone
- 👤 Admin account setup via environment variables
- 💾 Persistent data storage
- 🔄 Automatic restart capability
- 🔧 Production-ready configuration

## 📋 Prerequisites

- Docker Engine 20.10+
- Docker Compose 2.0+
- ARM-based system (Raspberry Pi 3+, ARM64 server, etc.)
- Minimum 1GB RAM recommended

## 🛠️ Quick Start

### 1. Clone the Repository

```bash
git clone https://github.com/aplayerv1/arm-serv.git
cd arm-serv
```

### 2. Create Required Directories

```bash
mkdir -p servuo data
```

### 3. Configure Environment

```bash
cp docker-compose.example.yml docker-compose.yml
```

Edit `docker-compose.yml` and change the default credentials:

```yaml
version: '3.8'
services:
  servuo:
    image: aplayerv1/arm-serv:latest
    container_name: servuo-server
    restart: unless-stopped
    network_mode: host
    environment:
      - TZ=Europe/Paris
      - ADMIN_NAME=youradmin      # ⚠️ CHANGE THIS!
      - ADMIN_PASSWORD=yourpassword  # ⚠️ CHANGE THIS!
    volumes:
      - ./servuo:/opt/ServUO
      - ./data:/opt/data
    healthcheck:
      test: ["CMD", "pgrep", "-f", "ServUO"]
      interval: 30s
      timeout: 10s
      retries: 3
```

### 4. Start the Server

```bash
docker-compose up -d
```

### 5. Verify Installation

```bash
docker-compose logs -f servuo
```

## ⚙️ Configuration

### Environment Variables

| Variable | Description | Default | Required |
|----------|-------------|---------|----------|
| `TZ` | Container timezone | `Europe/Paris` | No |
| `ADMIN_NAME` | Administrator username | `admin` | Yes |
| `ADMIN_PASSWORD` | Administrator password | `admin` | Yes |
| `SERVER_NAME` | Server display name | `ServUO ARM` | No |
| `MAX_PLAYERS` | Maximum concurrent players | `100` | No |

### Volume Mounts

| Host Path | Container Path | Description |
|-----------|----------------|-------------|
| `./servuo` | `/opt/ServUO` | ServUO server files and configuration |
| `./data` | `/opt/data` | Persistent game data (saves, accounts) |

### Network Ports

| Port | Protocol | Description |
|------|----------|-------------|
| `2593` | TCP | Game server port |
| `2594` | TCP | Admin console port |

## 📖 Usage

### Server Management

Start the server:
```bash
docker-compose up -d
```

Stop the server:
```bash
docker-compose down
```

Restart the server:
```bash
docker-compose restart
```

View real-time logs:
```bash
docker-compose logs -f servuo
```

### Server Console Access

```bash
docker-compose exec servuo /bin/bash
```

### Backup Data

```bash
tar -czf backup-$(date +%Y%m%d).tar.gz servuo/ data/
```

### Restore Data

```bash
tar -xzf backup-YYYYMMDD.tar.gz
```

## 🔧 Advanced Configuration

### Custom ServUO Configuration

1. Stop the container:
```bash
docker-compose down
```

2. Edit configuration files in `./servuo/` directory

3. Restart the container:
```bash
docker-compose up -d
```

### Performance Tuning

For better performance on ARM devices, you can adjust memory limits:

```yaml
services:
  servuo:
    # ... other configuration
    deploy:
      resources:
        limits:
          memory: 512M
        reservations:
          memory: 256M
```

## 🐛 Troubleshooting

### Common Issues

**Permission denied errors:**
```bash
sudo chown -R 1000:1000 servuo data
chmod -R 755 servuo data
```

**Port already in use:**
```bash
sudo netstat -tulpn | grep :2593
sudo lsof -i :2593
```

**Container won't start:**
```bash
docker-compose logs servuo
docker system prune -f
```

**Low memory issues:**
```bash
free -h
sudo dmesg | grep -i memory
```

### Health Checks

Check container health:
```bash
docker-compose ps
```

Monitor resource usage:
```bash
docker stats servuo-server
```

## 🏗️ Building from Source

### Clone and Build

```bash
git clone https://github.com/aplayerv1/arm-serv.git
cd arm-serv
```

```bash
docker build -t aplayerv1/arm-serv:local .
```

### Multi-architecture Build

```bash
docker buildx create --use
```

```bash
docker buildx build --platform linux/arm64,linux/arm/v7 -t aplayerv1/arm-serv:latest --push .
```

## 🔒 Security

### Essential Security Steps

1. **Change default credentials immediately**
2. **Use strong passwords (12+ characters)**
3. **Configure firewall rules:**

```bash
sudo ufw allow 2593/tcp
sudo ufw allow 2594/tcp
sudo ufw enable
```

4. **Regular updates:**

```bash
docker-compose pull
docker-compose up -d
```

### Recommended Security Configuration

```yaml
environment:
  - ADMIN_NAME=${ADMIN_NAME}
  - ADMIN_PASSWORD=${ADMIN_PASSWORD}
```

Create `.env` file:
```bash
echo "ADMIN_NAME=youradmin" > .env
echo "ADMIN_PASSWORD=$(openssl rand -base64 32)" >> .env
```

## 🤝 Contributing

We welcome contributions! Please see our [Contributing Guidelines](CONTRIBUTING.md).

### Development Setup

```bash
git clone https://github.com/aplayerv1/arm-serv.git
cd arm-serv
```


## 📝 Changelog

### [1.0.0] - 2024-01-XX
- Initial release
- Docker Compose configuration
- Health checks and monitoring
- Comprehensive documentation

## 🆘 Support

- 📋 [GitHub Issues](https://github.com/aplayerv1/arm-serv/issues)
- 📖 [ServUO Documentation](https://www.servuo.com/)
- 💬 [ServUO Forums](https://www.servuo.com/forums/)
- 🐳 [Docker Hub](https://hub.docker.com/r/aplayerv1/arm-serv)

## 📄 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## 🙏 Acknowledgments

- [ServUO Team](https://github.com/ServUO/ServUO) - Amazing server emulator
- [Ultima Online Community](https://uo.com/) - Continued support and development
- [Docker Community](https://docker.com/) - Containerization best practices

---

⭐ **Star this repository if it helped you!**

**Disclaimer**: This is an unofficial Docker container for ServUO. Please refer to official ServUO documentation for server configuration questions.
```

Create the example Docker Compose file:

```yaml:docker-compose.example.yml
version: '3.8'

services:
  servuo:
    image: aplayerv1/arm-serv:latest
    container_name: servuo-server
    restart: unless-stopped
    network_mode: host
    environment:
      - TZ=Europe/Paris
      - ADMIN_NAME=admin      # ⚠️ CHANGE THIS!
      - ADMIN_PASSWORD=admin  # ⚠️ CHANGE THIS!
      - SERVER_NAME=ServUO ARM Server
      - MAX_PLAYERS=100
    volumes:
      - ./servuo:/opt/ServUO
      - ./data:/opt/data
    healthcheck:
      test: ["CMD", "pgrep", "-f", "ServUO"]
      interval: 30s
      timeout: 10s
      retries: 3
      start_period: 60s
    logging:
      driver: "json-file"
      options:
        max-size: "10m"
        max-file: "3"
```

Create the LICENSE file:

```text:LICENSE
MIT License

Copyright (c) 2024 aplayerv1

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

Create the .gitignore file:

```gitignore:.gitignore
# Docker volumes and data
servuo/
data/
logs/

# Docker Compose files
docker-compose.yml
docker-compose.override.yml
docker-compose.override.yaml

# Environment files
.env
.env.local
.env.*.local
*.env

# Backup files
backup-*.tar.gz
*.backup
*.bak

# Logs
*.log
npm-debug.log*
yarn-debug.log*
yarn-error.log*

# Runtime data
pids
*.pid
*.seed
*.pid.lock

# OS generated files
.DS_Store
.DS_Store?
._*
.Spotlight-V100
.Trashes
ehthumbs.db
Thumbs.db
desktop.ini

# IDE and editor files
.vscode/
.idea/
*.swp
*.swo
*~
.project
.classpath
.settings/

# Temporary files
*.tmp
*.temp
.cache/
.temp/

# Docker
.dockerignore
```
## 🌐 Additional Features

### Telnet Console Access

The container includes telnet console support for remote server administration.

**Usage:**
```bash
telnet your-server-ip 6003:6003
```

### Web Server Map Interface

Built-in web-based map viewer for real-time server monitoring and player tracking.


**Access the Web Map:**
- URL: `http://your-server-ip:3344/map`
- Features:
  - Real-time player positions
  - Interactive world map


**Complete Configuration Example:**
```yaml
version: '3.8'
services:
  servuo:
    image: aplayerv1/arm-serv:latest
    container_name: servuo-server
    restart: unless-stopped
    environment:
      - TZ=Europe/Paris
      - ADMIN_NAME=youradmin
      - ADMIN_PASSWORD=yourpassword
    ports:
      - "2593:2593"  # Game server
      - "6003:6003"  # Telnet console
      - "3344:3344"  # Web map
      - "3345:3345"  # Web map (SSL)
    volumes:
      - ./servuo:/opt/ServUO
      - ./data:/opt/data
```


**Web Map Features:**
- 📍 Real-time player tracking
- 🗺️ Interactive zoom and pan

## ⚠️ Disclaimer

**🚧 WORK IN PROGRESS 🚧**

This project is currently under active development and should be considered **BETA SOFTWARE**. 

### Current Status:
- ✅ Core ServUO server deployment
- 🚧 Telnet console integration (testing phase)
- 🚧 Web map interface (development phase)
- 🚧 Advanced configuration options (planned)

### Important Notes:
- **Not recommended for production use** without thorough testing
- Features may change without notice
- Some documented features may not be fully implemented yet
- Breaking changes may occur between versions
- Limited testing on all ARM platforms

### What to Expect:
- 🐛 Potential bugs and stability issues
- 📝 Incomplete or changing documentation
- 🔄 Frequent updates and improvements
- 💬 Active development and community feedback integration

**Use at your own risk. Always backup your data before updates.**

For stable production deployments, please wait for the v1.0.0 release or use the official ServUO installation methods.

---
