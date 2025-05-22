using System;
using Server.Custom;  // To access both Webserver and TelnetConsole classes

namespace Server.Custom
{
    public class Startup
    {
        public static void Initialize()
        {
            Console.WriteLine("Initializing Custom Components...");

            TelnetConsole.Initialize();
            Webserver.Initialize();

            Console.WriteLine("Custom Initialization Complete.");
        }
    }
}
