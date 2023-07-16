using DeltaProxy;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;

namespace DeltaProxy.modules
{
    /// <summary>
    /// This module keeps track of connections: it stores current connection info (nickname, username, IP, VHost), as well as keeps track of when last connection with the same nickname+username pair occured.
    /// It does NOT keep track of auth, and it is NOT to be relied on for auth. For auth, please check auth module.
    /// This module is both SERVER and CLIENT-side and is ALWAYS enabled.
    /// It is also the only BUILT-IN module, since ModuleHandle relies on it.
    /// </summary>
    public class ConnectionInfoHolderModule
    {
        public static int CLIENT_PRIORITY = 0; // we want connectioninfo module to catch everything first-hand
        public static int SERVER_PRIORITY = 0;

        public static List<ConnectionInfo> connectedUsers;
        public static ModuleConfig cfg;
        public static Database db;

        public static Dictionary<string, List<ConnectionInfo>> channelUsers;

        public static void OnEnable()
        {
            cfg = ModuleConfig.LoadConfig("mod_conninfo.json");
            db = Database.LoadDatabase("conninfo_db.json");

            connectedUsers = new List<ConnectionInfo>();
            channelUsers = new Dictionary<string, List<ConnectionInfo>>();
        }

        public static void ResolveServerMessage(ConnectionInfo info, string msg)
        {
            var msgSplit = msg.SplitMessage();

            if (msgSplit.Assert("396", 1) && msgSplit.AssertCount(5, true)) // expects server to set a VHost
            {
                info.VHost = msgSplit[3];
                if (info.stored is not null) info.stored.VHost = info.VHost;
            }
            if (msgSplit.Assert("433", 1) && msgSplit.AssertCount(4, true)) // expects server to decline an already taken nickname
            {
                info.Nickname = info._oldNickname;
            }
            if (msgSplit.AssertCorrectPerson(info) && msgSplit.Assert("JOIN", 1)) // expects a join confirmation
            {
                string channelName = msgSplit[2].Trim(':');

                lock (channelUsers) { if (channelUsers.ContainsKey(channelName)) { channelUsers[channelName].Add(info); } else { channelUsers.Add(channelName, new List<ConnectionInfo>() { info }); } }
                lock (info.Channels) info.Channels.Add(channelName);
            }
            if (msgSplit.AssertCorrectPerson(info) && msgSplit.Assert("PART", 1)) // expects a part confirmation
            {
                string channelName = msgSplit[2];

                RemoveUserFromChannel(info, channelName);
            }

            // :kolya!Kolya@iktm-FA239AD0.nk.ax KICK #chat-ru kolya123 :kolya
            if (msgSplit.AssertCorrectPerson(info) && msgSplit.Assert("KICK", 1) && msgSplit.AssertCount(4, true)) // expects a kick confirmation
            {
                string channelName = msgSplit[2];
                string kickedUser = msgSplit[3];

                ConnectionInfo usertoKick = null;
                lock (connectedUsers) usertoKick = connectedUsers.FirstOrDefault((z) => z.Nickname == kickedUser);
                RemoveUserFromChannel(usertoKick, channelName);
            }
        }

        public static bool ResolveClientMessage(ConnectionInfo info, string msg)
        {
            var msgSplit = msg.SplitMessage();
            if (msgSplit.Assert("NICK", 0) && msgSplit.AssertCount(2)) // Expects NICK command from user
            {
                info._oldNickname = info.Nickname;
                if (info.Nickname is not null) info.ChangedNickname = true;
                info.Nickname = msgSplit[1];
            }
            if (info.Username is null && msgSplit.Assert("USER", 0) && msgSplit.AssertCount(2, true)) // Expects USER command from user, but only once
            {
                info.Username = msgSplit[1];

                if (msgSplit.AssertCount(5, true)) // expect USER command with actual realname
                {
                    info.Realname = msg.GetLongString();
                }
            }
            if (msgSplit.Assert("SETNAME", 0))
            {
                info.Realname = msgSplit.ToArray().Join(1);
            }

            if (info.stored is not null) // don't forget to update the time spent total!!
            {
                var currentTime = IRCExtensions.UnixMS();
                info.stored.TimeSpentTotal += currentTime - info.LastMessage;
                info.LastMessage = currentTime;
            }

            if (info.stored is null && info.Nickname is not null && info.Username is not null) // store the connection once Nickname, Username (and realname if present) are acquired
            {
                info.stored = new()
                {
                    IP = info.IP,
                    LastConnection = info.ConnectionTimestamp,
                    Nickname = info.Nickname,
                    Username = info.Username,
                    TimeSpentTotal = IRCExtensions.UnixMS() - info.ConnectionTimestamp * 1000,
                    Realname = info.Realname
                };
            }
            if (info.Nickname is not null && info.Username is not null && !info.init)
            {
                info.init = true;
                lock (connectedUsers) connectedUsers.Add(info); // keep track of that
            }

            if (msgSplit.Assert("CAP", 0) && msgSplit.Assert("REQ", 1))
            {
                string capabilities = msg.GetLongString();
                info.capabilities = capabilities.Split(' ').ToList();
            }

            return true;
        }

