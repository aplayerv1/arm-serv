using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Server;
using Server.Mobiles;
using Server.Items;
using Newtonsoft.Json;

namespace Server.Custom
{
    public static class Webserver
    {
        private static HttpListener _listener;
        private static List<WebSocket> _sockets = new List<WebSocket>();
        private static CancellationTokenSource _cts;
        private static int _serverPort = 0; // Add this to store the port

       public static void Initialize()
        {
            _cts = new CancellationTokenSource();
            _listener = new HttpListener();

            int basePort = 3344;
            int maxRetries = 10;
            bool started = false;

            for (int portOffset = 0; portOffset <= maxRetries; portOffset++)
            {
                int tryPort = basePort + portOffset;
                string prefix = $"http://10.10.1.230:{tryPort}/";

                try
                {
                    _listener.Prefixes.Clear();
                    _listener.Prefixes.Add(prefix);
                    _listener.Start();

                    _serverPort = tryPort; // Store the successful port
                    Console.WriteLine($"[Webserver] Started on port {tryPort}");
                    started = true;
                    break;
                }
                catch (HttpListenerException) // Remove unused variable
                {
                    Console.WriteLine($"[Webserver] Port {tryPort} is in use. Trying next port...");
                    // Dispose current listener and create a new one for next attempt
                    _listener.Close();
                    _listener = new HttpListener();
                }
            }

            if (!started)
            {
                throw new Exception("[Webserver] Failed to start HttpListener on any port from " +
                                    $"{basePort} to {basePort + maxRetries}");
            }

            Task.Run(() => ListenLoop());
            Task.Run(() => BroadcastPlayerPositionsLoop(_cts.Token));
        }


        public static void Dispose()
        {
            _cts.Cancel();
            _listener.Stop();
            lock (_sockets)
            {
                foreach (var ws in _sockets)
                    ws.Dispose();
                _sockets.Clear();
            }
        }

        private static async Task ListenLoop()
        {
            while (_listener.IsListening)
            {
                try
                {
                    var ctx = await _listener.GetContextAsync();

                    // Debug log for incoming requests
                    Console.WriteLine($"[Webserver] Incoming request: {ctx.Request.HttpMethod} {ctx.Request.RawUrl}");

                    if (ctx.Request.IsWebSocketRequest && ctx.Request.Url.AbsolutePath == "/players")
                    {
                        var wsContext = await ctx.AcceptWebSocketAsync(null);
                        var ws = wsContext.WebSocket;
                        lock (_sockets) { _sockets.Add(ws); }
                        Console.WriteLine("[Webserver] WebSocket client connected.");

                        _ = HandleWebSocket(ws);
                    }
                    else if (ctx.Request.Url.AbsolutePath.StartsWith("/map"))
                    {
                        var qs = ctx.Request.QueryString;
                        int x, y, width, height;
                        if (int.TryParse(qs["x"], out x) &&
                            int.TryParse(qs["y"], out y) &&
                            int.TryParse(qs["width"], out width) &&
                            int.TryParse(qs["height"], out height))
                        {
                            Bitmap bmp = null;
                            try
                            {
                                bmp = RenderMap(x, y, width, height);
                                ctx.Response.ContentType = "image/png";

                                using (var ms = new MemoryStream())
                                {
                                    bmp.Save(ms, ImageFormat.Png);
                                    ms.Position = 0;
                                    await ms.CopyToAsync(ctx.Response.OutputStream);
                                }
                            }
                            finally
                            {
                                if (bmp != null)
                                    bmp.Dispose();
                            }
                            ctx.Response.Close();
                        }
                        else
                        {
                            ctx.Response.StatusCode = 400;
                            using (var writer = new StreamWriter(ctx.Response.OutputStream))
                            {
                                writer.Write("Bad Request: missing or invalid parameters");
                            }
                            ctx.Response.Close();
                        }
                    }
                    else
                    {
                        if (ctx.Request.Url.AbsolutePath == "/" || ctx.Request.Url.AbsolutePath == "/index.html")
                        {
                            ctx.Response.ContentType = "text/html";
                            using (var writer = new StreamWriter(ctx.Response.OutputStream))
                            {
                                await writer.WriteAsync(GetHtmlPage(_serverPort)); // Pass the port here
                            }
                            ctx.Response.Close();
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[Webserver] Exception: " + ex.Message);
                }
            }
        }

        private static async Task HandleWebSocket(WebSocket ws)
        {
            var buffer = new byte[1024];
            try
            {
                while (ws.State == WebSocketState.Open)
                {
                    var segment = new ArraySegment<byte>(buffer);
                    var result = await ws.ReceiveAsync(segment, CancellationToken.None);

                    if (result.MessageType == WebSocketMessageType.Close || result.Count == 0)
                        break;
                }
            }
            catch { /* ignore */ }
            finally
            {
                lock (_sockets) { _sockets.Remove(ws); }
                if (ws.State == WebSocketState.Open || ws.State == WebSocketState.CloseReceived)
                {
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                }
                ws.Dispose();
                Console.WriteLine("[Webserver] WebSocket client disconnected.");
            }
        }

        private class PlayerData
        {
            public int Serial { get; set; }
            public string Name { get; set; }
            public int X { get; set; }
            public int Y { get; set; }
            public int Z { get; set; }
            public string Map { get; set; }
        }

        private static async Task BroadcastPlayerPositionsLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                var players = new List<PlayerData>();

                foreach (var mobile in World.Mobiles.Values)
                {
                    var player = mobile as PlayerMobile;
                    if (player != null && player.Map != null)
                    {
                        players.Add(new PlayerData
                        {
                            Serial = player.Serial.Value,
                            Name = player.Name,
                            X = player.Location.X,
                            Y = player.Location.Y,
                            Z = player.Location.Z,
                            Map = player.Map.ToString()
                        });
                    }
                }

                var json = JsonConvert.SerializeObject(players);
                var buffer = Encoding.UTF8.GetBytes(json);

                lock (_sockets)
                {
                    _sockets.RemoveAll(ws => ws.State != WebSocketState.Open);
                    foreach (var ws in _sockets)
                    {
                        try
                        {
                            var segment = new ArraySegment<byte>(buffer);
                            ws.SendAsync(segment, WebSocketMessageType.Text, true, CancellationToken.None).Wait();
                        }
                        catch { /* ignore individual send errors */ }
                    }
                }

                await Task.Delay(2000, token);
            }
        }

