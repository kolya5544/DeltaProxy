using static DeltaProxy.ModuleHandler;
using static DeltaProxy.modules.ConnectionInfoHolderModule;
using DeltaProxy.modules.MessageBacklog;

namespace DeltaProxy.modules.AnonymousChannel
{
    public class AnonymousChannelModule
    {
        public static ModuleConfig cfg;
        public static Database db;

        private static List<ConnectionInfo> _channelParticipants;
        public static MessageBacklogModule.Database.BacklogChannel backlogChannel;

        public static List<ConnectionInfo> channelParticipants
        {
            get
            {
                lock (_channelParticipants) _channelParticipants.RemoveAll((z) => z.Terminated);
                return _channelParticipants;
            }
        }


        public static ModuleResponse ResolveClientMessage(ConnectionInfo info, ref string msg)
        {
            var msgSplit = msg.SplitMessage(); // this method helps us break the message apart into simpler parts\

            if (info.Nickname is null || info.Username is null) return ModuleResponse.PASS; // don't let non-registered connections through!

            if (msgSplit.Assert("JOIN", 0) && msgSplit[1].HasChannel(cfg.channelName)) // expects a JOIN attempt for channel
            {
                // preserve the channel list for multichannel joins like JOIN #anon,#chat
                msg = msg.PreserveMultiChannel(cfg.channelName);

                lock (channelParticipants)
                {
                    if (!channelParticipants.Contains(info)) channelParticipants.Add(info);
                }
                info.SendRawClientMessage($"{IRCExtensions.GetTimeString(info)}:{info.GetProperNickname()} JOIN {cfg.channelName} {info.Nickname} :{info.Realname}");
                MessageBacklogModule.PlaybackChannelHistory(info, cfg.channelName);
                return string.IsNullOrEmpty(msg) ? ModuleResponse.BLOCK_PASS : ModuleResponse.PASS; // we should actually pass it, but only if there's a channel
            }
            if (msgSplit.Assert("PRIVMSG", 0) && msgSplit.Assert(cfg.channelName, 1) && channelParticipants.Contains(info))
            {
                string userMsg = msg.GetLongString();

                int dbId = db.lastMessageID++;
                string sender = $"{dbId}!{dbId}@bot";
                lock (channelParticipants)
                {
                    channelParticipants.ForEach((z) =>
                    {
                        z.SendRawClientMessage($"{IRCExtensions.GetTimeString(info)}:{z.GetProperNickname(sender)} PRIVMSG {cfg.channelName} :{userMsg}", false);
                        z.FlushClientQueueAsync();
                    });
                }

                backlogChannel.AddMessageSafely(sender, userMsg);
                db.SaveDatabase();
                return ModuleResponse.BLOCK_PASS;
            }
            if (msgSplit.Assert("PART", 0) && msgSplit[1].HasChannel(cfg.channelName))
            {
                // preserve the channel list for multichannel joins like PART #anon,#chat
                msg = msg.PreserveMultiChannel(cfg.channelName);

                lock (channelParticipants) channelParticipants.Remove(info);
                info.SendRawClientMessage($"{IRCExtensions.GetTimeString(info)}:{info.GetProperNickname()} PART {cfg.channelName} :");
                return string.IsNullOrEmpty(msg) ? ModuleResponse.BLOCK_PASS : ModuleResponse.PASS; // we should actually pass it, but only if there's a channel left
            }

            return ModuleResponse.PASS;
        }

        public static void OnEnable()
        {
            cfg = ModuleConfig.LoadConfig("mod_anonymousChannel.json");
            db = Database.LoadDatabase("anonChan_db.json");
            _channelParticipants = new List<ConnectionInfo>();
            backlogChannel = MessageBacklogModule.AcquireChannel(cfg.channelName);
        }

        public class ModuleConfig : ConfigBase<ModuleConfig>
        {
            public bool isEnabled = true;
            public string channelName = "#anon";

            public bool useBacklog = true; // uses backlog using MessageBacklogModule
            public long backlogStoreTime = 3600 * 120;
            public long backlogStoreAmount = 100;
        }
        public class Database : DatabaseBase<Database>
        {
            public int lastMessageID = 0;
        }
    }
}