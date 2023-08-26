using DeltaProxy;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using static DeltaProxy.ModuleHandler;

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

        public static Dictionary<string, List<ConnectionInfo>> channelUsers;

        public static void OnEnable()
        {
            connectedUsers = new List<ConnectionInfo>();
            channelUsers = new Dictionary<string, List<ConnectionInfo>>();
        }

        public static ModuleResponse ResolveServerMessage(ConnectionInfo info, string msg)
        {
            var msgSplit = msg.SplitMessage();

            if (msgSplit.Assert("396", 1) && msgSplit.AssertCount(5, true)) // expects server to set a VHost
            {
                info.VHost = msgSplit[3];
            }
            if (msgSplit.Assert("433", 1) && msgSplit.AssertCount(4, true)) // expects server to decline an already taken nickname
            {
                info.Nickname = info._oldNickname;
            }

            if (msgSplit.AssertCorrectPerson(info) && msgSplit.Assert("NICK", 1) && msgSplit.AssertCount(3, true)) // expects server to change your nickname
            {
                string newNickname = msgSplit[2].Trim(':');
                info.Nickname = newNickname;
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

            if (msgSplit.AssertCorrectPerson(info) && msgSplit.Assert("KICK", 1) && msgSplit.AssertCount(4, true)) // expects a kick confirmation
            {
                string channelName = msgSplit[2];
                string kickedUser = msgSplit[3];

                ConnectionInfo usertoKick = null;
                lock (connectedUsers) usertoKick = connectedUsers.FirstOrDefault((z) => z.Nickname == kickedUser);
                RemoveUserFromChannel(usertoKick, channelName);
            }

            return ModuleResponse.PASS;
        }

        public static ModuleResponse ResolveClientMessage(ConnectionInfo info, ref string msg)
        {
            var msgSplit = msg.SplitMessage();

            // handle IRCCloud IRCv3 whatever shit where commands begin with :nickname!username@vhost
            if (msgSplit.AssertCorrectPerson(info))
            {
                msg = msg.Remove(0, msgSplit.First().Length + 1); // lol
            }

            if (msgSplit.Assert("NICK", 0) && msgSplit.AssertCount(2)) // Expects NICK command from user
            {
                if (msgSplit[1].Equals("deltaproxy", StringComparison.OrdinalIgnoreCase))
                {
                    info.SendClientMessage($"NOTICE * :*** DeltaProxy: This is a reserved nickname. You cannot use it.");
                    return ModuleResponse.BLOCK_ALL;
                }

                info._oldNickname = info.Nickname;
                if (info.Nickname is not null) info.ChangedNickname = true;
                info.Nickname = msgSplit[1].Trim(':');

                // now this is important, we shouldn't let server see this message, but we should let other modules do so.
                // important for webIRC auth

                if (Program.cfg.SendUsernameOverWebIRC)
                {
                    return ModuleResponse.BLOCK_PASS;
                }
            }
            if (info.Username is null && msgSplit.Assert("USER", 0) && msgSplit.AssertCount(2, true)) // Expects USER command from user, but only once
            {
                info.Username = msgSplit[1];

                if (msgSplit.AssertCount(5, true)) // expect USER command with actual realname
                {
                    info.Realname = msg.GetLongString();
                }

                // now this is important!! We send WebIRC auth request
                // besides we should actually send both NICK & USER to server now.

                if (Program.cfg.SendUsernameOverWebIRC)
                {
                    // get IP and hostname for WebIRC

                    string ip_address = ((IPEndPoint)info.Client.Client.RemoteEndPoint).Address.ToString();
                    string hostname;
                    try
                    {
                        hostname = Dns.GetHostEntry(ip_address).HostName;
                    }
                    catch { hostname = ip_address; }

                    // WebIRC auth
                    string isSecure = info.isSSL ? "secure" : "";
                    string clientCert = !string.IsNullOrEmpty(info.clientCert) ? $" certfp-sha-256={info.clientCert}" : "";
                    if (!string.IsNullOrEmpty(clientCert)) Program.Log($"Found a client cert! {clientCert}");
                    info.ServerWriter.WriteLine($"WEBIRC {Program.cfg.serverPass} {info.Username} {hostname} {ip_address} :{isSecure}{clientCert} local-port={info.localPort} remote-port={info.remotePort}");

                    info.ServerWriter.WriteLine($"CAP LS 302");

                    // Send NICK to server.
                    info.ServerWriter.WriteLine($"NICK {info.Nickname}");

                    // Send USER to server.
                    info.ServerWriter.WriteLine($"USER {info.Username} 0 * :{info.Realname}");

                    info.WebAuthed = true;

                    return ModuleResponse.BLOCK_PASS;
                }
            }
            if (msgSplit.Assert("SETNAME", 0))
            {
                info.Realname = msgSplit.ToArray().Join(1);
            }
            if (msgSplit.Assert("438", 1)) // nickname change declined
            {
                info.Nickname = info._oldNickname;
            }

            if (!info.WebAuthed) return ModuleResponse.BLOCK_ALL;

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

            return ModuleResponse.PASS;
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

        public partial class ConnectionInfo
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

            public bool ChangedNickname = false;

            public List<string> serverQueue = new List<string>();
            public List<string> postServerQueue = new List<string>(); 
            public List<string> clientQueue = new List<string>(); // non-post queues send messages BEFORE the initial message
            public List<string> postClientQueue = new List<string>(); // post queues are used to send a message AFTER the initial processed message

            public List<string> capabilities = new();

            public string? clientCert;

            public bool isSSL;

            public int localPort_IRCd; // port PROXY uses to connect to server on client's behalf
            public int remotePort; // port user uses their side to connect to PROXY
            public int localPort; // port uses uses OUR side to connect to PROXY (like 6667)

            public bool WebAuthed = false;

            public bool Terminated = false;

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
        }
    }
}