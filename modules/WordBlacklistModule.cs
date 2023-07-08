using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data.SqlTypes;
using System.Linq;
using System.Runtime;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using static DeltaProxy.modules.ConnectionInfoHolderModule;
using static DeltaProxy.modules.BansModule;
using static DeltaProxy.modules.WordBlacklistModule;

namespace DeltaProxy.modules
{
    /// <summary>
    /// This CLIENT and SERVER-SIDE module monitors for usage of blacklisted words in different messages. Use /blacklist as an admin to get more help.
    /// </summary>
    public class WordBlacklistModule
    {
        public static ModuleConfig cfg = ModuleConfig.LoadConfig("mod_wordblacklist.json");
        public static Database db = Database.LoadDatabase("wordblacklist_db.json");
        public static List<Offender> offenders = new List<Offender>();

        public static void ResolveServerMessage(ConnectionInfo info, string msg)
        {
            var msgSplit = msg.SplitMessage();

            if (cfg.checkPMs && msgSplit.Assert("376", 1) && msgSplit.AssertCount(5, true)) // expects server to end a MOTD
            {
                // now we just insert our own MOTD part at the end
                info.Writer.SendServerMessage($"372 {info.Nickname} :-");
                info.Writer.SendServerMessage($"372 {info.Nickname} :- < ! > < ! > ATTENTION! This server uses DeltaProxy with a word blacklist module that WILL analyse ALL personal messages you send against a predefined list of banned phrases. Keep that in mind when chatting, or use OTR! < ! > < ! >");
            }
        }

