﻿using DeltaProxy;
using DeltaProxy.modules;
using dotVK;
using Newtonsoft.Json;
using System.Text;
using System.Text.RegularExpressions;
using VKBotExtensions;
using dotVK;
using static DeltaProxy.modules.ConnectionInfoHolderModule;
using static System.Net.Mime.MediaTypeNames;
using System.Reflection;
using System;
using EmbedIO;
using EmbedIO.WebApi;
using Swan.Logging;
using System.Reflection.Metadata;
using System.Net.Mail;

namespace VKBridgeModule
{
    /// <summary>
    /// This CLIENT and SERVER-side module allows proxy to set up an IRC to VK bridge
    /// </summary>
    public class VKBridgeModule
    {
        public static ModuleConfig cfg;
        public static Database db;
        public static VK bot;
        public static List<ConnectionInfo> bridgeMembers;
        public static List<SharedVKUserHolder> vkMembers;
        public static Dictionary<long, SharedVKUserHolder> hashedMembers;
        public static LookupCache lc;
        public static WebServer web;
        public static CancellationTokenSource cancelTokenSource;
        public static CancellationTokenSource messagesThread;
        public static CancellationTokenSource userUpdateThread;

        public static bool ResolveClientMessage(ConnectionInfo info, string msg)
        {
            var msgSplit = msg.SplitMessage();

            if (msgSplit.Assert("NICK", 0)) // expecting NICK
            {
                if (info.Nickname.StartsWith(cfg.collisionPrefix)) // cannot allow it! sorry.
                {
                    info.SendClientMessage($"NOTICE * :*** DeltaProxy: You cannot use nickname that begins with {cfg.collisionPrefix} while VKBridgeModule is active. Sorry!");
                    BansModule.ProperDisconnect(info, $"Killed for nickname collision.");
                }
            }
            if (msgSplit.Assert("PRIVMSG", 0) && msgSplit.Assert(cfg.ircChat, 1)) // expects a message in the IRC channel
            {
                string vkMessage = msg.GetLongString();

                if (vkMessage == "!ignoreme")
                {
                    bool newStatus = false;
                    lock (db.ignoredIRC)
                    {
                        if (db.ignoredIRC.Contains(info.Nickname))
                        {
                            db.ignoredIRC.Remove(info.Nickname);
                        }
                        else
                        {
                            db.ignoredIRC.Add(info.Nickname);
                            newStatus = true;
                        }
                    }
                    info.SendClientMessage("DeltaProxy", info.Nickname, $"Success! Your messages will {(newStatus ? "NO LONGER" : "now")} be seen by everyone on VK.");
                    db.SaveDatabase();
                    return false;
                }

                if (db.ignoredIRC.Contains(info.Nickname)) return false; 

                string contentsRemoved = RemoveBadChar(vkMessage);
                bot.SendMessage(cfg.vkChat, $"<{info.Nickname}> {contentsRemoved}");
            }
            if (msgSplit.Assert("PRIVMSG", 0) && msgSplit.AssertCount(3, true)) // expects a message with text
            {
                string receiver = msgSplit[1];
                string contents = msg.GetLongString();

                var actualReciever = vkMembers.FirstOrDefault((z) => z.screenName == receiver);
                if (actualReciever is null) return true;

                if (contents.Trim() == "!ignore")
                {
                    Ignore ignores;
                    bool newStatus = false;
                    lock (db.ignores) ignores = db.ignores.FirstOrDefault((z) => z.createdOnIrc == true && z.id == actualReciever.id && z.name == info.Nickname);
                    if (ignores is null)
                    {
                        newStatus = true;
                        ignores = new Ignore() { createdOnIrc = false, id = actualReciever.id, name = info.Nickname };
                        lock (db.ignores) db.ignores.Add(ignores);
                    }
                    else
                    {
                        lock (db.ignores) db.ignores.Remove(ignores);
                    }

                    info.SendClientMessage("DeltaProxy", info.Nickname, $"Successfully {(newStatus ? "BLOCKED" : "unblocked")} user {actualReciever.screenName}!");
                    db.SaveDatabase();
                    return false;
                }

                bool isIgnored = false;
                lock (db.ignores) isIgnored = db.ignores.Any((z) => z.name == info.Nickname && z.id == actualReciever.id);
                if (isIgnored)
                {
                    info.SendClientMessage("DeltaProxy", info.Nickname, $"Your message was NOT sent. You ignore, or are being ignored by the user.");
                    return false;
                }

                string attempt = bot.SendMessage(actualReciever.id, $"<{info.Nickname}> {contents}");
                if (attempt.Contains("error") && attempt.Contains("permission"))
                {
                    info.SendClientMessage("DeltaProxy", info.Nickname, $"Your message was NOT sent. A user should first send a message to the bot to be capable of receiving personal messages.");
                }
                return false;
            }
            if (msgSplit.Assert("JOIN", 0) && msgSplit[1].HasChannel(cfg.ircChat)) // expects a JOIN attempt for channel
            {
                // need to handle a specific edge case with nickname collision first
                lock (vkMembers)
                {
                    SharedVKUserHolder vm;
                    lock (vkMembers) vm = vkMembers.FirstOrDefault((z) =>
                    {
                        return z.screenName.Equals(info.Nickname, StringComparison.OrdinalIgnoreCase);
                    });

                    if (vm is not null)
                    {
                        string oldName = vm.screenName;
                        vm.screenName = $"{cfg.collisionPrefix}{vm.screenName}";
                        lock (bridgeMembers)
                        {
                            bridgeMembers.ForEach((x) => {
                                if (x.Nickname == oldName) return;
                                lock (x.clientQueue) x.clientQueue.Add($"{IRCExtensions.GetTimeString(x)}:{oldName}!{(vm.isBot ? "vkbot" : "vkuser")}@vkbridge-user NICK :{vm.screenName}");
                            });
                        }
                    }
                }
            }

            lock (bridgeMembers) bridgeMembers.ForEach((x) => x.FlushClientQueueAsync());

            return true;
        }

