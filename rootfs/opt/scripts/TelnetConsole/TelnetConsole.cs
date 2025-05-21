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
using Server.Accounting;
using Server.Misc;


namespace Server.Custom
{
    public class TelnetConsole
    {
        private static TcpListener _listener;
        private static Thread _listenerThread;
        private static int _port = 6003;
        private static int _maxPort = 6010;

        // Configuration
        private static readonly string[] AllowedIPs = { "127.0.0.1", "10.10.1.230" }; // Add IPs here
        private static readonly bool PersistFakeAdmin = false;
        private static readonly string LogPath = "Logs/TelnetCommands.log";

        public static void Initialize()
        {
            if (_listener != null)
            {
                Console.WriteLine("[TelnetConsole] Already initialized.");
                return;
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
                    var endpoint = client.Client.RemoteEndPoint as IPEndPoint;

                    if (endpoint == null || !AllowedIPs.Contains(endpoint.Address.ToString()))
                    {
                        Console.WriteLine("[TelnetConsole] Rejected connection from " + endpoint?.Address);
                        client.Close();
                        continue;
                    }

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
                writer = new StreamWriter(stream, Encoding.ASCII) { AutoFlush = true };

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
                    string logEntry = $"[{DateTime.Now}] {username}: {commandText}";
                    File.AppendAllText(LogPath, logEntry + Environment.NewLine);

                    Timer.DelayCall(TimeSpan.Zero, () =>
                    {
                        try
                        {
                            Mobile admin = World.Mobiles.Values.FirstOrDefault(m => m.AccessLevel >= AccessLevel.Administrator);
                            bool createdFake = false;

                            if (admin == null)
                            {
                                admin = CreateFakeAdmin();
                                createdFake = true;
                            }

                            CommandSystem.Handle(admin, CommandSystem.Prefix + commandText);
                            Console.WriteLine("[TelnetConsole] Executed command: " + commandText);

                            if (createdFake && !PersistFakeAdmin)
                            {
                                Timer.DelayCall(TimeSpan.FromSeconds(5), () =>
                                {
                                    if (admin != null && !admin.Deleted)
                                        admin.Delete();
                                });
                            }
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
                writer?.Dispose();
                reader?.Dispose();
                stream?.Dispose();
                client.Close();
            }
        }

        private static Mobile CreateFakeAdmin()
        {
            string username = "FakeAdmin";
            string password = "changeme";

            Account account = Accounts.GetAccount(username) as Account;

            if (account == null)
            {
                account = new Account(username, password);
                account.AccessLevel = AccessLevel.Administrator;
                Accounts.Add(account); // Adds the account to the server's account list
            }
            else
            {
                account.AccessLevel = AccessLevel.Administrator;
            }

            PlayerMobile fake = new PlayerMobile
            {
                Name = "FakeAdmin",
                AccessLevel = AccessLevel.Administrator,
                Hidden = true,
                Body = 400,
                Hue = 0,
                Female = false,
                Blessed = true,
                CantWalk = true,
                Account = account
            };

            // Assign the mobile to the first available slot
            for (int i = 0; i < account.Length; i++)
            {
                if (account[i] == null)
                {
                    account[i] = fake;
                    break;
                }
            }

            World.AddMobile(fake);
            fake.MoveToWorld(new Point3D(0, 0, 0), Map.Felucca);

            return fake;
        }

    }
}