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

// Add these for MUL file access
using Server.Multis;
using Server.Network;

namespace Server.Custom
{
    public static class Webserver
    {
        private static HttpListener _listener;
        private static List<WebSocket> _sockets = new List<WebSocket>();
        private static CancellationTokenSource _cts;
        private static int _serverPort = 0;
        private static Dictionary<int, Dictionary<string, Bitmap>> _mapCache = new Dictionary<int, Dictionary<string, Bitmap>>();
        private static readonly object _cacheLock = new object();

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

                    _serverPort = tryPort;
                    Console.WriteLine($"[Webserver] Started on port {tryPort}");
                    started = true;
                    break;
                }
                catch (HttpListenerException)
                {
                    Console.WriteLine($"[Webserver] Port {tryPort} is in use. Trying next port...");
                    _listener.Close();
                    _listener = new HttpListener();
                }
            }

            if (!started)
            {
                throw new Exception("[Webserver] Failed to start HttpListener on any port from " +
                                    $"{basePort} to {basePort + maxRetries}");
            }

            // Initialize map cache
            for (int i = 0; i < Map.AllMaps.Count; i++)
            {
                _mapCache[i] = new Dictionary<string, Bitmap>();
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
            
            // Dispose map cache
            lock (_cacheLock)
            {
                foreach (var mapDict in _mapCache.Values)
                {
                    foreach (var bitmap in mapDict.Values)
                    {
                        bitmap.Dispose();
                    }
                    mapDict.Clear();
                }
                _mapCache.Clear();
            }
        }

        private static async Task ListenLoop()
        {
            while (_listener.IsListening)
            {
                try
                {
                    var ctx = await _listener.GetContextAsync();

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
                        await HandleMapRequest(ctx);
                    }
                    else if (ctx.Request.Url.AbsolutePath.StartsWith("/assets/"))
                    {
                        await HandleAssetRequest(ctx);
                    }
                    else
                    {
                        if (ctx.Request.Url.AbsolutePath == "/" || ctx.Request.Url.AbsolutePath == "/index.html")
                        {
                            ctx.Response.ContentType = "text/html";
                            using (var writer = new StreamWriter(ctx.Response.OutputStream))
                            {
                                await writer.WriteAsync(GetHtmlPage(_serverPort));
                            }
                            ctx.Response.Close();
                        }
                        else
                        {
                            ctx.Response.StatusCode = 404;
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

        private static async Task HandleMapRequest(HttpListenerContext ctx)
        {
            var qs = ctx.Request.QueryString;
            int x, y, width, height, mapIndex = 0;
            
            // Parse map index if provided
            if (qs["map"] != null)
            {
                int.TryParse(qs["map"], out mapIndex);
            }
            
            // Ensure valid map index
            if (mapIndex < 0 || mapIndex >= Map.AllMaps.Count)
            {
                mapIndex = 0; // Default to Felucca
            }
            
            if (int.TryParse(qs["x"], out x) &&
                int.TryParse(qs["y"], out y) &&
                int.TryParse(qs["width"], out width) &&
                int.TryParse(qs["height"], out height))
            {
                // Limit size to reasonable values
                width = Math.Min(width, 100);
                height = Math.Min(height, 75);
                
                Bitmap mapSection = null;
                try
                {
                    ctx.Response.ContentType = "image/png";
                    
                    // Get the map section using MUL files
                    mapSection = GetMapSectionFromMul(mapIndex, x, y, width, height);
                    
                    using (var ms = new MemoryStream())
                    {
                        mapSection.Save(ms, ImageFormat.Png);
                        ms.Position = 0;
                        await ms.CopyToAsync(ctx.Response.OutputStream);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Webserver] Error rendering map: {ex.Message}");
                    if (!ctx.Response.OutputStream.CanWrite)
                    {
                        // Can't write error, response already started
                    }
                    else
                    {
                        ctx.Response.StatusCode = 500;
                        using (var writer = new StreamWriter(ctx.Response.OutputStream))
                        {
                            writer.Write("Server error rendering map");
                        }
                    }
                }
                finally
                {
                    // Only dispose if it's not from cache
                    string cacheKey = $"{x}_{y}_{width}_{height}";
                    bool isFromCache = false;
                    
                    lock (_cacheLock)
                    {
                        isFromCache = _mapCache[mapIndex].ContainsKey(cacheKey) && 
                                     _mapCache[mapIndex][cacheKey] == mapSection;
                    }
                    
                    if (mapSection != null && !isFromCache)
                    {
                        mapSection.Dispose();
                    }
                    
                    ctx.Response.Close();
                }
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

        private static async Task HandleAssetRequest(HttpListenerContext ctx)
        {
            string assetPath = ctx.Request.Url.AbsolutePath.Substring("/assets/".Length);
            string fullPath = Path.Combine(Core.BaseDirectory, "Data", "WebAssets", assetPath);
            
            if (File.Exists(fullPath))
            {
                string contentType = "application/octet-stream";
                string ext = Path.GetExtension(fullPath).ToLower();
                
                switch (ext)
                {
                    case ".png": contentType = "image/png"; break;
                    case ".jpg": case ".jpeg": contentType = "image/jpeg"; break;
                    case ".gif": contentType = "image/gif"; break;
                    case ".css": contentType = "text/css"; break;
                    case ".js": contentType = "application/javascript"; break;
                    case ".html": contentType = "text/html"; break;
                }
                
                ctx.Response.ContentType = contentType;
                
                try
                {
                    using (var fileStream = new FileStream(fullPath, FileMode.Open, FileAccess.Read))
                    {
                        await fileStream.CopyToAsync(ctx.Response.OutputStream);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Webserver] Error serving asset {fullPath}: {ex.Message}");
                    ctx.Response.StatusCode = 500;
                }
                
                ctx.Response.Close();
            }
            else
            {
                ctx.Response.StatusCode = 404;
                ctx.Response.Close();
            }
        }

        private static Bitmap GetMapSectionFromMul(int mapIndex, int startX, int startY, int width, int height)
        {
            // Check cache first
            string cacheKey = $"{startX}_{startY}_{width}_{height}";
            
            lock (_cacheLock)
            {
                if (_mapCache[mapIndex].ContainsKey(cacheKey))
                {
                    return _mapCache[mapIndex][cacheKey];
                }
            }
            
            Map map = Map.AllMaps[mapIndex];
            
            // Create a new bitmap for the map section
            Bitmap bmp = new Bitmap(width * 8, height * 8); // Use 8x8 pixels per tile for better detail
            
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Black);
                
                // Render map tiles from MUL files
                for (int x = 0; x < width; x++)
                {
                    for (int y = 0; y < height; y++)
                    {
                        int mapX = startX + x;
                        int mapY = startY + y;
                        
                        // Skip if out of bounds
                        if (mapX < 0 || mapY < 0 || mapX >= map.Width || mapY >= map.Height)
                            continue;
                        
                        // Get land tile
                        var landTile = map.Tiles.GetLandTile(mapX, mapY);
                        
                        // Get the actual land tile color from UO data
                        Color landColor = GetLandTileColorFromMul(landTile.ID);
                        
                        // Draw land tile
                        using (Brush brush = new SolidBrush(landColor))
                        {
                            g.FillRectangle(brush, x * 8, y * 8, 8, 8);
                        }
                        
                        // Get static tiles
                        var statics = map.Tiles.GetStaticTiles(mapX, mapY);
                        
                        // Draw the highest static (most visible)
                        if (statics.Length > 0)
                        {
                            // Sort by Z to get the highest
                            int highestZ = -1;
                            int highestID = 0;
                            
                            foreach (var stat in statics)
                            {
                                if (stat.Z >= highestZ)
                                {
                                    highestZ = stat.Z;
                                    highestID = stat.ID;
                                }
                            }
                            
                            // Get the actual static tile color from UO data
                            Color staticColor = GetStaticTileColorFromMul(highestID);
                            
                            // Draw static tile (smaller to show it's on top of land)
                            using (Brush staticBrush = new SolidBrush(staticColor))
                            {
                                g.FillRectangle(staticBrush, x * 8 + 1, y * 8 + 1, 6, 6);
                            }
                        }
                    }
                }
                
                // Draw players on the map
                foreach (var mobile in World.Mobiles.Values)
                {
                    var player = mobile as PlayerMobile;
                    if (player != null && player.Map == map)
                    {
                        int px = player.Location.X - startX;
                        int py = player.Location.Y - startY;
                        
                        if (px >= 0 && px < width && py >= 0 && py < height)
                        {
                            using (Brush brush = new SolidBrush(Color.FromArgb(220, Color.Red)))
                            {
                                g.FillEllipse(brush, px * 8, py * 8, 8, 8);
                            }
                            
                            // Draw player name if zoomed in enough
                            if (width <= 40)
                            {
                                using (Font font = new Font("Arial", 7))
                                using (Brush textBrush = new SolidBrush(Color.White))
                                using (Brush shadowBrush = new SolidBrush(Color.Black))
                                {
                                    // Draw shadow
                                    g.DrawString(player.Name, font, shadowBrush, px * 8 + 9, py * 8 + 1);
                                    // Draw text
                                    g.DrawString(player.Name, font, textBrush, px * 8 + 8, py * 8);
                                }
                            }
                        }
                    }
                }
            }
            
            // Cache the result
            lock (_cacheLock)
            {
                // Limit cache size to prevent memory issues
                if (_mapCache[mapIndex].Count > 100)
                {
                    // Remove a random entry
                    var keyToRemove = _mapCache[mapIndex].Keys.GetEnumerator();
                    keyToRemove.MoveNext();
                    string removeKey = keyToRemove.Current;
                    
                    _mapCache[mapIndex][removeKey].Dispose();
                    _mapCache[mapIndex].Remove(removeKey);
                }
                
                _mapCache[mapIndex][cacheKey] = bmp;
            }
            
            return bmp;
        }

        // Get land tile color from MUL files
        private static Color GetLandTileColorFromMul(int tileID)
        {
            try
            {
                // Try to get actual color from UO data if available
                var landData = TileData.LandTable[tileID];
                
                // Use the tile name to determine a better color
                // Use the tile name to determine a better color
                string tileName = landData.Name.ToLower();
                
                // Water tiles
                if (tileName.Contains("water") || (tileID >= 0x00 && tileID <= 0x15))
                    return Color.FromArgb(0, 92, 148);
                
                // Sand/desert
                else if (tileName.Contains("sand") || tileName.Contains("desert") || 
                         (tileID >= 0x16 && tileID <= 0x3E))
                    return Color.FromArgb(210, 190, 149);
                
                // Grass/plains
                else if (tileName.Contains("grass") || tileName.Contains("plain") || 
                         (tileID >= 0x3F && tileID <= 0x6F))
                    return Color.FromArgb(86, 153, 86);
                
                // Mountains/rocks
                else if (tileName.Contains("mountain") || tileName.Contains("rock") || 
                         (tileID >= 0x70 && tileID <= 0x9F))
                    return Color.FromArgb(144, 144, 144);
                
                // Snow
                else if (tileName.Contains("snow") || (tileID >= 0xA0 && tileID <= 0xC5))
                    return Color.FromArgb(224, 224, 224);
                
                // Swamp
                else if (tileName.Contains("swamp"))
                    return Color.FromArgb(96, 116, 77);
                
                // Lava
                else if (tileName.Contains("lava"))
                    return Color.FromArgb(200, 90, 40);
                
                // Default - use hue from tile data
                return Color.FromArgb(
                    landData.Hue & 0xFF, 
                    (landData.Hue >> 8) & 0xFF, 
                    (landData.Hue >> 16) & 0xFF);
            }
            catch
            {
                // Fallback colors if we can't access the MUL data
                if (tileID >= 0x00 && tileID <= 0x15)
                    return Color.DarkBlue;
                else if (tileID >= 0x16 && tileID <= 0x3E)
                    return Color.SandyBrown;
                else if (tileID >= 0x3F && tileID <= 0x6F)
                    return Color.ForestGreen;
                else if (tileID >= 0x70 && tileID <= 0x9F)
                    return Color.Gray;
                else if (tileID >= 0xA0 && tileID <= 0xC5)
                    return Color.White;
                else
                    return Color.DarkGreen;
            }
        }

        // Get static tile color from MUL files
        private static Color GetStaticTileColorFromMul(int tileID)
        {
            try
            {
                // Try to get actual color from UO data if available
                var staticData = TileData.ItemTable[tileID];
                
                // Use the tile name to determine a better color
                string tileName = staticData.Name.ToLower();
                
                // Trees and plants
                if (tileName.Contains("tree") || tileName.Contains("plant") || 
                    tileName.Contains("bush") || tileName.Contains("foliage"))
                    return Color.FromArgb(0, 100, 0);
                
                // Buildings and structures
                else if (tileName.Contains("wall") || tileName.Contains("door") || 
                         tileName.Contains("roof") || tileName.Contains("floor"))
                    return Color.FromArgb(139, 69, 19);
                
                // Roads
                else if (tileName.Contains("road") || tileName.Contains("path"))
                    return Color.FromArgb(160, 160, 160);
                
                // Water features
                else if (tileName.Contains("water") || tileName.Contains("sea"))
                    return Color.FromArgb(0, 92, 148);
                
                // Default - use hue from tile data
                return Color.FromArgb(
                    staticData.Hue & 0xFF, 
                    (staticData.Hue >> 8) & 0xFF, 
                    (staticData.Hue >> 16) & 0xFF);
            }
            catch
            {
                // Fallback colors if we can't access the MUL data
                if ((tileID >= 0x0C8E && tileID <= 0x0CC7) || (tileID >= 0x0CE0 && tileID <= 0x0D29))
                    return Color.FromArgb(0, 100, 0);
                else if ((tileID >= 0x0064 && tileID <= 0x0900))
                    return Color.FromArgb(139, 69, 19);
                else if (tileID >= 0x071D && tileID <= 0x07A0)
                    return Color.FromArgb(160, 160, 160);
                else
                    return Color.FromArgb(120, 70, 20);
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

        // Advanced method to render map using actual UO art assets
        private static Bitmap RenderMapWithUOArt(int mapIndex, int startX, int startY, int width, int height)
        {
            // This would be a more advanced implementation that uses actual UO art assets
            // from the client files to render a more accurate representation of the map.
            // It would require access to the art.mul, texmaps.mul, etc. files.
            
            // For now, we'll use our simpler implementation
            return GetMapSectionFromMul(mapIndex, startX, startY, width, height);
        }
        private static string GetHtmlPage(int port)
        {
             return $@"
<!DOCTYPE html>
<html lang='en'>
<head>
<meta charset='UTF-8' />
<meta name='viewport' content='width=device-width, initial-scale=1' />
<title>ServUO Interactive Map</title>
<style>
  body {{
    font-family: Arial, sans-serif;
    margin: 0;
    padding: 0;
    overflow: hidden;
    height: 100vh;
    display: flex;
    flex-direction: column;
  }}
  
  .header {{
    background-color: #333;
    color: white;
    padding: 10px;
    display: flex;
    justify-content: space-between;
    align-items: center;
  }}
  
  .header h1 {{
    margin: 0;
    font-size: 1.5em;
  }}
  
  .controls {{
    display: flex;
    align-items: center;
  }}
  
  select, button {{
    margin-left: 10px;
    padding: 5px 10px;
    border: none;
    border-radius: 3px;
    background-color: #555;
    color: white;
    cursor: pointer;
  }}
  
  button:hover {{
    background-color: #777;
  }}
  
  #mapContainer {{
    flex: 1;
    position: relative;
    overflow: hidden;
    background-color: #000;
    cursor: grab;
  }}
  
  #mapImg {{
    position: absolute;
    image-rendering: pixelated;
  }}
  
  .player-marker {{
    position: absolute;
    width: 16px;
    height: 16px;
    background: rgba(255,0,0,0.7);
    border-radius: 50%;
    pointer-events: none;
    transform: translate(-50%, -50%);
    z-index: 10;
    box-shadow: 0 0 5px #fff;
  }}
  
  #loading {{
    position: absolute;
    top: 50%;
    left: 50%;
    transform: translate(-50%, -50%);
    background: rgba(0,0,0,0.7);
    color: white;
    padding: 10px 20px;
    border-radius: 5px;
    z-index: 20;
    display: none;
  }}
  
  #coordinates {{
    position: absolute;
    bottom: 10px;
    left: 10px;
    background: rgba(0,0,0,0.7);
    color: white;
    padding: 5px 10px;
    border-radius: 3px;
    font-family: monospace;
    z-index: 10;
  }}
  
  .location-buttons {{
    position: absolute;
    top: 10px;
    right: 10px;
    display: flex;
    flex-direction: column;
    z-index: 10;
  }}
  
  .location-buttons button {{
    margin-bottom: 5px;
    background: rgba(0,0,0,0.7);
    color: white;
    border: 1px solid #555;
  }}
  
  .zoom-controls {{
    position: absolute;
    bottom: 10px;
    right: 10px;
    display: flex;
    flex-direction: column;
    z-index: 10;
  }}
  
  .zoom-controls button {{
    width: 40px;
    height: 40px;
    font-size: 20px;
    margin-bottom: 5px;
    background: rgba(0,0,0,0.7);
    color: white;
    border: 1px solid #555;
  }}
