ServUO arm docker container

            version: '3"
            services:
              servuo:
                image: aplayerv1/arm-serv
                restart: always
                port:
                  - 2593:2593
                environment:
                  - TZ=Europe/Paris
                  - ADMIN_NAME=admin
                  - ADMIN_PASSWORD=admin
                volumes:
                  - ./servuo:/opt/ServUO
                