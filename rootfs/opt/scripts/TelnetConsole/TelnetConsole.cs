using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Linq;
using System.Security.Cryptography;
using Server;
using Server.Commands;
using Server.Mobiles;
using Server.Accounting;
using Server.Misc;
using Server.Network;

namespace Server.Custom
{
    public class TelnetConsole
    {
        private static TcpListener _listener;
        private static Thread _listenerThread;
        private static int _port = 6003;
        private static int _maxPort = 6010;
        private static readonly List<TelnetSession> _activeSessions = new List<TelnetSession>();
        private static readonly object _sessionLock = new object();

        // Enhanced Configuration
        private static readonly string[] AllowedIPs = { "127.0.0.1", "10.10.1.230", "::1" }; // Added IPv6 localhost
        private static readonly bool PersistFakeAdmin = false;
        private static readonly string LogPath = "Logs/TelnetCommands.log";
        private static readonly int MaxConcurrentSessions = 5;
        private static readonly int SessionTimeoutMinutes = 30;
        private static readonly int MaxFailedAttempts = 3;
        private static readonly Dictionary<string, FailedAttempt> _failedAttempts = new Dictionary<string, FailedAttempt>();
        
        // Enhanced authentication
        private static readonly Dictionary<string, UserCredentials> _users = new Dictionary<string, UserCredentials>
        {
            { "admin", new UserCredentials("admin", HashPassword("changeme"), AccessLevel.Administrator) },
            { "gm", new UserCredentials("gm", HashPassword("gmpass"), AccessLevel.GameMaster) },
            { "seer", new UserCredentials("seer", HashPassword("seerpass"), AccessLevel.Seer) }
        };

        private class UserCredentials
        {
            public string Username { get; }
            public string PasswordHash { get; }
            public AccessLevel AccessLevel { get; }
            public DateTime LastLogin { get; set; }
            public int LoginCount { get; set; }

            public UserCredentials(string username, string passwordHash, AccessLevel accessLevel)
            {
                Username = username;
                PasswordHash = passwordHash;
                AccessLevel = accessLevel;
                LastLogin = DateTime.MinValue;
                LoginCount = 0;
            }
        }

        private class FailedAttempt
        {
            public int Count { get; set; }
            public DateTime LastAttempt { get; set; }
            public DateTime LockoutUntil { get; set; }
        }

        private class TelnetSession
        {
            public string SessionId { get; }
            public TcpClient Client { get; }
            public NetworkStream Stream { get; }
            public StreamReader Reader { get; }
            public StreamWriter Writer { get; }
            public string Username { get; set; }
            public AccessLevel AccessLevel { get; set; }
            public DateTime ConnectedAt { get; }
            public DateTime LastActivity { get; set; }
            public string RemoteEndPoint { get; }
            public Mobile FakeAdmin { get; set; }

            public TelnetSession(TcpClient client)
            {
                SessionId = Guid.NewGuid().ToString("N").Substring(0, 8);
                Client = client;
                Stream = client.GetStream();
                Reader = new StreamReader(Stream, Encoding.UTF8);
                Writer = new StreamWriter(Stream, Encoding.UTF8) { AutoFlush = true };
                ConnectedAt = DateTime.Now;
                LastActivity = DateTime.Now;
                RemoteEndPoint = client.Client.RemoteEndPoint?.ToString() ?? "Unknown";
            }

            public void UpdateActivity()
            {
                LastActivity = DateTime.Now;
            }

            public bool IsTimedOut => DateTime.Now - LastActivity > TimeSpan.FromMinutes(SessionTimeoutMinutes);

            public void Dispose()
            {
                try
                {
                    if (FakeAdmin != null && !FakeAdmin.Deleted && !PersistFakeAdmin)
                    {
                        FakeAdmin.Delete();
                    }
                    Writer?.Dispose();
                    Reader?.Dispose();
                    Stream?.Dispose();
                    Client?.Close();
                }
                catch { /* ignore cleanup errors */ }
            }
        }

        public static void Initialize()
        {
            if (_listener != null)
            {
                Console.WriteLine("[TelnetConsole] Already initialized.");
                return;
            }

            // Ensure log directory exists
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath));

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

