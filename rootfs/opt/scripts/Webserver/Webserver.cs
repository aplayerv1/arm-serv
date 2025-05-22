using System;
using System.IO;
using System.Net;
using System.Text;
using System.Timers;
using Server;
using Server.Mobiles;

namespace Server.Custom.Webserver
{
    public static class Webserver
    {
        private static readonly HttpListener Listener = new HttpListener();
        private static Timer updateTimer;

        public static void Initialize()
        {
            Listener.Prefixes.Add("http://*:8822/");
            Listener.Start();
            Listener.BeginGetContext(OnRequest, Listener);

            updateTimer = new Timer(5000); // 5 seconds in milliseconds
            updateTimer.Elapsed += OnTimedEvent;
            updateTimer.AutoReset = true;
            updateTimer.Start();

            Console.WriteLine("[Webserver] Listening on port 8080.");
        }

        private static void OnTimedEvent(object sender, ElapsedEventArgs e)
        {
            // You could update cached values or other logic here
        }

        private static void OnRequest(IAsyncResult result)
        {
            try
            {
                var context = Listener.EndGetContext(result);
                Listener.BeginGetContext(OnRequest, Listener);

                var responseText = GeneratePlayerMapHtml();

                var buffer = Encoding.UTF8.GetBytes(responseText);
                context.Response.ContentLength64 = buffer.Length;
                context.Response.ContentType = "text/html";
                context.Response.OutputStream.Write(buffer, 0, buffer.Length);
                context.Response.OutputStream.Close();
            }
            catch (Exception ex)
            {
                Console.WriteLine("[Webserver] Error: " + ex.Message);
            }
        }

        private static string GeneratePlayerMapHtml()
        {
            var sb = new StringBuilder();

            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html><head><title>UO Live Map</title></head><body>");
            sb.AppendLine("<h1>Live Player Coordinates</h1>");
            sb.AppendLine("<ul>");

            foreach (NetState state in NetState.Instances)
            {
                if (state != null && state.Mobile != null && !state.Mobile.Deleted)
                {
                    var mob = state.Mobile;
                    sb.AppendFormat("<li>{0} - X: {1}, Y: {2}, Map: {3}</li>", mob.Name, mob.X, mob.Y, mob.Map);
                }
            }

            sb.AppendLine("</ul>");
            sb.AppendLine("<p>Last updated: " + DateTime.UtcNow.ToString("u") + "</p>");
            sb.AppendLine("<meta http-equiv='refresh' content='5'>");
            sb.AppendLine("</body></html>");

            return sb.ToString();
        }
    }
}
