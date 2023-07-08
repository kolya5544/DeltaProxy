using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace DeltaProxy
{
    /// <summary>
    /// This module keeps track of connections: it stores current connection info (nickname, username, IP, VHost), as well as keeps track of when last connection with the same nickname+username pair occured.
    /// It does NOT keep track of auth, and it is NOT to be relied on for auth. For auth, please check auth module.
    /// This module is both SERVER and CLIENT-side and is ALWAYS enabled.
    /// </summary>
    public class ConnectionInfoHolderModule
    {
        public static List<ConnectionInfo> connectedUsers = new List<ConnectionInfo>();
        public static ModuleConfig cfg = ModuleConfig.LoadConfig("mod_conninfo.json");
        public static Database db = Database.LoadDatabase("conninfo_db.json");

        public static void ResolveServerMessage(ConnectionInfo info, string msg)
        {
            var msgSplit = msg.SplitMessage();

            if (msgSplit.Assert("396", 1) && msgSplit.AssertCount(5, true) && msg.GetLongString().Contains("is now your displayed host")) // expects server to set a VHost
            {
                info.VHost = msgSplit[3];
                if (info.stored is not null) info.stored.VHost = info.VHost;
            }
        }

        public static bool ResolveClientMessage(ConnectionInfo info, string msg)
        {
            var msgSplit = msg.SplitMessage();
            if (msgSplit.Assert("NICK", 0) && msgSplit.AssertCount(2)) // Expects NICK command from user
            {
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
            return true;
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

            public TcpClient Client;
            public StreamWriter Writer;
            public StreamReader Reader;
            public Stream Stream;

            public long ConnectionTimestamp;
            public long LastMessage;

            private StoredConnection _cached;

            public StoredConnection stored
            {
                get
                {
                    if (_cached is not null) return _cached;
                    var val = db.userConnections.LastOrDefault((z) => z.Nickname == Nickname && z.Username == Username);
                    if (val is not null) _cached = val;
                    return val;
                }
                set
                {
                    _cached = null;
                    db.userConnections.Add(value);
                    db.SaveDatabase("conninfo_db.json");
                }
            }
        }
    }
}
