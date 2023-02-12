ServUO arm docker container

<<<<<<< HEAD
            version: '3'
            services:
              servuo:
              image: aplayerv1/arm-serv
              restart: always
              network_mode: host
              environment:
                - TZ=Europe/Paris
                - ADMIN_NAME=admin
                - ADMIN_PASSWORD=admin
              volumes:
                - ./servuo:/opt/ServUO
                - ./data:/opt/data
                
=======
version: '3'
services:
  servuo:
   image: aplayerv1/arm-serv
   restart: always
   network_mode: host
   environment:
    - TZ=Europe/Paris
    - ADMIN_NAME=admin
    - ADMIN_PASSWORD=admin
   volumes:
    - ./servuo:/opt/ServUO
    - ./data:/opt/data
                
>>>>>>> 2d267f02a08d3d0fd9e476b88ad651d400281ed7