</style>
</head>
<body>
<div class='header'>
  <h1>ServUO Interactive Map (Port {port})</h1>
  <div class='controls'>
    <select id='mapSelect'>
      <option value='0'>Felucca</option>
      <option value='1'>Trammel</option>
      <option value='2'>Ilshenar</option>
      <option value='3'>Malas</option>
      <option value='4'>Tokuno</option>
    </select>
    <button id='resetView'>Reset View</button>
  </div>
</div>

<div id='mapContainer'>
  <div id='loading'>Loading map...</div>
  <img id='mapImg' />
  
  <div id='coordinates'>
    X: <span id='viewX'>0</span>, Y: <span id='viewY'>0</span>, 
    Zoom: <span id='zoomLevel'>1.0</span>x, 
    Map: <span id='viewMap'>Felucca</span>
  </div>
  
  <div class='location-buttons'>
    <button id='showBritain'>Britain</button>
    <button id='showMinoc'>Minoc</button>
    <button id='showTrinsic'>Trinsic</button>
  </div>
  
  <div class='zoom-controls'>
    <button id='zoomIn'>+</button>
    <button id='zoomOut'>−</button>
  </div>
</div>

<script>
  const mapContainer = document.getElementById('mapContainer');
  const mapImg = document.getElementById('mapImg');
  const viewX = document.getElementById('viewX');
  const viewY = document.getElementById('viewY');
  const zoomLevel = document.getElementById('zoomLevel');
  const viewMap = document.getElementById('viewMap');
  const loading = document.getElementById('loading');
  const mapSelect = document.getElementById('mapSelect');
  
  // Map names for display
  const mapNames = ['Felucca', 'Trammel', 'Ilshenar', 'Malas', 'Tokuno'];
  
  // Map boundaries for each map
  const mapBounds = [
    {{ width: 6144, height: 4096 }}, // Felucca
    {{ width: 6144, height: 4096 }}, // Trammel
    {{ width: 2304, height: 1600 }}, // Ilshenar
    {{ width: 2560, height: 2048 }}, // Malas
    {{ width: 1448, height: 1448 }}  // Tokuno
  ];
  
  // City locations for each map
  const locations = {{
    '0': {{ // Felucca
      britain: {{ x: 1400, y: 1600 }},
      minoc: {{ x: 2500, y: 500 }},
      trinsic: {{ x: 1900, y: 2800 }}
    }},
    '1': {{ // Trammel (same as Felucca)
      britain: {{ x: 1400, y: 1600 }},
      minoc: {{ x: 2500, y: 500 }},
      trinsic: {{ x: 1900, y: 2800 }}
    }},
    '2': {{ // Ilshenar
      compassion: {{ x: 1215, y: 467 }},
      honesty: {{ x: 722, y: 1366 }},
      valor: {{ x: 528, y: 187 }}
    }},
    '3': {{ // Malas
      luna: {{ x: 1000, y: 500 }},
      umbra: {{ x: 1997, y: 1386 }}
    }},
    '4': {{ // Tokuno
      makoto: {{ x: 802, y: 1204 }},
      homare: {{ x: 270, y: 320 }}
    }}
  }};
  
  // Viewport state
  let state = {{
    mapIndex: 0,
    centerX: 1000,
    centerY: 1000,
    zoom: 1.0,
    tileSize: 8, // 8 pixels per tile matches our server-side rendering
    isDragging: false,
    lastMouseX: 0,
    lastMouseY: 0,
    players: [],
    pendingRequest: false
  }};
  
  // Calculate viewport dimensions based on container size and zoom
  function getViewport() {{
    const containerWidth = mapContainer.clientWidth;
    const containerHeight = mapContainer.clientHeight;
    
    const effectiveTileSize = state.tileSize * state.zoom;
    
    const tilesWide = Math.ceil(containerWidth / effectiveTileSize);
    const tilesHigh = Math.ceil(containerHeight / effectiveTileSize);
    
    const startX = Math.floor(state.centerX - tilesWide / 2);
    const startY = Math.floor(state.centerY - tilesHigh / 2);
    
    return {{
      startX,
      startY,
      width: tilesWide,
      height: tilesHigh,
      effectiveTileSize
    }};
  }}
  
  // Update the map display
  function updateMap() {{
    if (state.pendingRequest) {{
      return; // Don't send multiple requests at once
    }}
    
    loading.style.display = 'block';
    state.pendingRequest = true;
    
    const viewport = getViewport();
    const currentBounds = mapBounds[state.mapIndex];
    
    // Ensure center point is within map bounds
    state.centerX = Math.max(0, Math.min(currentBounds.width, state.centerX));
    state.centerY = Math.max(0, Math.min(currentBounds.height, state.centerY));
    
    // Calculate map section to request
    const requestX = Math.max(0, viewport.startX);
    const requestY = Math.max(0, viewport.startY);
    const requestWidth = Math.min(viewport.width, currentBounds.width - requestX);
    const requestHeight = Math.min(viewport.height, currentBounds.height - requestY);
    
    // Create a new image to prevent flickering
    const newImg = new Image();
    
    newImg.onload = function() {{
      // Replace the old image
      mapImg.src = newImg.src;
      
      // Position the image correctly within the container
      const offsetX = (requestX - viewport.startX) * viewport.effectiveTileSize;
      const offsetY = (requestY - viewport.startY) * viewport.effectiveTileSize;
      
      mapImg.style.left = offsetX + 'px';
      mapImg.style.top = offsetY + 'px';
      mapImg.style.width = (requestWidth * viewport.effectiveTileSize) + 'px';
      mapImg.style.height = (requestHeight * viewport.effectiveTileSize) + 'px';
      
      loading.style.display = 'none';
      state.pendingRequest = false;
      
      // Update player markers
      updatePlayerMarkers();
    }};
    
    newImg.onerror = function() {{
      console.error('Failed to load map');
      loading.style.display = 'none';
      state.pendingRequest = false;
      
      // Try with smaller dimensions if we failed
      if (requestWidth > 50 || requestHeight > 50) {{
        state.zoom = Math.max(0.5, state.zoom * 0.8);
        setTimeout(updateMap, 500); // Retry after a delay
      }}
    }};
    
    // Set the source to request the map
    newImg.src = `/map?x=${{requestX}}&y=${{requestY}}&width=${{requestWidth}}&height=${{requestHeight}}&map=${{state.mapIndex}}&t=${{Date.now()}}`;
    
    // Update coordinate display
    viewX.textContent = Math.floor(state.centerX);
    viewY.textContent = Math.floor(state.centerY);
    zoomLevel.textContent = state.zoom.toFixed(1);
    viewMap.textContent = mapNames[state.mapIndex];
    
    // Update map selector
    mapSelect.value = state.mapIndex;
  }}
  
  // Update player markers on the map
  function updatePlayerMarkers() {{
    // Remove existing markers
    document.querySelectorAll('.player-marker').forEach(e => e.remove());
    
    const viewport = getViewport();
    
    state.players.forEach(p => {{
      // Check if player is on the current map
      if (p.Map === mapNames[state.mapIndex]) {{
        // Calculate screen position
        const screenX = (p.X - viewport.startX) * viewport.effectiveTileSize;
        const screenY = (p.Y - viewport.startY) * viewport.effectiveTileSize;
        
        // Check if player is visible in the current view
        if (screenX >= 0 && screenX <= mapContainer.clientWidth &&
            screenY >= 0 && screenY <= mapContainer.clientHeight) {{
          
          const marker = document.createElement('div');
          marker.className = 'player-marker';
          marker.style.left = screenX + 'px';
          marker.style.top = screenY + 'px';
          marker.title = p.Name;
          mapContainer.appendChild(marker);
        }}
      }}
    }});
  }}
  
  // Handle map selection change
  mapSelect.addEventListener('change', () => {{
    state.mapIndex = parseInt(mapSelect.value);
    
    // Reset to center of map
    const currentBounds = mapBounds[state.mapIndex];
    state.centerX = Math.floor(currentBounds.width / 2);
    state.centerY = Math.floor(currentBounds.height / 2);
    
    updateMap();
  }});
  
  // Reset view button
  document.getElementById('resetView').addEventListener('click', () => {{
    const currentBounds = mapBounds[state.mapIndex];
    state.centerX = Math.floor(currentBounds.width / 2);
    state.centerY = Math.floor(currentBounds.height / 2);
    state.zoom = 1.0;
    updateMap();
  }});
  
  // Zoom controls
  document.getElementById('zoomIn').addEventListener('click', () => {{
    state.zoom = Math.min(4.0, state.zoom * 1.5);
    updateMap();
  }});
  
  document.getElementById('zoomOut').addEventListener('click', () => {{
    state.zoom = Math.max(0.2, state.zoom / 1.5);
    updateMap();
  }});
  
  // Location buttons
  document.getElementById('showBritain').addEventListener('click', () => {{
    const loc = locations[state.mapIndex]?.britain;
    if (loc) {{
      state.centerX = loc.x;
      state.centerY = loc.y;
      state.zoom = 1.0;
      updateMap();
    }}
  }});
  
  document.getElementById('showMinoc').addEventListener('click', () => {{
    const loc = locations[state.mapIndex]?.minoc;
    if (loc) {{
      state.centerX = loc.x;
      state.centerY = loc.y;
      state.zoom = 1.0;
      updateMap();
    }}
  }});
  
  document.getElementById('showTrinsic').addEventListener('click', () => {{
    const loc = locations[state.mapIndex]?.trinsic;
    if (loc) {{
      state.centerX = loc.x;
      state.centerY = loc.y;
      state.zoom = 1.0;
      updateMap();
    }}
  }});
  
  // Mouse wheel zoom
  mapContainer.addEventListener('wheel', (e) => {{
    e.preventDefault();
    
    // Get mouse position relative to container
    const rect = mapContainer.getBoundingClientRect();
    const mouseX = e.clientX - rect.left;
    const mouseY = e.clientY - rect.top;
    
    // Calculate map coordinates under mouse before zoom
    const viewport = getViewport();
    const mapX = viewport.startX + mouseX / viewport.effectiveTileSize;
    const mapY = viewport.startY + mouseY / viewport.effectiveTileSize;
    
    // Adjust zoom level
    if (e.deltaY < 0) {{
      // Zoom in
      state.zoom = Math.min(4.0, state.zoom * 1.1);
    }} else {{
      // Zoom out
      state.zoom = Math.max(0.2, state.zoom / 1.1);
    }}
    
    // Calculate new viewport
    const newViewport = getViewport();
    
    // Adjust center to keep mouse position over same map point
    state.centerX = mapX - (mouseX / newViewport.effectiveTileSize - viewport.startX);
    state.centerY = mapY - (mouseY / newViewport.effectiveTileSize - viewport.startY);
    
    updateMap();
  }});
  
  // Mouse drag to pan
  mapContainer.addEventListener('mousedown', (e) => {{
    if (e.button === 0) {{ // Left mouse button
      state.isDragging = true;
      state.lastMouseX = e.clientX;
      state.lastMouseY = e.clientY;
      mapContainer.style.cursor = 'grabbing';
    }}
  }});
  
  window.addEventListener('mousemove', (e) => {{
    if (state.isDragging) {{
      const dx = e.clientX - state.lastMouseX;
      const dy = e.clientY - state.lastMouseY;
      
      const viewport = getViewport();
      
      // Convert pixel movement to tile movement
      const tileDX = dx / viewport.effectiveTileSize;
      const tileDY = dy / viewport.effectiveTileSize;
      
      // Move in opposite direction of drag
      state.centerX -= tileDX;
      state.centerY -= tileDY;
      
      state.lastMouseX = e.clientX;
      state.lastMouseY = e.clientY;
      
      updateMap();
    }}
  }});
  
  window.addEventListener('mouseup', () => {{
    if (state.isDragging) {{
      state.isDragging = false;
      mapContainer.style.cursor = 'grab';
    }}
  }});
  
  // Double click to center and zoom in
  mapContainer.addEventListener('dblclick', (e) => {{
    // Get mouse position relative to container
    const rect = mapContainer.getBoundingClientRect();
    const mouseX = e.clientX - rect.left;
    const mouseY = e.clientY - rect.top;
    
    // Calculate map coordinates under mouse
    const viewport = getViewport();
    const mapX = viewport.startX + mouseX / viewport.effectiveTileSize;
    const mapY = viewport.startY + mouseY / viewport.effectiveTileSize;
    
    // Center on that point and zoom in
    state.centerX = mapX;
    state.centerY = mapY;
    state.zoom = Math.min(4.0, state.zoom * 1.5);
    
    updateMap();
  }});
  
  // WebSocket connection for player positions
  const ws = new WebSocket('ws://' + window.location.host + '/players');
  ws.onmessage = function(event) {{
    state.players = JSON.parse(event.data);
    updatePlayerMarkers();
  }};
  
  // Handle window resize
  window.addEventListener('resize', () => {{
    updateMap();
  }});
  
  // Handle touch events for mobile devices
  let touchStartX, touchStartY;
  let initialPinchDistance = 0;
  
  mapContainer.addEventListener('touchstart', (e) => {{
    if (e.touches.length === 1) {{
      // Single touch - prepare for panning
      touchStartX = e.touches[0].clientX;
      touchStartY = e.touches[0].clientY;
      state.lastMouseX = touchStartX;
      state.lastMouseY = touchStartY;
      state.isDragging = true;
    }} else if (e.touches.length === 2) {{
      // Two touches - prepare for pinch zoom
      const dx = e.touches[0].clientX - e.touches[1].clientX;
      const dy = e.touches[0].clientY - e.touches[1].clientY;
      initialPinchDistance = Math.sqrt(dx * dx + dy * dy);
    }}
  }});
  
  mapContainer.addEventListener('touchmove', (e) => {{
    e.preventDefault(); // Prevent scrolling
    
    if (e.touches.length === 1 && state.isDragging) {{
      // Single touch - pan
      const touchX = e.touches[0].clientX;
      const touchY = e.touches[0].clientY;
      
      const dx = touchX - state.lastMouseX;
      const dy = touchY - state.lastMouseY;
      
      const viewport = getViewport();
      
      // Convert pixel movement to tile movement
      const tileDX = dx / viewport.effectiveTileSize;
      const tileDY = dy / viewport.effectiveTileSize;
      
      // Move in opposite direction of drag
      state.centerX -= tileDX;
      state.centerY -= tileDY;
      
      state.lastMouseX = touchX;
      state.lastMouseY = touchY;
      
      updateMap();
    }} else if (e.touches.length === 2) {{
      // Two touches - pinch zoom
      const dx = e.touches[0].clientX - e.touches[1].clientX;
      const dy = e.touches[0].clientY - e.touches[1].clientY;
      const pinchDistance = Math.sqrt(dx * dx + dy * dy);
      
      if (initialPinchDistance > 0) {{
        // Calculate zoom factor
        const zoomFactor = pinchDistance / initialPinchDistance;
        
        // Get center point between the two touches
        const centerX = (e.touches[0].clientX + e.touches[1].clientX) / 2;
        const centerY = (e.touches[0].clientY + e.touches[1].clientY) / 2;
        
        // Get map coordinates under center point
        const rect = mapContainer.getBoundingClientRect();
        const mouseX = centerX - rect.left;
        const mouseY = centerY - rect.top;
        
        const viewport = getViewport();
        const mapX = viewport.startX + mouseX / viewport.effectiveTileSize;
        const mapY = viewport.startY + mouseY / viewport.effectiveTileSize;
        
        // Adjust zoom level
        const oldZoom = state.zoom;
        state.zoom = Math.max(0.2, Math.min(4.0, oldZoom * zoomFactor));
        
        // Adjust center to keep touch center over same map point
        const newViewport = getViewport();
        state.centerX = mapX - (mouseX / newViewport.effectiveTileSize - viewport.startX);
        state.centerY = mapY - (mouseY / newViewport.effectiveTileSize - viewport.startY);
        
        initialPinchDistance = pinchDistance;
        updateMap();
      }}
    }}
  }});
  
  mapContainer.addEventListener('touchend', () => {{
    state.isDragging = false;
    initialPinchDistance = 0;
  }});
  
  // Prevent context menu on right click
  mapContainer.addEventListener('contextmenu', (e) => {{
    e.preventDefault();
  }});
  
  // Initialize the map
  updateMap();
  
  // Add keyboard navigation
  window.addEventListener('keydown', (e) => {{
    const moveAmount = 10 / state.zoom; // Move more when zoomed out
    
    switch(e.key) {{
      case 'ArrowUp':
        state.centerY -= moveAmount;
        updateMap();
        e.preventDefault();
        break;
      case 'ArrowDown':
        state.centerY += moveAmount;
        updateMap();
        e.preventDefault();
        break;
      case 'ArrowLeft':
        state.centerX -= moveAmount;
        updateMap();
        e.preventDefault();
        break;
      case 'ArrowRight':
        state.centerX += moveAmount;
        updateMap();
        e.preventDefault();
        break;
      case '+':
        state.zoom = Math.min(4.0, state.zoom * 1.2);
        updateMap();
        e.preventDefault();
        break;
      case '-':
        state.zoom = Math.max(0.2, state.zoom / 1.2);
        updateMap();
        e.preventDefault();
        break;
      case 'Home':
        const currentBounds = mapBounds[state.mapIndex];
        state.centerX = Math.floor(currentBounds.width / 2);
        state.centerY = Math.floor(currentBounds.height / 2);
        updateMap();
        e.preventDefault();
        break;
    }}
  }});
</script>
</body>
</html>
";
}

  
    }
}