            // Start cleanup timer
            Timer.DelayCall(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1), CleanupSessions);

            Console.WriteLine("[TelnetConsole] Listening on port {0}", _port);
            LogMessage($"TelnetConsole started on port {_port}");
        }

        public static void Shutdown()
        {
            try
            {
                _listener?.Stop();
                _listenerThread?.Join(5000);

                lock (_sessionLock)
                {
                    foreach (var session in _activeSessions.ToList())
                    {
                        try
                        {
                            session.Writer?.WriteLine("Server is shutting down. Goodbye!");
                            session.Dispose();
                        }
                        catch { /* ignore */ }
                    }
                    _activeSessions.Clear();
                }

                Console.WriteLine("[TelnetConsole] Shutdown complete.");
            }
            catch (Exception ex)
            {
                Console.WriteLine("[TelnetConsole] Error during shutdown: " + ex.Message);
            }
        }

        private static void ListenForClients()
        {
            while (true)
            {
                try
                {
                    TcpClient client = _listener.AcceptTcpClient();
                    var endpoint = client.Client.RemoteEndPoint as IPEndPoint;

                    if (endpoint == null)
                    {
                        Console.WriteLine("[TelnetConsole] Rejected connection: invalid endpoint");
                        client.Close();
                        continue;
                    }

                    string clientIP = endpoint.Address.ToString();

                    // Check IP whitelist
                    if (!AllowedIPs.Contains(clientIP))
                    {
                        Console.WriteLine("[TelnetConsole] Rejected connection from " + clientIP);
                        client.Close();
                        continue;
                    }

                    // Check concurrent session limit
                    lock (_sessionLock)
                    {
                        if (_activeSessions.Count >= MaxConcurrentSessions)
                        {
                            Console.WriteLine("[TelnetConsole] Rejected connection from " + clientIP + ": too many sessions");
                            client.Close();
                            continue;
                        }
                    }

                    // Check if IP is locked out
                    if (IsIPLockedOut(clientIP))
                    {
                        Console.WriteLine("[TelnetConsole] Rejected connection from " + clientIP + ": locked out");
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
                    break;
                }
            }
        }

        private static void HandleClient(TcpClient client)
        {
            TelnetSession session = null;
            try
            {
                session = new TelnetSession(client);
                
                lock (_sessionLock)
                {
                    _activeSessions.Add(session);
                }

                Console.WriteLine($"[TelnetConsole] New connection: {session.RemoteEndPoint} (Session: {session.SessionId})");

                // Welcome message
                session.Writer.WriteLine("=== ServUO Telnet Console ===");
                session.Writer.WriteLine($"Session ID: {session.SessionId}");
                session.Writer.WriteLine($"Connected at: {session.ConnectedAt:yyyy-MM-dd HH:mm:ss}");
                session.Writer.WriteLine();

                // Authentication
                if (!AuthenticateUser(session))
                {
                    session.Writer.WriteLine("Authentication failed. Goodbye!");
                    return;
                }

                // Main command loop
                HandleCommands(session);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TelnetConsole] Client error: {ex.Message}");
            }
            finally
            {
                if (session != null)
                {
                    lock (_sessionLock)
                    {
                        _activeSessions.Remove(session);
                    }
                    
                    Console.WriteLine($"[TelnetConsole] Session ended: {session.SessionId}");
                    LogMessage($"Session ended: {session.Username}@{session.RemoteEndPoint} (Session: {session.SessionId})");
                    session.Dispose();
                }
            }
        }

        private static bool AuthenticateUser(TelnetSession session)
        {
            int attempts = 0;
            string clientIP = ((IPEndPoint)session.Client.Client.RemoteEndPoint).Address.ToString();

            while (attempts < 3)
            {
                try
                {
                    session.Writer.Write("Username: ");
                    string username = session.Reader.ReadLine()?.Trim();
                    
                    if (string.IsNullOrEmpty(username))
                    {
                        session.Writer.WriteLine("Invalid username.");
                        attempts++;
                        continue;
                    }

                    session.Writer.Write("Password: ");
                    string password = session.Reader.ReadLine()?.Trim();

                    if (string.IsNullOrEmpty(password))
                    {
                        session.Writer.WriteLine("Invalid password.");
                        attempts++;
                        continue;
                    }

                    // Check credentials
                    if (_users.TryGetValue(username.ToLower(), out UserCredentials user))
                    {
                        if (VerifyPassword(password, user.PasswordHash))
                        {
                            session.Username = username;
                            session.AccessLevel = user.AccessLevel;
                            
                            // Update user stats
                            user.LastLogin = DateTime.Now;
                            user.LoginCount++;

                            // Clear failed attempts for this IP
                            _failedAttempts.Remove(clientIP);

                            session.Writer.WriteLine($"Welcome, {username}! Access Level: {user.AccessLevel}");
                            session.Writer.WriteLine($"Last login: {(user.LoginCount > 1 ? user.LastLogin.ToString("yyyy-MM-dd HH:mm:ss") : "First time")}");
                            session.Writer.WriteLine("Type 'help' for available commands, 'exit' to disconnect.");
                            session.Writer.WriteLine();

                            LogMessage($"Successful login: {username}@{session.RemoteEndPoint} (Session: {session.SessionId})");
                            return true;
                        }
                    }

                    // Failed authentication
                    RecordFailedAttempt(clientIP);
                    session.Writer.WriteLine("Invalid credentials.");
                    LogMessage($"Failed login attempt: {username}@{session.RemoteEndPoint}");
                    attempts++;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[TelnetConsole] Authentication error: {ex.Message}");
                    return false;
                }
            }

            LogMessage($"Too many failed attempts: {session.RemoteEndPoint}");
            return false;
        }

        private static void HandleCommands(TelnetSession session)
        {
            string line;
            while ((line = session.Reader.ReadLine()) != null)
            {
                session.UpdateActivity();
                line = line.Trim();

                if (string.IsNullOrEmpty(line))
                    continue;

                string[] parts = line.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                string command = parts[0].ToLower();

                try
                {
                    switch (command)
                    {
                        case "exit":
                        case "quit":
                            session.Writer.WriteLine("Goodbye!");
                            return;

                        case "help":
                            ShowHelp(session);
                            break;

                        case "status":
                            ShowStatus(session);
                            break;

                        case "who":
                            ShowOnlinePlayers(session);
                            break;

                        case "sessions":
                            ShowActiveSessions(session);
                            break;

                        case "broadcast":
                            if (parts.Length > 1)
                            {
                                string message = string.Join(" ", parts.Skip(1));
                                BroadcastMessage(session, message);
                            }
                            else
                            {
                                session.Writer.WriteLine("Usage: broadcast <message>");
                            }
                            break;

                        case "createaccount":
                            if (parts.Length >= 3)
                            {
                                string username = parts[1];
                                string password = parts[2];
                                AccessLevel level = AccessLevel.Player;
                                
                                if (parts.Length > 3 && Enum.TryParse<AccessLevel>(parts[3], true, out AccessLevel parsedLevel))
                                {
                                    level = parsedLevel;
                                }
                                
                                CreateGameAccount(session, username, password, level);
                            }
                            else
                            {
                                session.Writer.WriteLine("Usage: createaccount <username> <password> [accesslevel]");
                            }
                            break;

                        case "listaccounts":
                            ListGameAccounts(session);
                            break;

                        case "setaccess":
                            if (parts.Length >= 3)
                            {
                                string username = parts[1];
                                if (Enum.TryParse<AccessLevel>(parts[2], true, out AccessLevel level))
                                {
                                    SetAccountAccess(session, username, level);
                                }
                                else
                                {
                                    session.Writer.WriteLine("Invalid access level. Use: Player, Counselor, GameMaster, Seer, Administrator, Owner");
                                }
                            }
                            else
                            {
                                session.Writer.WriteLine("Usage: setaccess <username> <accessl
                                session.Writer.WriteLine("Usage: setaccess <username> <accesslevel>");
                            }
                            break;

                        default:
                            // Execute as ServUO command
                            ExecuteServUOCommand(session, line);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    session.Writer.WriteLine($"Error executing command: {ex.Message}");
                    Console.WriteLine($"[TelnetConsole] Command error: {ex.Message}");
                }
            }
        }

        private static void ExecuteServUOCommand(TelnetSession session, string commandText)
        {
            LogMessage($"Command: {session.Username}: {commandText}");

            Timer.DelayCall(TimeSpan.Zero, () =>
            {
                try
                {
                    Mobile admin = GetOrCreateAdmin(session);
                    if (admin == null)
                    {
                        Console.WriteLine("[TelnetConsole] Failed to get admin mobile");
                        return;
                    }

                    CommandSystem.Handle(admin, CommandSystem.Prefix + commandText);
                    Console.WriteLine($"[TelnetConsole] Executed command: {commandText}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[TelnetConsole] Command execution error: {ex.Message}");
                }
            });

            session.Writer.WriteLine($"Command executed: {commandText}");
        }

        private static Mobile GetOrCreateAdmin(TelnetSession session)
        {
            // Try to find existing admin with sufficient access level
            Mobile admin = World.Mobiles.Values.FirstOrDefault(m => m.AccessLevel >= session.AccessLevel && !m.Deleted);

            // If we found an existing admin, use it
            if (admin != null)
            {
                return admin;
            }

            // If no suitable admin found, try to create one without account restrictions
            if (session.FakeAdmin == null || session.FakeAdmin.Deleted)
            {
                session.FakeAdmin = CreateFakeAdminWithoutAccount(session);
            }

            return session.FakeAdmin;
        }

        private static Mobile CreateFakeAdminWithoutAccount(TelnetSession session)
        {
            // Create a temporary admin without creating an account
            PlayerMobile fake = new PlayerMobile
            {
                Name = $"TelnetAdmin_{session.SessionId}",
                AccessLevel = session.AccessLevel,
                Hidden = true,
                Body = 400,
                Hue = 0,
                Female = false,
                Blessed = true,
                CantWalk = true,
                Account = null // No account needed for command execution
            };

            World.AddMobile(fake);
            fake.MoveToWorld(new Point3D(0, 0, 0), Map.Felucca);

            Console.WriteLine($"[TelnetConsole] Created temporary admin for session {session.SessionId}");
            return fake;
        }

        private static void CreateGameAccount(TelnetSession session, string username, string password, AccessLevel accessLevel)
        {
            try
            {
                // Check if account already exists
                if (Accounts.GetAccount(username) != null)
                {
                    session.Writer.WriteLine($"Account '{username}' already exists.");
                    return;
                }

                // Create new account
                Account newAccount = new Account(username, password);
                newAccount.AccessLevel = accessLevel;
                
                // Add to accounts system
                Accounts.Add(newAccount);
                
                session.Writer.WriteLine($"Account '{username}' created successfully with access level: {accessLevel}");
                LogMessage($"Account created: {username} ({accessLevel}) by {session.Username}");
                
                Console.WriteLine($"[TelnetConsole] Account '{username}' created by {session.Username}");
            }
            catch (Exception ex)
            {
                session.Writer.WriteLine($"Error creating account: {ex.Message}");
                Console.WriteLine($"[TelnetConsole] Account creation error: {ex.Message}");
            }
        }

        private static void ListGameAccounts(TelnetSession session)
        {
            try
            {
                var accounts = Accounts.GetAccounts().Cast<Account>().ToList();
                
                session.Writer.WriteLine($"=== Game Accounts ({accounts.Count}) ===");
                
                foreach (var account in accounts.OrderBy(a => a.Username))
                {
                    string status = account.Banned ? " [BANNED]" : "";
                    string access = account.AccessLevel > AccessLevel.Player ? $" [{account.AccessLevel}]" : "";
                    session.Writer.WriteLine($"{account.Username}{access}{status}");
                }
                
                session.Writer.WriteLine();
            }
            catch (Exception ex)
            {
                session.Writer.WriteLine($"Error listing accounts: {ex.Message}");
            }
        }

        private static void SetAccountAccess(TelnetSession session, string username, AccessLevel accessLevel)
        {
            try
            {
                Account account = Accounts.GetAccount(username) as Account;
                
                if (account == null)
                {
                    session.Writer.WriteLine($"Account '{username}' not found.");
                    return;
                }
                
                account.AccessLevel = accessLevel;
                session.Writer.WriteLine($"Set '{username}' access level to: {accessLevel}");
                LogMessage($"Access level changed: {username} -> {accessLevel} by {session.Username}");
                
                Console.WriteLine($"[TelnetConsole] Access level for '{username}' set to {accessLevel}");
            }
            catch (Exception ex)
            {
                session.Writer.WriteLine($"Error setting access level: {ex.Message}");
            }
        }

        private static void ShowHelp(TelnetSession session)
        {
            session.Writer.WriteLine("=== Available Commands ===");
            session.Writer.WriteLine("help          - Show this help");
            session.Writer.WriteLine("status        - Show server status");
            session.Writer.WriteLine("who           - Show online players");
            session.Writer.WriteLine("sessions      - Show active telnet sessions");
            session.Writer.WriteLine("broadcast <msg> - Broadcast message to all players");
            session.Writer.WriteLine("createaccount <user> <pass> [level] - Create game account");
            session.Writer.WriteLine("listaccounts  - List all game accounts");
            session.Writer.WriteLine("setaccess <user> <level> - Set account access level");
            session.Writer.WriteLine("exit/quit     - Disconnect");
            session.Writer.WriteLine();
            session.Writer.WriteLine("=== ServUO Commands ===");
            session.Writer.WriteLine("Any other command will be executed as a ServUO command.");
            session.Writer.WriteLine("Examples:");
            session.Writer.WriteLine("  save");
            session.Writer.WriteLine("  shutdown");
            session.Writer.WriteLine("  add gold");
            session.Writer.WriteLine("  go britain");
            session.Writer.WriteLine();
        }

        private static void ShowStatus(TelnetSession session)
        {
            session.Writer.WriteLine("=== Server Status ===");
            session.Writer.WriteLine($"Uptime: {DateTime.Now - DateTime.Now:dd\\:hh\\:mm\\:ss}"); // Simplified since Core.StartTime not available
            session.Writer.WriteLine($"Online Players: {GetOnlinePlayerCount()}");
            session.Writer.WriteLine($"Total Mobiles: {World.Mobiles.Count}");
            session.Writer.WriteLine($"Total Items: {World.Items.Count}");
            session.Writer.WriteLine($"Memory Usage: {GC.GetTotalMemory(false) / 1024 / 1024:N0} MB");
            session.Writer.WriteLine($"Active Telnet Sessions: {_activeSessions.Count}");
            session.Writer.WriteLine();
        }

        private static int GetOnlinePlayerCount()
        {
            try
            {
                return NetState.Instances.Count;
            }
            catch
            {
                // Fallback if NetState is not accessible
                return World.Mobiles.Values.Count(m => m is PlayerMobile && m.NetState != null);
            }
        }

        private static void ShowOnlinePlayers(TelnetSession session)
        {
            var players = new List<PlayerMobile>();
            
            try
            {
                players = NetState.Instances
                    .Where(ns => ns.Mobile is PlayerMobile)
                    .Select(ns => ns.Mobile as PlayerMobile)
                    .Where(pm => pm != null)
                    .ToList();
            }
            catch
            {
                // Fallback if NetState is not accessible
                players = World.Mobiles.Values
                    .OfType<PlayerMobile>()
                    .Where(pm => pm.NetState != null)
                    .ToList();
            }

            session.Writer.WriteLine($"=== Online Players ({players.Count}) ===");
            
            if (players.Count == 0)
            {
                session.Writer.WriteLine("No players online.");
            }
            else
            {
                foreach (var player in players.OrderBy(p => p.Name))
                {
                    string location = $"{player.Location} ({player.Map})";
                    string accessLevel = player.AccessLevel > AccessLevel.Player ? $" [{player.AccessLevel}]" : "";
                    session.Writer.WriteLine($"{player.Name}{accessLevel} - {location}");
                }
            }
            session.Writer.WriteLine();
        }

        private static void ShowActiveSessions(TelnetSession session)
        {
            lock (_sessionLock)
            {
                session.Writer.WriteLine($"=== Active Telnet Sessions ({_activeSessions.Count}) ===");
                
                foreach (var s in _activeSessions.OrderBy(s => s.ConnectedAt))
                {
                    string current = s.SessionId == session.SessionId ? " (current)" : "";
                    string duration = (DateTime.Now - s.ConnectedAt).ToString(@"hh\:mm\:ss");
                    string lastActivity = (DateTime.Now - s.LastActivity).ToString(@"mm\:ss");
                    
                    session.Writer.WriteLine($"{s.SessionId}: {s.Username ?? "Not authenticated"} from {s.RemoteEndPoint}");
                    session.Writer.WriteLine($"  Connected: {duration} ago, Last activity: {lastActivity} ago{current}");
                }
            }
            session.Writer.WriteLine();
        }

        private static void BroadcastMessage(TelnetSession session, string message)
        {
            if (session.AccessLevel < AccessLevel.GameMaster)
            {
                session.Writer.WriteLine("Insufficient access level for broadcast.");
                return;
            }

            Timer.DelayCall(TimeSpan.Zero, () =>
            {
                try
                {
                    World.Broadcast(0x35, true, $"[Broadcast] {message}");
                    Console.WriteLine($"[TelnetConsole] Broadcast by {session.Username}: {message}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[TelnetConsole] Broadcast error: {ex.Message}");
                }
            });

            session.Writer.WriteLine($"Broadcast sent: {message}");
            LogMessage($"Broadcast: {session.Username}: {message}");
        }

        private static void CleanupSessions()
        {
            lock (_sessionLock)
            {
                var timedOutSessions = _activeSessions.Where(s => s.IsTimedOut).ToList();
                
                foreach (var session in timedOutSessions)
                {
                    try
                    {
                        session.Writer?.WriteLine("Session timed out. Goodbye!");
                        Console.WriteLine($"[TelnetConsole] Session {session.SessionId} timed out");
                        LogMessage($"Session timeout: {session.Username}@{session.RemoteEndPoint} (Session: {session.SessionId})");
                        session.Dispose();
                        _activeSessions.Remove(session);
                    }
                    catch { /* ignore cleanup errors */ }
                }
            }

            // Clean up old failed attempts
            var expiredAttempts = _failedAttempts
                .Where(kvp => DateTime.Now - kvp.Value.LastAttempt > TimeSpan.FromHours(1))
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var ip in expiredAttempts)
            {
                _failedAttempts.Remove(ip);
            }
        }

        private static bool IsIPLockedOut(string ip)
        {
            if (!_failedAttempts.TryGetValue(ip, out FailedAttempt attempt))
                return false;

            if (DateTime.Now < attempt.LockoutUntil)
                return true;

            // Lockout expired, reset
            if (DateTime.Now > attempt.LockoutUntil)
            {
                attempt.Count = 0;
                attempt.LockoutUntil = DateTime.MinValue;
            }

            return false;
        }

        private static void RecordFailedAttempt(string ip)
        {
            if (!_failedAttempts.TryGetValue(ip, out FailedAttempt attempt))
            {
                attempt = new FailedAttempt();
                _failedAttempts[ip] = attempt;
            }

            attempt.Count++;
            attempt.LastAttempt = DateTime.Now;

            // Lock out after max failed attempts
            if (attempt.Count >= MaxFailedAttempts)
            {
                attempt.LockoutUntil = DateTime.Now.AddMinutes(15); // 15 minute lockout
                Console.WriteLine($"[TelnetConsole] IP {ip} locked out for 15 minutes after {attempt.Count} failed attempts");
                LogMessage($"IP lockout: {ip} after {attempt.Count} failed attempts");
            }
        }

        private static string HashPassword(string password)
        {
            using (var sha256 = SHA256.Create())
            {
                byte[] hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password + "ServUOSalt"));
                return Convert.ToBase64String(hashedBytes);
            }
        }

        private static bool VerifyPassword(string password, string hash)
        {
            return HashPassword(password) == hash;
        }

        private static void LogMessage(string message)
        {
            try
            {
                string logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
                File.AppendAllText(LogPath, logEntry + Environment.NewLine);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TelnetConsole] Logging error: {ex.Message}");
            }
        }

        // Command to add new users (can be called from ServUO console)
        public static void AddUser(string username, string password, AccessLevel accessLevel)
        {
            if (_users.ContainsKey(username.ToLower()))
            {
                Console.WriteLine($"[TelnetConsole] User {username} already exists");
                return;
            }

            _users[username.ToLower()] = new UserCredentials(username, HashPassword(password), accessLevel);
            Console.WriteLine($"[TelnetConsole] Added user {username} with access level {accessLevel}");
            LogMessage($"User added: {username} ({accessLevel})");
        }

        // Command to remove users
        public static void RemoveUser(string username)
        {
            if (_users.Remove(username.ToLower()))
            {
                Console.WriteLine($"[TelnetConsole] Removed user {username}");
                LogMessage($"User removed: {username}");
            }
            else
            {
                Console.WriteLine($"[TelnetConsole] User {username} not found");
            }
        }

        // Get current status
        public static void ShowInfo()
        {
            Console.WriteLine($"[TelnetConsole] Listening on port {_port}");
            Console.WriteLine($"[TelnetConsole] Active sessions: {_activeSessions.Count}");
            Console.WriteLine($"[TelnetConsole] Registered users: {_users.Count}");
            Console.WriteLine($"[TelnetConsole] Failed attempt tracking: {_failedAttempts.Count} IPs");
        }
    }
}
