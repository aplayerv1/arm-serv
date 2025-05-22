using Server;

namespace Server.Custom
{
    public class Startup
    {
        public static void Initialize()
        {
            Console.WriteLine("Initializing Custom Components...");

            Telnet.TelnetConsole.Initialize();
            Webserver.Webserver.Initialize();

            Console.WriteLine("Custom Initialization Complete.");
        }
    }
}