        private const int TilePixelSize = 24;

        private static Bitmap RenderMap(int startX, int startY, int width, int height)
        {
            Bitmap bmp = new Bitmap(width * TilePixelSize, height * TilePixelSize);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Black);

                Map map = Map.Felucca;

                for (int dx = 0; dx < width; dx++)
                {
                    for (int dy = 0; dy < height; dy++)
                    {
                        int mapX = startX + dx;
                        int mapY = startY + dy;

                        DrawTile(g, map, mapX, mapY, dx * TilePixelSize, dy * TilePixelSize);
                    }
                }

                foreach (var mobile in World.Mobiles.Values)
                {
                    var player = mobile as PlayerMobile;
                    if (player != null && player.Map == map)
                    {
                        int px = player.Location.X - startX;
                        int py = player.Location.Y - startY;

                        if (px >= 0 && px < width && py >= 0 && py < height)
                        {
                            Rectangle playerRect = new Rectangle(px * TilePixelSize, py * TilePixelSize, TilePixelSize, TilePixelSize);
                            using (Brush brush = new SolidBrush(Color.FromArgb(180, Color.Red)))
                            {
                                g.FillEllipse(brush, playerRect);
                            }
                        }
                    }
                }
            }

            return bmp;
        }

        private static void DrawTile(Graphics g, Map map, int x, int y, int screenX, int screenY)
        {
            var landTile = map.Tiles.GetLandTile(x, y);
            Color landColor = GetLandTileColor(landTile.ID);
            using (Brush brush = new SolidBrush(landColor))
            {
                g.FillRectangle(brush, screenX, screenY, TilePixelSize, TilePixelSize);
            }

            var statics = map.Tiles.GetStaticTiles(x, y);
            foreach (var stat in statics)
            {
                Color staticColor = GetStaticTileColor(stat.ID);
                using (Brush staticBrush = new SolidBrush(staticColor))
                {
                    int size = TilePixelSize / 2;
                    g.FillEllipse(staticBrush, screenX + TilePixelSize / 4, screenY + TilePixelSize / 4, size, size);
                }
            }
        }

        private static Color GetLandTileColor(int tileID)
        {
            // Water tiles
            if (tileID >= 0x00 && tileID <= 0x15)
                return Color.DarkBlue;
            // Sand/desert
            else if (tileID >= 0x16 && tileID <= 0x3E)
                return Color.SandyBrown;
            // Grass/plains
            else if (tileID >= 0x3F && tileID <= 0x6F)
                return Color.ForestGreen;
            // Mountains/rocks
            else if (tileID >= 0x70 && tileID <= 0x9F)
                return Color.Gray;
            // Snow
            else if (tileID >= 0xA0 && tileID <= 0xC5)
                return Color.White;
            // Default
            else
                return Color.DarkGreen;
        }

        private static Color GetStaticTileColor(int tileID)
        {
            // Trees and plants
            if ((tileID >= 0x0C8E && tileID <= 0x0CC7) || (tileID >= 0x0CE0 && tileID <= 0x0D29))
                return Color.FromArgb(0, 100, 0);
            // Buildings and structures
            else if ((tileID >= 0x0064 && tileID <= 0x0900))
                return Color.FromArgb(139, 69, 19);
            // Roads
            else if (tileID >= 0x071D && tileID <= 0x07A0)
                return Color.FromArgb(160, 160, 160);
            // Default
            else
                return Color.FromArgb(120, 70, 20);
        }

        private static string GetHtmlPage(int port)
        {
            return $@"
<!DOCTYPE html>
<html lang='en'>
<head>
<meta charset='UTF-8' />
<meta name='viewport' content='width=device-width, initial-scale=1' />
<title>ServUO Dynamic Map</title>
<style>
  body {{
    font-family: Arial, sans-serif;
    margin: 0;
    padding: 20px;
  }}
  #mapContainer {{
    position: relative;
    width: 800px;
    height: 600px;
    border: 1px solid black;
    overflow: hidden;
    margin-bottom: 10px;
  }}
  #mapImg {{
    image-rendering: pixelated;
    width: 100%;
    height: 100%;
  }}
  .player-marker {{
    position: absolute;
    width: 16px;
    height: 16px;
    background: rgba(255,0,0,0.7);
    border-radius: 50%;
    pointer-events: none;
    transform: translate(-50%, -50%);
  }}
  .controls {{
    margin-bottom: 15px;
  }}
  button {{
    padding: 5px 10px;
    margin-right: 5px;
  }}
  #coordinates {{
    margin-top: 10px;
  }}
