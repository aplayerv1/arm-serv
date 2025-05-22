using System;
using System.IO;
using System.Net;
using System.Text;
using System.Timers;
using System.Collections.Generic;
using Server;
using Server.Mobiles;

namespace Server.Custom
{
    public class MapWebServer
    {
        private static HttpListener _listener;
        private static string _mapHtml = Path.Combine(Core.BaseDirectory, "map.html");
        private static string _playerJson = Path.Combine(Core.BaseDirectory, "players.json");
        private static Timer _playerUpdateTimer;

        public static void Initialize()
        {
            EventSink.WorldLoad += OnWorldLoad;
        }

        private static void OnWorldLoad()
        {
            try
            {
                _listener = new HttpListener();
                _listener.Prefixes.Add("http://*:8080/");
                _listener.Start();
                _listener.BeginGetContext(OnRequest, null);
                Console.WriteLine("[MapWebServer] Serving http://localhost:8080");

                _playerUpdateTimer = new Timer(1000); // every 1 second
                _playerUpdateTimer.Elapsed += UpdatePlayerData;
                _playerUpdateTimer.Start();
            }
            catch (Exception ex)
            {
                Console.WriteLine("[MapWebServer] Error: " + ex);
            }
        }

        private static void UpdatePlayerData(object sender, ElapsedEventArgs e)
        {
            try
            {
                var players = new List<string>();

                foreach (Mobile m in World.Mobiles.Values)
                {
                    if (m is PlayerMobile player && !player.Deleted && player.Map != null)
                    {
                        string json = $"{{\"name\":\"{player.Name}\",\"x\":{player.X},\"y\":{player.Y}}}";
                        players.Add(json);
                    }
                }

                File.WriteAllText(_playerJson, $"[{string.Join(",", players)}]");
            }
            catch (Exception ex)
            {
                Console.WriteLine("[MapWebServer] Error updating player JSON: " + ex);
            }
        }

        private static void OnRequest(IAsyncResult result)
        {
            if (_listener == null || !_listener.IsListening)
                return;

            var context = _listener.EndGetContext(result);
            _listener.BeginGetContext(OnRequest, null); // next request

            var response = context.Response;
            string path = context.Request.Url.AbsolutePath.TrimStart('/');

            try
            {
                string filePath = path switch
                {
                    "players.json" => _playerJson,
                    "" or "map.html" => _mapHtml,
                    _ => null
                };

                if (filePath != null && File.Exists(filePath))
                {
                    string contentType = path.EndsWith(".json") ? "application/json" : "text/html";
                    byte[] buffer = File.ReadAllBytes(filePath);
                    response.ContentType = contentType;
                    response.ContentLength64 = buffer.Length;
                    response.OutputStream.Write(buffer, 0, buffer.Length);
                }
                else
                {
                    byte[] buffer = Encoding.UTF8.GetBytes("404 Not Found");
                    response.StatusCode = 404;
                    response.ContentType = "text/plain";
                    response.OutputStream.Write(buffer, 0, buffer.Length);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[MapWebServer] Error: " + ex);
            }
            finally
            {
                response.OutputStream.Close();
            }
        }
    }
}