        public static void ResolveServerMessage(ConnectionInfo info, string msg)
        {
            var msgSplit = msg.SplitMessage();

            if (msgSplit.AssertCorrectPerson(info) && msgSplit.Assert("JOIN", 1) && msgSplit[2].HasChannel(cfg.ircChat)) // expects a bridge channel join confirmation
            {
                if (bridgeMembers.Contains(info)) return;
                lock (bridgeMembers) bridgeMembers.Add(info);
            }
            if (msgSplit.AssertCorrectPerson(info) && msgSplit.Assert("QUIT", 1)) // expects someone to quit
            {
                lock (bridgeMembers) bridgeMembers.Remove(info);
            }
            if (msgSplit.AssertCorrectPerson(info) && msgSplit.Assert("PART", 1)) // expects someone to part successfully - with a server confirmation
            {
                lock (bridgeMembers) bridgeMembers.Remove(info);
            }
            if (msgSplit.Assert("366", 1) && msgSplit[3].HasChannel(cfg.ircChat)) // expect NAMES end of list - to append our own entries
            {
                lock (vkMembers)
                {
                    vkMembers.ForEach((z) =>
                    {
                        info.SendClientMessage($"353 {info.Nickname} = {cfg.ircChat} :{(z.isBot ? "%" : (z.isOnline ? "+" : ""))}{z.screenName}!{(z.isBot ? "vkbot" : "vkuser")}@vkbridge-user");
                    });
                }
            }
        }

        public static string[] bannedWords = new string[] { "@all", "@everyone", "@online", "@здесь", "@все",
                                                            "*all", "*everyone", "*online", "*здесь", "*все" };

        public static string RemoveBadChar(string str)
        {
            foreach (string bword in bannedWords)
            {
                str = RemoveSpecialCharacters(str).Replace(bword, "<bad>");
            }
            Regex r = new Regex("(\u00a9|\u00ae|[\u2000-\u3300]|\ud83c[\ud000-\udfff]|\ud83d[\ud000-\udfff]|\ud83e[\ud000-\udfff])");
            var a = r.Matches(str);
            List<string> knownEmojis = new List<string>();

            foreach (Match gc in a)
            {
                if (!knownEmojis.Contains(gc.Value)) { knownEmojis.Add(gc.Value); }
                if (knownEmojis.Count > 14)
                {
                    str = str.Replace(gc.Value, knownEmojis[0]);
                }
            }

            return str;
        }

