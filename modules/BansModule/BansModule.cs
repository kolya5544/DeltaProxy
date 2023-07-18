using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static DeltaProxy.ModuleHandler;
using static DeltaProxy.modules.Bans.BansModule;
using static DeltaProxy.modules.ConnectionInfoHolderModule;

namespace DeltaProxy.modules.Bans
{
    /// <summary>
    /// This is a CLIENT-side module responsible for managing bans and mutes. This module can be disabled, however it is NOT recommended, since other modules rely on it.
    /// Database is available at all times, even while module is disabled. Keep in mind this module only checks bans at re-connection, and mutes at chat attempts.
    /// </summary>
    public class BansModule
    {
        public static int CLIENT_PRIORITY = 1; // bans module has to have access to data before other modules to prevent other modules from processing banned users' data
        public static int SERVER_PRIORITY = 1;

        public static ModuleConfig cfg;
        public static Database db;
        public static List<Disconnect> disconnects = new(); // holds a list of disconnect reasons

        public static void OnEnable()
        {
            cfg = ModuleConfig.LoadConfig("mod_bans.json");
            db = Database.LoadDatabase("bans_db.json");
        }

        public static ModuleResponse ResolveServerMessage(ConnectionInfo info, string msg)
        {
            var msgSplit = msg.SplitMessage();

            if (msgSplit.Assert("QUIT", 1)) // expects someone to quit - block the message from reaching other users if it's a system quit message
            {
                var subject = msgSplit[0].ParseIdentifier();

                string nickname = subject.Item1;
                string username = subject.Item2;
                string vhost = subject.Item3;

                // first and foremost
                lock (connectedUsers) connectedUsers.RemoveAll((z) => z.Nickname == nickname);

                string quitReason = msgSplit.ToArray().Join(2).Trim(':');

                var disconnect = GetDisconnect(nickname);

                if (quitReason.Contains("DeltaProxy Forced Disconnect #")) // this is our unique signature!
                {
                    disconnect = GetDisconnect(nickname, quitReason);
                }
                if (disconnect is not null)
                {
                    var fullName = $"{nickname}!{username}@{vhost}";
                    lock (info.clientQueue) info.clientQueue.Add($":{info.GetProperNickname(fullName)} QUIT :DeltaProxy: {disconnect.reason}");
                    return ModuleResponse.BLOCK_MODULES;
                }
            }

            return ModuleResponse.PASS;
        }

        public static ModuleResponse ResolveClientMessage(ConnectionInfo info, string msg)
        {
            var msgSplit = msg.SplitMessage();

            // handle bans
            if (info.Nickname is not null && info.IP is not null && msgSplit.Assert("NICK", 0) && !info.ChangedNickname) // expecting first NICK
            {
                // removing old bans
                lock (db.lockObject) db.bans.RemoveAll((z) => IRCExtensions.Unix() - z.Issued - z.Duration >= 0);

                Punishment ban;
                lock (db.lockObject) ban = db.bans.FirstOrDefault((z) => z.IP == info.IP);

                if (ban is not null)
                {
                    info.SendClientMessage($"NOTICE * :*** DeltaProxy: {ReplacePlaceholders(ban, cfg.banConnectMessage)}");
                    ProperDisconnect(info, $"User was banned.");
                }
            }

            // handle mutes
            if (msgSplit.Assert("PRIVMSG", 0) && msgSplit.AssertCount(2, true))
            {
                // removing old mutes
                lock (db.lockObject) db.mutes.RemoveAll((z) => IRCExtensions.Unix() - z.Issued - z.Duration >= 0);

                Punishment mute;
                lock (db.lockObject) mute = db.mutes.FirstOrDefault((z) => z.IP == info.IP);

                if (mute is not null)
                {
                    info.SendClientMessage("DeltaProxy", info.Nickname, ReplacePlaceholders(mute, cfg.talkMutedMessage));
                    return ModuleResponse.BLOCK_MODULES;
                }
            }

            return ModuleResponse.PASS;
        }

        public static void ProperDisconnect(ConnectionInfo info, string reason = "Disconnected by DeltaProxy.")
        {
            var disconnectID = CreateDisconnect(info.Nickname, reason);

            // we should alert other modules of someone's KICK!! they are NOT going to get the message otherwise, because of BansModule returning FALSE in server processor!!
            var fullName = $"{info.Nickname}!{info.Username}@{info.VHost}";
            string fakeQuitMessage = $":{fullName} QUIT :DeltaProxy: {reason}"; // we'll have to create a fake quit message for other module to process

            ModuleHandler.ProcessServerMessage(info, ref fakeQuitMessage, typeof(BansModule));

            lock (info.serverQueue) info.serverQueue.Add($"QUIT :DeltaProxy Forced Disconnect #{disconnectID}");
            info.FlushServerQueue();

            info.Client.Close();
        }

        public static Disconnect? GetDisconnect(string nickname, string reason)
        {
            // remove older disconnects
            lock (disconnects) disconnects.RemoveAll((z) => IRCExtensions.Unix() - z.issued > 5);

            var extr = reason.Split('#').Last();

            long id = 0;
            bool success = long.TryParse(extr, out id);
            if (!success) return null;

            Disconnect d;
            lock (disconnects) d = disconnects.FirstOrDefault((z) => z.id == id && z.nickname == nickname);
            return d;
        }

        public static Disconnect? GetDisconnect(string nickname)
        {
            // remove older disconnects
            lock (disconnects) disconnects.RemoveAll((z) => IRCExtensions.Unix() - z.issued > 5);

            Disconnect d;
            lock (disconnects) d = disconnects.FirstOrDefault((z) => z.nickname == nickname);
            return d;
        }

        private static long CreateDisconnect(string nickname, string reason)
        {
            var z = new Disconnect()
            {
                reason = reason,
                issued = IRCExtensions.Unix(),
                id = Disconnect.currentId++,
                nickname = nickname
            };
            lock (disconnects) disconnects.Add(z);
            return z.id;
        }

        public static string ReplacePlaceholders(Punishment punishment, string msg)
        {
            return msg.Replace("{duration}", punishment.Duration is null ? punishment.Duration.ToDuration() : ((punishment.Issued + punishment.Duration) - IRCExtensions.Unix()).ToDuration())
                      .Replace("{reason}", punishment.Reason is null ? "Not specified" : punishment.Reason)
                      .Replace("{issuer}", punishment.Issuer is null ? "DeltaProxy" : punishment.Issuer);
        }

        public class Disconnect
        {
            public static long currentId = 0;

            public string reason;
            public string nickname;
            public long issued;
            public long id;
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
