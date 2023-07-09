using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static DeltaProxy.modules.ConnectionInfoHolderModule;

namespace DeltaProxy.modules
{
    /// <summary>
    /// This is a CLIENT-side module responsible for managing bans and mutes. This module can be disabled, however it is NOT recommended, since other modules rely on it.
    /// Database is available at all times, even while module is disabled. Keep in mind this module only checks bans at re-connection, and mutes at chat attempts.
    /// </summary>
    public class BansModule
    {
        public static ModuleConfig cfg;
        public static Database db;

        public static void OnEnable()
        {
            cfg = ModuleConfig.LoadConfig("mod_bans.json");
            db = Database.LoadDatabase("bans_db.json");
        }

        public static bool ResolveClientMessage(ConnectionInfo info, string msg)
        {
            var msgSplit = msg.SplitMessage();

            // handle bans
            if (info.Nickname is not null && info.IP is not null && msgSplit.Assert("NICK", 0) && !info.ChangedNickname) // expecting first NICK
            {
                // removing old bans
                lock (db.bans) db.bans.RemoveAll((z) => IRCExtensions.Unix() - z.Issued - z.Duration >= 0);

                Punishment ban;
                lock (db.bans) ban = db.bans.FirstOrDefault((z) => z.IP == info.IP);

                if (ban is not null)
                {
                    info.Writer.SendServerMessage($"NOTICE * :*** DeltaProxy: {ReplacePlaceholders(ban, cfg.banConnectMessage)}");
                    ProperDisconnect(info, $"User was banned.");
                }
            }

            // handle mutes
            if (msgSplit.Assert("PRIVMSG", 0) && msgSplit.AssertCount(2, true))
            {
                // removing old mutes
                lock (db.mutes) db.mutes.RemoveAll((z) => IRCExtensions.Unix() - z.Issued - z.Duration >= 0);

                Punishment mute;
                lock (db.mutes) mute = db.mutes.FirstOrDefault((z) => z.IP == info.IP);

                if (mute is not null)
                {
                    info.Writer.SendServerMessage("DeltaProxy", info.Nickname, ReplacePlaceholders(mute, cfg.talkMutedMessage));
                    return false;
                }
            }

            return true;
        }

        public static void ProperDisconnect(ConnectionInfo info, string reason = "Disconnected by DeltaProxy.")
        {
            info.ServerWriter.WriteLine($"QUIT :{reason}");
            info.Client.Close();
        }

        public static string ReplacePlaceholders(Punishment punishment, string msg)
        {
            return msg.Replace("{duration}", punishment.Duration is null ? punishment.Duration.ToDuration() : ((punishment.Issued + punishment.Duration) - IRCExtensions.Unix()).ToDuration())
                      .Replace("{reason}", punishment.Reason is null ? "Not specified" : punishment.Reason)
                      .Replace("{issuer}", punishment.Issuer is null ? "DeltaProxy" : punishment.Issuer);
        }


        public class ModuleConfig : ConfigBase<ModuleConfig>
        {
            public bool isEnabled = true;
            public string talkMutedMessage = "! <-> You cannot chat! Your punishment will expire in {duration}. Mute reason: {reason} by {issuer} <-> !";
            public string banConnectMessage = "! <-> You cannot connect! You are banned for {duration}. Ban reason: {reason} by {issuer} <-> !";
        }

        public class Database : DatabaseBase<Database>
        {
            public List<Punishment> mutes = new();
            public List<Punishment> bans = new();
        }

        public class Punishment
        {
            public string IP;
            public long Issued;

            [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
            public int? Duration;
            [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
            public string? Reason;
            [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
            public string? Issuer;
        }
    }
}
