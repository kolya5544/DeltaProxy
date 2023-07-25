using DeltaProxy;
using static DeltaProxy.ModuleHandler;
using static DeltaProxy.modules.ConnectionInfoHolderModule;

namespace AnopeModule
{
    public class AnopeModule
    {
        public static ModuleConfig cfg;
        public static Dictionary<ConnectionInfo, AnopeStatus> users;

        public static void OnEnable()
        {
            cfg = ModuleConfig.LoadConfig("mod_anope.json");
        }

        public static ModuleResponse ResolveClientMessage(ConnectionInfo info, string msg)
        {
            var msgSplit = msg.SplitMessage();

            if (msgSplit.Assert("NICK", 0)) // starting point
            {
                lock (users) if (!users.ContainsKey(info)) users.Add(info, AnopeStatus.Unknown);
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
                if (msg.Contains("This nickname is registered")) // how do I handle different languages?!?! scrap this idea
                {
                    lock (users) users[info] = AnopeStatus.RegisteredNotAuth;
                }
            }

            if (msgSplit.AssertCount(3, true) && msgSplit.Assert("MODE", 1) && msgSplit[0].StartsWith(":NickServ") && cfg.anopePresent) // NickServ changing MODE of another person
            {
                string newMode = msgSplit.Last().Trim(':');
                lock (users) users[info] = newMode.StartsWith("+") ? AnopeStatus.RegisteredAuth : AnopeStatus.RegisteredNotAuth;
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
        }
    }
}