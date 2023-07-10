ServUO arm docker container

            version: '3'
            services:
              servuo:
              image: aplayerv1/arm-serv
              restart: always
              network_mode: host
              environment:
                - TZ=Europe/Paris
                - ADMIN_NAME=admin      #change me
                - ADMIN_PASSWORD=admin  #change me 
              volumes:
                - ./servuo:/opt/ServUO
                - ./data:/opt/data
                