        public static bool ResolveClientMessage(ConnectionInfo info, string msg)
        {
            var msgSplit = msg.SplitMessage();

            // check if it's an admin command /blacklist
            if (msgSplit.Assert("BLACKLIST", 0))
            {
                lock (StaffAuthModule.authedStaff) if (!StaffAuthModule.authedStaff.Contains(info)) { info.Writer.SendServerMessage("DeltaProxy", info.Nickname, $"Access denied."); return false; }

                if (msgSplit.AssertCount(1))
                {
                    lock (db.patterns) info.Writer.SendServerMessage("DeltaProxy", info.Nickname, $"BL - There are a total of {db.patterns.Count} substrings to match.");
                    info.Writer.SendServerMessage("DeltaProxy", info.Nickname, $"BL - You can use next subcommands: help, add, remove, list.");
                }
                else if (msgSplit.AssertCount(2))
                {
                    if (msgSplit.Assert("help", 1))
                    {
                        info.Writer.SendServerMessage("DeltaProxy", info.Nickname, $"BL - help -> Shows this message");
                        info.Writer.SendServerMessage("DeltaProxy", info.Nickname, $"BL - add [<action> <duration> <warnings>] <substring> -> Adds a new substring. Actions: kick, mute, ban. Duration is in seconds. Warnings is the amount of warnings a user will get before action. Default action is ban, duration is 1 day, and 0 warnings. Example: /blacklist add This network is awful");
                        info.Writer.SendServerMessage("DeltaProxy", info.Nickname, $"BL - remove <substring> -> Removes a substring");
                        info.Writer.SendServerMessage("DeltaProxy", info.Nickname, $"BL - list -> Lists all substrings");
                    }
                    else if (msgSplit.Assert("list", 1))
                    {
                        List<string> messages = new List<string>();
                        lock (db.patterns) db.patterns.ForEach((z) => { messages.Add($"BL - \"{z.entry}\", ACT: {z.action}, DUR: {(z.duration is null ? "eternal" : $"{z.duration} seconds ({z.duration.ToDuration()})")}"); });

                        foreach (string s in messages)
                        {
                            info.Writer.SendServerMessage("DeltaProxy", info.Nickname, s);
                        }
                    }
                }
                else if (msgSplit.AssertCount(3))
                {
                    if (msgSplit.Assert("remove", 1))
                    {
                        string substring = msgSplit[2].Trim();
                        Database.Pattern pattern;
                        lock (db.patterns) pattern = db.patterns.FirstOrDefault((z) => z.entry.Equals(substring, StringComparison.OrdinalIgnoreCase));
                        if (pattern is null)
                        {
                            info.Writer.SendServerMessage("DeltaProxy", info.Nickname, $"BL - Entry not found!");
                        }
                        else
                        {
                            db.patterns.Remove(pattern);
                            info.Writer.SendServerMessage("DeltaProxy", info.Nickname, $"BL - Successfully removed!");
                        }
                    }
                    else if (msgSplit.Assert("add", 1))
                    {
                        string substring = msgSplit[2].Trim();
                        Database.Pattern pattern;
                        lock (db.patterns) pattern = db.patterns.FirstOrDefault((z) => z.entry.Equals(substring, StringComparison.OrdinalIgnoreCase));
                        if (pattern is not null)
                        {
                            info.Writer.SendServerMessage("DeltaProxy", info.Nickname, $"BL - Entry already exists!");
                        }
                        else
                        {
                            pattern = new() { entry = substring, action = "ban", duration = 60 * 60 * 24, warnings = 0 };
                            lock (db.patterns) db.patterns.Add(pattern);
                            info.Writer.SendServerMessage("DeltaProxy", info.Nickname, $"BL - Added entry successfully!");
                        }
                    }
                }
                else if (msgSplit.AssertCount(6, true))
                {
                    if (msgSplit.Assert("add", 1))
                    {
                        string action = msgSplit[2].ToLower();
                        string duration = msgSplit[3];
                        string warnings = msgSplit[4];
                        string entry = msgSplit.ToArray().Join(5);

                        int durationInt = 0;
                        int warningsInt = 0;

                        string[] allowedActions = new string[] { "kick", "mute", "ban" };

                        if (int.TryParse(duration, out _) && int.TryParse(warnings, out _) && !allowedActions.Contains(action))
                        {
                            entry = msgSplit.ToArray().Join(2);
                            action = "ban";
                            duration = (60 * 60 * 24).ToString();
                            warnings = "0";
                        }

                        if (!allowedActions.Contains(action)) { info.Writer.SendServerMessage("DeltaProxy", info.Nickname, $"BL - Unknown action! Actions supported are: kick, mute, ban"); return false; }

                        Database.Pattern pattern;
                        lock (db.patterns) pattern = db.patterns.FirstOrDefault((z) => z.entry.Equals(entry, StringComparison.OrdinalIgnoreCase));

                        if (pattern is not null)
                        {
                            info.Writer.SendServerMessage("DeltaProxy", info.Nickname, $"BL - Entry already exists!"); return false;
                        }

                        try
                        {
                            durationInt = int.Parse(duration);
                            warningsInt = int.Parse(warnings);
                        } catch
                        {
                            info.Writer.SendServerMessage("DeltaProxy", info.Nickname, $"BL - Incorrect duration or warnings! Integer values only are accepted."); return false;
                        }

                        if (entry.Length < cfg.minimumEntryLength)
                        {
                            info.Writer.SendServerMessage("DeltaProxy", info.Nickname, $"BL - Entry length is too short! It must be at least {cfg.minimumEntryLength} characters long to prevent false positives."); return false;
                        }

                        var ptr = new Database.Pattern()
                        {
                            entry = entry,
                            action = action,
                            duration = durationInt,
                            warnings = warningsInt
                        };
                        lock (db.patterns) db.patterns.Add(ptr);
                        info.Writer.SendServerMessage("DeltaProxy", info.Nickname, $"BL - Added entry successfully!");
                    }
                } else if (msgSplit.AssertCount(3, true) && msgSplit.Assert("add", 1))
                {
                    string substring = msgSplit.ToArray().Join(2);
                    Database.Pattern pattern;
                    lock (db.patterns) pattern = db.patterns.FirstOrDefault((z) => z.entry.Equals(substring, StringComparison.OrdinalIgnoreCase));
                    if (pattern is not null)
                    {
                        info.Writer.SendServerMessage("DeltaProxy", info.Nickname, $"BL - Entry already exists!");
                    }
                    else
                    {
                        pattern = new() { entry = substring, action = "ban", duration = 60 * 60 * 24, warnings = 0 };
                        lock (db.patterns) db.patterns.Add(pattern);
                        info.Writer.SendServerMessage("DeltaProxy", info.Nickname, $"BL - Added entry successfully!");
                    }
                }

                db.SaveDatabase();

                return false;
            }
            
            // check for actual filters
            string content = null;
            if ((cfg.checkChannels && msgSplit.Assert("PRIVMSG", 0) && msgSplit.AssertCount(2, true) && msgSplit[1].StartsWith("#")) || // expects a public message
                (cfg.checkPMs && msgSplit.Assert("PRIVMSG", 0) && msgSplit.AssertCount(2, true) && msgSplit[1].StartsWith("#"))) // expects a private message
            {
                content = msg.GetLongString();
            }
            if (string.IsNullOrEmpty(content)) return true;

            // check if any patterns match
            Database.Pattern ptrn;
            lock (db.patterns) ptrn = db.patterns.FirstOrDefault((z) => SanitizeCompare(content, z.entry));
            if (ptrn is null) return true;
            return CheckAction(info, ptrn);
        }