        public static void RemoveUserFromChannel(ConnectionInfo user, string channel)
        {
            lock (channelUsers)
            {
                if (channelUsers.ContainsKey(channel))
                {
                    channelUsers[channel].Remove(user);
                    if (channelUsers[channel].Count == 0)
                    {
                        channelUsers.Remove(channel);
                    }
                }
            }
            lock (user.Channels) user.Channels.Remove(channel);
        }

        public class ModuleConfig : ConfigBase<ModuleConfig>
        {
            // this module is ALWAYS enabled.
            public bool storeConnections = true; // whether or not we should store past connections to keep track of stuff like last connection, seen IPs and stuff.
            public bool storeIPs = false; // store past IP addresses?
        }

        public class Database : DatabaseBase<Database>
        {
            public List<StoredConnection> userConnections = new();
        }

        public class StoredConnection
        {
            public string Nickname;
            public string Username;
            public string IP;
            public string VHost;
            public string Realname;

            public long LastConnection;
            public long TimeSpentTotal;
        }

        public class ConnectionInfo
        {
            public string Nickname;
            public string Username;
            public string IP;
            public string VHost;
            public string Realname;
            
            public string _oldNickname = null;

            public List<string> Channels = new(); // keeps track of what channels a user is part of

            public bool init = false;

            public TcpClient Client;
            public StreamWriter Writer; // not recommended to use
            public Stream Stream;

            public StreamWriter ServerWriter;

            public long ConnectionTimestamp;
            public long LastMessage;

            private StoredConnection _cached;

            public bool ChangedNickname = false;

            public List<string> serverQueue = new List<string>();
            public List<string> postServerQueue = new List<string>(); 
            public List<string> clientQueue = new List<string>(); // non-post queues send messages BEFORE the initial message
            public List<string> postClientQueue = new List<string>(); // post queues are used to send a message AFTER the initial processed message

            public bool RemoteBlockServer = false; // you can set these to remotely block further execution of code on SERVER module side
            public bool RemoteBlockClient = false; // or CLIENT module side. This flag is being reset after every message received.

            public List<string> capabilities = new();

            public X509Certificate? clientCert;

            public void FlushServerQueue()
            {
                FlushQueue(ServerWriter, serverQueue);
            }
            public void FlushClientQueue()
            {
                FlushQueue(Writer, clientQueue);
            }

            public void FlushPostServerQueue()
            {
                FlushQueue(ServerWriter, postServerQueue);
            }
            public void FlushPostClientQueue()
            {
                FlushQueue(Writer, postClientQueue);
            }

            public void FlushQueue(StreamWriter sw, List<string> queue)
            {
                try
                {
                    lock (queue) { queue.ForEach((z) => { sw.WriteLine(z); /*Program.Log($"!! {z}");*/ }); queue.Clear(); }
                }
                catch { }
            }

            public StoredConnection stored
            {
                get
                {
                    if (_cached is not null) return _cached;
                    lock (db.lockObject)
                    {
                        var val = db.userConnections.LastOrDefault((z) => z.Nickname == Nickname && z.Username == Username);
                        if (val is not null) _cached = val;
                        return val;
                    }
                }
                set
                {
                    _cached = null;
                    lock (db.lockObject) db.userConnections.Add(value);
                    db.SaveDatabase();
                }
            }
        }
    }
}