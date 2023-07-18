using System.Threading.Tasks.Dataflow;
using static DeltaProxy.ModuleHandler;
using static DeltaProxy.modules.ConnectionInfoHolderModule;

namespace DeltaProxy.modules.MessageBacklog
{
    /// <summary>
    /// This module is responsible for backlogging PRIVMSG messages sent to channels using modules. It is a SERVER-side module. It allows modules to add
    /// messages that will never reach the server (and thus cannot be saved using traditional "history" server plug-ins), to be sent to newly connected clients.
    /// </summary>
    public class MessageBacklogModule
    {
        public static int LOAD_PRIORITY = 0; // this makes it load before any other module - so that any module using the backlog reaches it successfully

        public static ModuleConfig cfg;
        public static Database db;
        public static CancellationTokenSource dbSaveToken;

        public static ModuleResponse ResolveServerMessage(ConnectionInfo info, string msg)
        {
            List<string> msgSplit = msg.SplitMessage();

            if (msgSplit.AssertCorrectPerson(info) && msgSplit.Assert("JOIN", 1)) // expects a join confirmation
            {
                string channelName = msgSplit[2];

                Database.BacklogChannel chan;
                lock (db.lockObject) chan = db.channels.FirstOrDefault((z) => z.channel == channelName);
                if (chan is null) return ModuleResponse.PASS;

                lock (chan.messages) // send backlog if any
                {
                    var currentTime = IRCExtensions.UnixMS();
                    chan.messages.RemoveAll((z) => currentTime - z.timestamp > chan.maxStoreTime * 1000);
                    chan.messages = chan.messages.TakeLast((int)chan.maxStoreAmount).ToList();

                    lock (info.postClientQueue)
                    {
                        chan.messages.ForEach((z) =>
                        {
                            info.postClientQueue.Add($"{IRCExtensions.GetTimeString(info, z.timestamp)}:{info.GetProperNickname(z.sender)} PRIVMSG {channelName} :{z.message}");
                        });
                    }
                }
            }

            return ModuleResponse.PASS;
        }

        public static void OnEnable()
        {
            cfg = ModuleConfig.LoadConfig("mod_backlog.json");
            db = Database.LoadDatabase("backlog_db.json");
            dbSaveToken = new CancellationTokenSource();

            new Thread(() =>
            {
                while (true)
                {
                    Thread.Sleep(300000); // save all backlogged messages once per 5 minutes
                    if (dbSaveToken.IsCancellationRequested) return;
                    db.SaveDatabase();
                }
            }).Start();
        }

        public static void OnDisable()
        {
            dbSaveToken.Cancel();
            db.SaveDatabase();
        }

        public class ModuleConfig : ConfigBase<ModuleConfig>
        {
            public bool isEnabled = true;
        }
        public class Database : DatabaseBase<Database>
        {
            public List<BacklogChannel> channels = new();

            public class BacklogChannel
            {
                public string channel;
                public List<Message> messages = new();

                public long maxStoreTime = 3600 * 120;
                public long maxStoreAmount = 100;

                public void AddMessageSafely(string sender, string msg)
                {
                    var message = new Message() { message = msg, sender = sender, timestamp = IRCExtensions.UnixMS() };
                    lock (db.lockObject) messages.Add(message);
                }

                public void AddMessageSafely(ConnectionInfo sender, string msg)
                {
                    AddMessageSafely($"{sender.Nickname}!{sender.Username}@{sender.VHost}", msg);
                }
            }

            public class Message
            {
                public string sender;
                public string message;
                public long timestamp;
            }
        }
    }
}