        private static bool CheckAction(ConnectionInfo info, Database.Pattern ptrn)
        {
            Offender offender;
            lock (offenders)
            {
                offender = offenders.FirstOrDefault((z) => z.Client == info.Client);
                if (offender is null) { offender = new(info); offenders.Add(offender); }
            }

            // check if this pattern has any specific warning conditions
            if (ptrn.warnings > 0)
            {
                var warnings = 0;
                lock (offender.warnings)
                {
                    var attempt = offender.warnings.TryGetValue(ptrn, out warnings);
                    if (!attempt) { offender.warnings.Add(ptrn, 0); }
                }

                if (warnings == ptrn.warnings)
                {
                    return TakeAction(info, ptrn);
                } else
                {
                    warnings += 1;
                    lock (offender.warnings) offender.warnings[ptrn] = warnings;
                    info.Writer.SendServerMessage("DeltaProxy", info.Nickname, ReplacePlaceholders(info, ptrn, cfg.warningMessage, warnings));
                    return false;
                }
            }
            // else, just take action
            return TakeAction(info, ptrn);
        }

        private static bool TakeAction(ConnectionInfo info, Database.Pattern ptrn)
        {
            // possible actions: kick (server disconnect), mute (prevents a person from sending any messages), ban (prevents connection using this IP address + disconnects the user)
            
            if (ptrn.action == "kick")
            {
                info.Writer.SendServerMessage("DeltaProxy", info.Nickname, cfg.kickMessage);
            } else if (ptrn.action == "mute")
            {
                var mute = new Punishment() { Duration = ptrn.duration, IP = info.IP, Issued = IRCExtensions.Unix() };
                lock (BansModule.db.mutes) BansModule.db.mutes.Add(mute);

                info.Writer.SendServerMessage("DeltaProxy", info.Nickname, ReplacePlaceholders(info, ptrn, cfg.muteMessage));
                BansModule.db.SaveDatabase();
            } else if (ptrn.action == "ban")
            {
                var ban = new Punishment() { Duration = ptrn.duration, IP = info.IP, Issued = IRCExtensions.Unix() };
                lock (BansModule.db.bans) BansModule.db.bans.Add(ban);

                info.Writer.SendServerMessage("DeltaProxy", info.Nickname, ReplacePlaceholders(info, ptrn, cfg.banMessage));
                BansModule.ProperDisconnect(info, $"Banned for rules violation.");
                BansModule.db.SaveDatabase();
            }
            return false;
        }

        public static string ReplacePlaceholders(ConnectionInfo info, Database.Pattern ptrn, string msg, int warnings = 0)
        {
            return msg.Replace("{warnings}", warnings.ToString())
                      .Replace("{max_warnings}", ptrn.warnings.ToString())
                      .Replace("{duration}", ptrn.duration.ToDuration())
                      .Replace("{pattern}", ptrn.entry);
        }

        /// <summary>
        /// Composes a list of all common forms of text obfuscation like color codes and unprintable characters
        /// </summary>
        /// <param name="t"></param>
        /// <returns></returns>
        private static List<string> Sanitize(string t)
        {
            t = t.ToLower();
            var results = new List<string>();
            t = Regex.Replace(t, @"[\x02\x1F\x0F\x16]|\x03(\d\d?(,\d\d?)?)?", string.Empty);
            results.Add(t);
            t = Regex.Replace(t, @"[^\u0000-\u007F]+", string.Empty);
            results.Add(t);
            t = t.Replace("?", "");
            results.Add(t);
            t = t.Replace(" ", "");
            results.Add(t);

            return results;
        }

        public static bool SanitizeCompare(string message, string pattern)
        {
            var s = Sanitize(message);

            if (s.Any(x => x.Contains(pattern.ToLower()))) return true;
            return false;
        }

        public class Offender : ConnectionInfo
        {
            public Offender(ConnectionInfo ci)
            {
                Nickname = ci.Nickname;
                IP = ci.IP;
                Client = ci.Client;
            }

            public Dictionary<Database.Pattern, int> warnings = new();
        }

        public class ModuleConfig : ConfigBase<ModuleConfig>
        {
            public bool isEnabled = true;
            public bool checkChannels = true;
            public bool checkPMs = false; // setting this to true will add a new line to MOTD stating this server monitors your PMs.

            public int minimumEntryLength = 3;

            public string warningMessage = "! ! ! <-> Your message was not sent! It violates the filters set by admins. Warnings: {warnings} (out of {max_warnings}) <-> ! ! !";
            public string kickMessage = "! ! ! <-> Your message violates the filters set by admins. As a punishment, you'll be disconnected from server. <-> ! ! !";
            public string muteMessage = "! ! ! <-> Your message violates the filters set by admins. Your rights to talk were revoked for {duration}. <-> ! ! !";
            public string banMessage = "! ! ! <-> Your message violates the filters set by admins. You are now banned for {duration}. <-> ! ! !";
        }

        public class Database : DatabaseBase<Database>
        {
            public List<Pattern> patterns = new();

            public class Pattern
            {
                public string entry;
                public string action;

                [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
                public int? warnings = null;
                [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
                public int? duration = null;
            }
        }
    }
}