        public static void OnEnable()
        {
            cfg = ModuleConfig.LoadConfig("mod_vkbridge.json");
            db = Database.LoadDatabase("vkbridge_db.json");
            lc = new LookupCache();
            cancelTokenSource = new CancellationTokenSource();
            messagesThread = new CancellationTokenSource();
            userUpdateThread = new CancellationTokenSource();
            if (VKBridgeModule.cfg.isEnabled)
            {
                new Thread(() =>
                {
                    bot = new VK(VKBridgeModule.cfg.vkToken, VKBridgeModule.cfg.vkGroup);
                    bot.UpdateLongPoll();
                    bot.EnqueueUpdates(300 * 1000);

                    VKMessageProcessor.UpdateUsers(firstTime: true);

                    while (true)
                    {
                        try
                        {
                            var msg = bot.ReceiveMessage();

                            if (messagesThread.IsCancellationRequested) break;

                            foreach (Update u in msg.Updates)
                            {
                                VKMessageProcessor.HandleUpdate(u);
                            }
                        }
                        catch (Exception e)
                        {

                        }
                    }
                }).Start();

                new Thread(() =>
                {
                    while (true)
                    {
                        Thread.Sleep(20000); // update new users, online statuses etc every 20 seconds
                        if (userUpdateThread.IsCancellationRequested) break;
                        try
                        {
                            VKMessageProcessor.UpdateUsers();
                        }
                        catch
                        {

                        }
                    }
                }).Start();

                // create the cache server
                web = new WebServer(o => o
                    .WithUrlPrefix(VKBridgeModule.cfg.cacheServer)
                    .WithMode(HttpListenerMode.EmbedIO))
                .WithCors()
                .WithWebApi("/", Controller.AsText, m => m
                    .WithController<Controller>());

                try { Logger.UnregisterLogger<ConsoleLogger>(); } catch { }

                web.RunAsync(cancelTokenSource.Token);

                bridgeMembers = new List<ConnectionInfo>();
            }
        }

        public static void OnDisable()
        {
            cancelTokenSource.Cancel();
            messagesThread.Cancel();
            userUpdateThread.Cancel();
        }


        public static string RemoveSpecialCharacters(string str)
        {
            StringBuilder sb = new StringBuilder();
            foreach (char c in str)
            {
                if (!(c == '\r' || c == '\t' || c == '\0' || c == '\a' || c == '\b' || c == '\f' || c == '\v'))
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        public class SharedVKUserHolder
        {
            public string screenName;
            public string fullName; // for both bots and users
            public long id;

            public bool isBot;
            public bool isOnline;
            public long lastStatusChange;
        }

        public class ModuleConfig : ConfigBase<ModuleConfig>
        {
            public bool isEnabled = false;

            public string vkToken = "";
            public long vkGroup = 0;
            public long vkChat = 2000000001; // bridge chat
            public string vkBotName = "Bridge Bot"; // bridge channel

            public string ircChat = "#bridge"; // bridge channel
            public bool ircSpoofUsers = true; // make VK-sided users appear as IRC users
            public string collisionPrefix = "[VK]"; // add this prefix to VK users if their nickname collides with nickname of someone already on server

            public bool sendOnlineUpdatesIRC = true; // update users that are online VK on IRC side

            public bool allowOnline = true; // allow the use of /online command VK-side
            public bool allowIgnoreVK = true; // allow the use of /ignoreme command VK-side
            public bool allowIgnoreIRC = true; // allow the use of /ignoreme command IRC-side
            public bool allowUserVK = true; // allow the use of /user command VK-side
            public bool allowUserIRC = true; // allow the use of /whois command IRC-side on VK users

            public bool allowPMs = true; // allow the use of personal messages from VK to IRC and vice versa

            public bool enableURLcache = false; // shorten and store VK URLs to
            public string publicURL = "https://example.com/c/";
            public string cacheServer = "http://127.0.0.1:8818/"; // it's assumed you have a reverse proxy pointing here
            public long storageTime = 72 * 3600;
        }

        public class Database : DatabaseBase<Database>
        {
            public List<string> ignoredIRC = new();
            public List<long> ignoredVK = new();

            public List<Ignore> ignores = new();
        }

        public class Ignore
        {
            public long id;
            public string name;

            public bool createdOnIrc;
        }
    }
}