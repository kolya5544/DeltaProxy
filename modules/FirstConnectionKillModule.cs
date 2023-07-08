using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static DeltaProxy.ConnectionInfoHolderModule;

namespace DeltaProxy.modules
{
    /// <summary>
    /// This is a CLIENT-SIDE module that kills the connection for newly connected clients as a way to protect against bots.
    /// </summary>
    public class FirstConnectionKillModule
    {
        public static ModuleConfig cfg = ModuleConfig.LoadConfig("mod_firstkill.json");
        public static Database db = Database.LoadDatabase("firstkill_db.json");

        public static bool ResolveClientMessage(ConnectionInfo info, string msg)
        {
            var msgSplit = msg.SplitMessage();

            // relying on nickname + IP pair. Executed on NICK so ConnectionInfo doesn't store bot connections in the DB
            if (info.Nickname is not null && info.IP is not null && msgSplit.Assert("NICK", 0))
            { 
                var ticket = db.allowedList.LastOrDefault((z) => z.Nickname == info.Nickname && z.IPAddress == info.IP);
                if (ticket is null) // unfortunately no ticket. Create one and disconnect the user
                {
                    IssueNewTicket(info);

                    return false;
                }

                // has ticket. check if not expired. Expires in 5 minutes
                if (!ticket.didReconnect && IRCExtensions.Unix() - ticket.DateNoticed > 300)
                {
                    // issue a new ticket
                    db.allowedList.Remove(ticket);
                    IssueNewTicket(info);
                    return false;
                } else
                {
                    // in all other cases, mark ticket as successful and move on
                    if (ticket.didReconnect != true)
                    {
                        ticket.didReconnect = true;
                        db.SaveDatabase("firstkill_db.json");
                    }
                }
            }

            return true;
        }

        private static void IssueNewTicket(ConnectionInfo info)
        {
            db.allowedList.Add(new Database.AllowedEntry() { Nickname = info.Nickname, IPAddress = info.IP, DateNoticed = IRCExtensions.Unix(), didReconnect = false });

            // here we'll have to pretend to be server and send an informative message
            info.Writer.SendServerMessage($"NOTICE * :*** DeltaProxy: {cfg.KillMessage}");
            info.Client.Close();
        }

        public class Database : DatabaseBase<Database>
        {
            public class AllowedEntry
            {
                public string Nickname;
                public string IPAddress;

                public long DateNoticed;
                public bool didReconnect;
            }

            public List<AllowedEntry> allowedList = new();
        }

        public class ModuleConfig : ConfigBase<ModuleConfig>
        {
            public bool isEnabled = false;
            public string KillMessage = "Your connection is being scanned... Please reconnect!";
        }
    }
}
