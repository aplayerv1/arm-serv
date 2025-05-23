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

                    Console.WriteLine($"[Webserver] Started on port {tryPort}");
                    started = true;
                    break;
                }
                catch (HttpListenerException ex)
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
                                await writer.WriteAsync(GetHtmlPage());
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
            if ((tileID >= 0x0E00 && tileID <= 0x0EFF) || (tileID >= 0x25A && tileID <= 0x280))
                return Color.DarkGreen;

            return Color.Brown;
        }

        private static string GetHtmlPage()
        {
            return @"
<!DOCTYPE html>
<html lang='en'>
<head>
<meta charset='UTF-8' />
<meta name='viewport' content='width=device-width, initial-scale=1' />
<title>ServUO Dynamic Map</title>
<style>
  #mapContainer {
    position: relative;
    width: 480px;
    height: 360px;
    border: 1px solid black;
  }
  #mapImg {
    image-rendering: pixelated;
    width: 480px;
    height: 360px;
  }
  .player-marker {
    position: absolute;
    width: 16px;
    height: 16px;
    background: rgba(255,0,0,0.7);
    border-radius: 50%;
    pointer-events: none;
    transform: translate(-50%, -50%);
  }
</style>
</head>
<body>
<h1>ServUO Dynamic Map (Port 8822)</h1>
<div id='mapContainer'>
  <img id='mapImg' src='/map?x=100&y=100&width=20&height=15' />
</div>

<script>
  const mapContainer = document.getElementById('mapContainer');
  const mapImg = document.getElementById('mapImg');
  const tilePixelSize = 24;
  let viewport = { x: 100, y: 100, width: 20, height: 15 };

  const ws = new WebSocket('ws://' + window.location.host + '/players');
  ws.onmessage = function(event) {
    const players = JSON.parse(event.data);
    document.querySelectorAll('.player-marker').forEach(e => e.remove());

    players.forEach(p => {
      if (p.Map === 'Felucca' &&
          p.X >= viewport.x && p.X < viewport.x + viewport.width &&
          p.Y >= viewport.y && p.Y < viewport.y + viewport.height) {

        const px = (p.X - viewport.x) * tilePixelSize;
        const py = (p.Y - viewport.y) * tilePixelSize;

        const marker = document.createElement('div');
        marker.className = 'player-marker';
        marker.style.left = px + 'px';
        marker.style.top = py + 'px';
        marker.title = p.Name;
        mapContainer.appendChild(marker);
      }
    });
  };
</script>
</body>
</html>
";
        }
    }
}
