﻿using DeltaProxy;
using System.Text.RegularExpressions;
using static DeltaProxy.modules.ConnectionInfoHolderModule;

namespace PronounsModule
{
    /// <summary>
    /// This is a CLIENT & SERVER-side module that implements pronouns in /WHOIS. It acts as an example module for DeltaProxy, because it implements
    /// all the features a proper module should implement.
    /// </summary>
    public class PronounsModule
    {
        // It is important to set priorities for your actions. If you're running network-critical code, like checking for bans, doing connection establishment and
        // other things, this value should be lower. However 0 is reserved for ConnectionInfoHolderModule, and 1 is reserved for BansModule. Feel free to use any
        // other values, or default value. You can initialize them here
        public static int CLIENT_PRIORITY = int.MaxValue; 
        public static int SERVER_PRIORITY = int.MaxValue;

        // if you want to have any configuration files for your module, you HAVE to create a public static ModuleConfig variable called `cfg`.
        public static ModuleConfig cfg;
        // for databases, use Database class. Do NOT initialize ANY non-constant variables in here - do it in OnEnable() instead.
        public static Database db;

        /// <summary>
        /// This code will get ran for every message sent FROM server TO client.
        /// </summary>
        /// <param name="info">Information about what client is going to RECEIVE the message sent from server</param>
        /// <param name="msg">Raw contents of the message sent from server</param>
        public static void ResolveServerMessage(ConnectionInfo info, string msg)
        {
            var msgSplit = msg.SplitMessage(); // this method helps us break the message apart into simpler parts

            // we are waiting for a message that looks like this: :{hostname} 319 {info.Nickname} {someone's nickname} :~@#chat
            if (msgSplit.Assert("319", 1) && msgSplit.AssertCount(4, true)) // asserts that index 1 value is 319 - whois channels code. We will add our reply before there.
            {
                string subject = msgSplit[3].Trim();

                // check if subject has pronouns
                Database.Pronouns pronouns;
                lock (db.pronouns) pronouns = db.pronouns.FirstOrDefault((z) => z.Nickname == subject);

                if (pronouns is null) return;

                // SendClientMessage(string) sends a string in this form: :{hostname} {message}
                info.SendClientMessage($"371 {info.Nickname} {subject} :Preferred pronouns: {pronouns.PronounsText}"); // we use a WHOIS code to send our own WHOIS reply.
            }
        }

        /// <summary>
        /// This code will get ran for every message sent FROM client TO server.
        /// </summary>
        /// <param name="info"></param>
        /// <param name="msg"></param>
        /// <returns>If false, server will not receive this message, and execution of other modules will not conclude</returns>
        public static bool ResolveClientMessage(ConnectionInfo info, string msg)
        {
            var msgSplit = msg.SplitMessage(); // this method helps us break the message apart into simpler parts

            if (msgSplit.Assert("PRONOUNS", 0) && info.Nickname is not null) // here we make sure user executed /PRONOUNS, as well as authed with NICK before.
            {
                if (msgSplit.AssertCount(1)) // this is the same as msgSplit.Count == 1. You can use any form of this expression you like.
                {
                    info.SendClientMessage("DeltaProxy", info.Nickname, $"[Pronouns] - To set a pronouns, use /PRONOUNS [pronouns], or /PRONOUNS clear. " +
                        $"Keep in mind it can be no longer than {cfg.maxLength} characters and can only contain alphanumeric characters and / (slash)");
                } else if (msgSplit.AssertCount(2, true)) // this is the same as msgSplit.Count >= 2
                {
                    string pronouns = msgSplit.ToArray().Join(1); // .Join here allows us to take all values from index 1 till end. It is an extension method.

                    Regex rg = new Regex("^[a-zA-Z][a-zA-Z0-9/]*$"); // alphanumeric + /

                    if (!rg.IsMatch(pronouns) || pronouns.Length > cfg.maxLength) {
                        info.SendClientMessage("DeltaProxy", info.Nickname, $"[Pronouns] - Pronouns can only contain alphanumeric characters and / (slash), " +
                            $"and can be no longer than {cfg.maxLength} characters.");

                        return false; // we use false because we don't want server to tell us /PRONOUNS doesn't exist.
                    }

                    if (pronouns == "clear")
                    {
                        lock (db.pronouns) db.pronouns.RemoveAll((z) => z.Nickname == info.Nickname);
                        info.SendClientMessage("DeltaProxy", info.Nickname, $"[Pronouns] - Successfully cleared your pronouns field!");
                        db.SaveDatabase(); // don't forget to save the database!!
                        return false;
                    }

                    Database.Pronouns pn;
                    lock (db.pronouns) pn = db.pronouns.FirstOrDefault((z) => z.Nickname == info.Nickname); // checking if such record already exists
                    if (pn is null)
                    {
                        pn = new Database.Pronouns() { Nickname = info.Nickname, PronounsText = pronouns };
                        lock (db.pronouns) db.pronouns.Add(pn);
                    } else
                    {
                        pn.PronounsText = pronouns;
                    }

                    info.SendClientMessage("DeltaProxy", info.Nickname, $"[Pronouns] - Successfully changed pronouns to {pn.PronounsText}!");
                    db.SaveDatabase(); // don't forget to save the database!!
                }

                return false;
            }

            return true; // we didn't match any of our code - just pass it to other modules!
        }
        
        /// <summary>
        /// Code here will be ran ONCE on module load, regardless of whether or not a module is enabled in a config file.
        /// </summary>
        public static void OnEnable()
        {
            cfg = ModuleConfig.LoadConfig("mod_pronouns.json");
            db = Database.LoadDatabase("pronouns_db.json");
        }

        /// <summary>
        /// Module config. This is where you can define all the configurable properties your module will have.
        /// </summary>
        public class ModuleConfig : ConfigBase<ModuleConfig>
        {
            public bool isEnabled = true;
            public int maxLength = 16;
        }

        /// <summary>
        /// Module database. This is where you can store any values you want to persist between reloads and restarts.
        /// </summary>
        public class Database : DatabaseBase<Database>
        {
            public List<Pronouns> pronouns = new();

            /// <summary>
            /// You can define any data structure within the database. Newtonsoft.JSON is used for databases, which makes it possible to save complex structures.
            /// </summary>
            public class Pronouns
            {
                public string Nickname;
                public string PronounsText;
            }
        }
    }
}