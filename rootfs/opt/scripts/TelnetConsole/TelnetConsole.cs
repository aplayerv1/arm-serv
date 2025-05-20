using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Linq;
using Server;
using Server.Commands;
using Server.Mobiles;

namespace Server.Custom
{
    public class TelnetConsole
    {
        private static TcpListener _listener;
        private static Thread _listenerThread;
        private static int _port = 6003;
        private static int _maxPort = 6010;

        // Create a fake admin Mobile to run commands with
        private static Mobile _fakeAdmin;

        public static void Initialize()
        {
            if (_listener != null)
            {
                Console.WriteLine("[TelnetConsole] Already initialized.");
                return;
            }

            // Initialize fake admin only once
            if (_fakeAdmin == null)
            {
                _fakeAdmin = new Mobile()
                {
                    AccessLevel = AccessLevel.Administrator
                };
            }

            bool started = false;
            while (_port <= _maxPort)
            {
                try
                {
                    _listener = new TcpListener(IPAddress.Any, _port);
                    _listener.Start();
                    started = true;
                    break;
                }
                catch (SocketException ex)
                {
                    if (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
                    {
                        Console.WriteLine("[TelnetConsole] Port {0} in use. Trying next...", _port);
                        _port++;
                    }
                    else
                    {
                        Console.WriteLine("[TelnetConsole] Failed to start on port {0}: {1}", _port, ex.Message);
                        return;
                    }
                }
            }

            if (!started)
            {
                Console.WriteLine("[TelnetConsole] ERROR: No available ports between 6003–6010.");
                return;
            }

            _listenerThread = new Thread(ListenForClients);
            _listenerThread.IsBackground = true;
            _listenerThread.Start();

            Console.WriteLine("[TelnetConsole] Listening on port {0}", _port);
        }

        private static void ListenForClients()
        {
            while (true)
            {
                try
                {
                    TcpClient client = _listener.AcceptTcpClient();
                    Thread clientThread = new Thread(() => HandleClient(client));
                    clientThread.IsBackground = true;
                    clientThread.Start();
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[TelnetConsole] Listener error: " + ex.Message);
                }
            }
        }

        private static void HandleClient(TcpClient client)
        {
            NetworkStream stream = null;
            StreamReader reader = null;
            StreamWriter writer = null;

            try
            {
                stream = client.GetStream();
                reader = new StreamReader(stream, Encoding.ASCII);
                writer = new StreamWriter(stream, Encoding.ASCII);
                writer.AutoFlush = true;

                writer.Write("Username: ");
                string username = reader.ReadLine();
                writer.Write("Password: ");
                string password = reader.ReadLine();

                if (username != "admin" || password != "changeme")
                {
                    writer.WriteLine("Access denied.");
                    return;
                }

                writer.WriteLine("Welcome to ServUO Telnet Console! Type 'exit' to disconnect.");

                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (line.Trim().ToLower() == "exit")
                    {
                        writer.WriteLine("Goodbye!");
                        break;
                    }

                    string commandText = line;

                    Timer.DelayCall(TimeSpan.Zero, () =>
                    {
                        try
                        {
                            // Try to find a real admin player first
                            Mobile admin = World.Mobiles.Values
                                .FirstOrDefault(m => m.AccessLevel >= AccessLevel.Administrator);

                            if (admin != null)
                            {
                                CommandSystem.Handle(admin, CommandSystem.Prefix + commandText);
                            }
                            else
                            {
                                // Use the fake admin to run commands
                                CommandSystem.Handle(_fakeAdmin, CommandSystem.Prefix + commandText);
                            }

                            Console.WriteLine("[TelnetConsole] Executed command: " + commandText);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("[TelnetConsole] Command error: " + ex.Message);
                        }
                    });

                    writer.WriteLine("Command sent: " + commandText);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[TelnetConsole] Client error: " + ex.Message);
            }
            finally
            {
                if (writer != null) writer.Dispose();
                if (reader != null) reader.Dispose();
                if (stream != null) stream.Dispose();
                client.Close();
            }
        }
    }
}