</style>
</head>
<body>
<h1>ServUO Dynamic Map (Port {port})</h1>

<div class='controls'>
  <button id='zoomIn'>Zoom In</button>
  <button id='zoomOut'>Zoom Out</button>
  <button id='moveNorth'>North</button>
  <button id='moveSouth'>South</button>
  <button id='moveWest'>West</button>
  <button id='moveEast'>East</button>
  <button id='showBritain'>Britain</button>
  <button id='showMinoc'>Minoc</button>
  <button id='showTrinsic'>Trinsic</button>
  <button id='showFullMap'>Full Map</button>
</div>

<div id='mapContainer'>
  <img id='mapImg' src='/map?x=1000&y=1000&width=40&height=30' />
</div>

<div id='coordinates'>
  Viewing: X: <span id='viewX'>1000</span>, Y: <span id='viewY'>1000</span>, 
  Width: <span id='viewWidth'>40</span>, Height: <span id='viewHeight'>30</span>
</div>
<script>
  const mapContainer = document.getElementById('mapContainer');
  const mapImg = document.getElementById('mapImg');
  const viewX = document.getElementById('viewX');
  const viewY = document.getElementById('viewY');
  const viewWidth = document.getElementById('viewWidth');
  const viewHeight = document.getElementById('viewHeight');
  const tilePixelSize = 24;
  
  // Initial viewport settings
  let viewport = {{ x: 1000, y: 1000, width: 40, height: 30 }};
  
  // Map boundaries (Felucca)
  const mapBounds = {{ width: 6144, height: 4096 }};
  
  // City locations
  const locations = {{
    britain: {{ x: 1400, y: 1600, width: 40, height: 30 }},
    minoc: {{ x: 2500, y: 500, width: 40, height: 30 }},
    trinsic: {{ x: 1900, y: 2800, width: 40, height: 30 }},
    fullMap: {{ x: 0, y: 0, width: 200, height: 150 }}
  }};
  
  // Update the map with current viewport
  function updateMap() {{
    // Ensure viewport stays within map bounds
    viewport.x = Math.max(0, Math.min(mapBounds.width - viewport.width, viewport.x));
    viewport.y = Math.max(0, Math.min(mapBounds.height - viewport.height, viewport.y));
    
    // Update the map image
    mapImg.src = `/map?x=${{viewport.x}}&y=${{viewport.y}}&width=${{viewport.width}}&height=${{viewport.height}}`;
    
    // Update coordinate display
    viewX.textContent = viewport.x;
    viewY.textContent = viewport.y;
    viewWidth.textContent = viewport.width;
    viewHeight.textContent = viewport.height;
    
    // Clear existing player markers
    document.querySelectorAll('.player-marker').forEach(e => e.remove());
  }}
  
  // Navigation controls
  document.getElementById('zoomIn').addEventListener('click', () => {{
    if (viewport.width > 10 && viewport.height > 10) {{
      const centerX = viewport.x + viewport.width / 2;
      const centerY = viewport.y + viewport.height / 2;
      viewport.width = Math.floor(viewport.width * 0.7);
      viewport.height = Math.floor(viewport.height * 0.7);
      viewport.x = Math.floor(centerX - viewport.width / 2);
      viewport.y = Math.floor(centerY - viewport.height / 2);
      updateMap();
    }}
  }});
  
  document.getElementById('zoomOut').addEventListener('click', () => {{
    if (viewport.width < 200 && viewport.height < 150) {{
      const centerX = viewport.x + viewport.width / 2;
      const centerY = viewport.y + viewport.height / 2;
      viewport.width = Math.floor(viewport.width * 1.5);
      viewport.height = Math.floor(viewport.height * 1.5);
      viewport.x = Math.floor(centerX - viewport.width / 2);
      viewport.y = Math.floor(centerY - viewport.height / 2);
      updateMap();
    }}
  }});
  
  document.getElementById('moveNorth').addEventListener('click', () => {{
    viewport.y = Math.max(0, viewport.y - Math.floor(viewport.height / 2));
    updateMap();
  }});
  
  document.getElementById('moveSouth').addEventListener('click', () => {{
    viewport.y = Math.min(mapBounds.height - viewport.height, viewport.y + Math.floor(viewport.height / 2));
    updateMap();
  }});
  
  document.getElementById('moveWest').addEventListener('click', () => {{
    viewport.x = Math.max(0, viewport.x - Math.floor(viewport.width / 2));
    updateMap();
  }});
  
  document.getElementById('moveEast').addEventListener('click', () => {{
    viewport.x = Math.min(mapBounds.width - viewport.width, viewport.x + Math.floor(viewport.width / 2));
    updateMap();
  }});
  
  // City shortcuts
  document.getElementById('showBritain').addEventListener('click', () => {{
    viewport = {{ ...locations.britain }};
    updateMap();
  }});
  
  document.getElementById('showMinoc').addEventListener('click', () => {{
    viewport = {{ ...locations.minoc }};
    updateMap();
  }});
  
  document.getElementById('showTrinsic').addEventListener('click', () => {{
    viewport = {{ ...locations.trinsic }};
    updateMap();
  }});
  
  document.getElementById('showFullMap').addEventListener('click', () => {{
    viewport = {{ ...locations.fullMap }};
    updateMap();
  }});
  
  // WebSocket connection for player positions
  const ws = new WebSocket('ws://' + window.location.host + '/players');
  ws.onmessage = function(event) {{
    const players = JSON.parse(event.data);
    document.querySelectorAll('.player-marker').forEach(e => e.remove());

    players.forEach(p => {{
      if (p.Map === 'Felucca' &&
          p.X >= viewport.x && p.X < viewport.x + viewport.width &&
          p.Y >= viewport.y && p.Y < viewport.y + viewport.height) {{

        const containerWidth = mapContainer.clientWidth;
        const containerHeight = mapContainer.clientHeight;
        const tileWidth = containerWidth / viewport.width;
        const tileHeight = containerHeight / viewport.height;
        
        const px = ((p.X - viewport.x) / viewport.width) * containerWidth;
        const py = ((p.Y - viewport.y) / viewport.height) * containerHeight;

        const marker = document.createElement('div');
        marker.className = 'player-marker';
        marker.style.left = px + 'px';
        marker.style.top = py + 'px';
        marker.title = p.Name;
        mapContainer.appendChild(marker);
      }}
    }});
  }};
  
  // Allow dragging the map
  let isDragging = false;
  let dragStartX, dragStartY;
  let viewportStartX, viewportStartY;
  
  mapContainer.addEventListener('mousedown', (e) => {{
    isDragging = true;
    dragStartX = e.clientX;
    dragStartY = e.clientY;
    viewportStartX = viewport.x;
    viewportStartY = viewport.y;
    mapContainer.style.cursor = 'grabbing';
  }});
  
  window.addEventListener('mousemove', (e) => {{
    if (isDragging) {{
      const dx = e.clientX - dragStartX;
      const dy = e.clientY - dragStartY;
      
      const tileWidth = mapContainer.clientWidth / viewport.width;
      const tileHeight = mapContainer.clientHeight / viewport.height;
      
      const tilesX = Math.floor(dx / tileWidth);
      const tilesY = Math.floor(dy / tileHeight);
      
      viewport.x = Math.max(0, Math.min(mapBounds.width - viewport.width, viewportStartX - tilesX));
      viewport.y = Math.max(0, Math.min(mapBounds.height - viewport.height, viewportStartY - tilesY));
      
      updateMap();
    }}
  }});
  
  window.addEventListener('mouseup', () => {{
    isDragging = false;
    mapContainer.style.cursor = 'grab';
  }});
  
  // Initialize
  mapContainer.style.cursor = 'grab';
</script>
</body>
</html>
";
        }
    }
}
