using DeltaProxy;
using static DeltaProxy.ModuleHandler;
using static DeltaProxy.modules.ConnectionInfoHolderModule;

namespace DeltaProxy.modules
{
    public class AnopeModule
    {
        public static int CLIENT_PRIORITY = 2; // after bansmodule
        public static int SERVER_PRIORITY = 2;

        public static ModuleConfig cfg;
        public static Dictionary<ConnectionInfo, AnopeStatus> users;

        public static void OnEnable()
        {
            cfg = ModuleConfig.LoadConfig("mod_anope.json");
            users = new Dictionary<ConnectionInfo, AnopeStatus>();
        }

        public static AnopeStatus GetStatus(ConnectionInfo info)
        {
            if (!users.ContainsKey(info)) return AnopeStatus.Unknown;
            return users[info];
        }

        public static ModuleResponse ResolveClientMessage(ConnectionInfo info, string msg)
        {
            var msgSplit = msg.SplitMessage();

            if (msgSplit.Assert("NICK", 0) && msgSplit.AssertCount(2)) // starting point
            {
                lock (users)
                {
                    if (!users.ContainsKey(info))
                    {
                        users.Add(info, cfg.anopePresent ? AnopeStatus.Unknown : AnopeStatus.Unregistered);
                    } else
                    {
                        string newNickname = msgSplit[1];
                        users[info] = AnopeStatus.Unknown; // we don't know what user is up to!
                        lock (info.postServerQueue) info.postServerQueue.Add($"PRIVMSG NickServ STATUS {newNickname}");
                    }
                }
            }

            return ModuleResponse.PASS;
        }

        public static ModuleResponse ResolveServerMessage(ConnectionInfo info, string msg)
        {
            var msgSplit = msg.SplitMessage();

            if (info.Nickname is not null && msgSplit.Assert($":{info.Nickname}", 0) && msgSplit.Assert("MODE", 1)) // expect MODE set by server (acts as a trigger for successful connection)
            {
                lock (info.serverQueue) info.serverQueue.Add($"PRIVMSG NickServ STATUS {info.Nickname}"); // then remove the answer (TODO)
            }

            if (msgSplit.AssertCount(4, true) && msgSplit.Assert("NOTICE", 1) && msgSplit[0].StartsWith(":NickServ") && cfg.anopePresent) // expect a message from NickServ
            {
                string realMsg = msgSplit.ToArray().Join(3).Trim(':');
                var rSplt = realMsg.SplitMessage();
                if (realMsg.StartsWith("STATUS") && rSplt.AssertCount(3, true))
                {
                    bool block = users[info] == AnopeStatus.Unknown;

                    string nickname = rSplt[1];
                    if (info.Nickname != nickname) return ModuleResponse.PASS;
                    string status = rSplt[2];

                    lock (users)
                    {
                        switch (status)
                        {
                            case "0":
                                users[info] = AnopeStatus.Unregistered; break;
                            case "1":
                                users[info] = AnopeStatus.RegisteredNotAuth; break;
                            case "2":
                            case "3":
                                users[info] = AnopeStatus.RegisteredAuth; break;
                        }
                    }

                    if (block) return ModuleResponse.BLOCK_MODULES;
                }
            }

            if (msgSplit.AssertCount(3, true) && msgSplit.Assert("MODE", 1) && msgSplit[0].StartsWith(":NickServ") && cfg.anopePresent) // NickServ changing MODE of another person
            {
                string newMode = msgSplit.Last().Trim(':');
                lock (users) users[info] = newMode.StartsWith(cfg.nickServIdentifiedMode) ? AnopeStatus.RegisteredAuth : AnopeStatus.RegisteredNotAuth;
            }

            if (msgSplit.Assert("NICK", 2) && msgSplit.AssertCount(3)) // server-side NICK change expected
            {
                users[info] = AnopeStatus.Unknown; // uncertainty.
                lock (info.postServerQueue) info.postServerQueue.Add($"PRIVMSG NickServ STATUS {info.Nickname}");
            }

            return ModuleResponse.PASS;
        }

        public static void OnDisable() { }

        public enum AnopeStatus
        {
            Unknown, Unregistered, RegisteredNotAuth, RegisteredAuth
        }

        public class ModuleConfig : ConfigBase<ModuleConfig>
        {
            public bool isEnabled = true;
            public bool anopePresent = true;
            public string nickServIdentifiedMode = "+r";
        }
    }
}