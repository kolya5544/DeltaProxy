using DeltaProxy;
using System;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using static DeltaProxy.ModuleHandler;
using static DeltaProxy.modules.ConnectionInfoHolderModule;

namespace DeltaProxy.modules.FakeWhoNamesWhois
{
    public class FakeWhoNamesWhoisModule
    {
        public static int CLIENT_PRIORITY = 2; // we want this module to catch everything essentially first lol
        public static int SERVER_PRIORITY = 2;

        public static List<WhoX> requests;
        public static List<Fields> fieldNames;
        public static List<string> fields;

        public enum Fields
        {
            Token, Channel, User, IP, Host, Server, Nick, Flags, Hopcount, Idle, Account, Oplevel, Realname, Reserved
        }

        public static ModuleResponse ResolveClientMessage(ConnectionInfo info, string msg)
        {
            var msgSplit = msg.SplitMessage();

            if (msgSplit.Assert("WHO", 0)) // expects a WHO command
            {
                if (msgSplit.AssertCount(2) || (msgSplit.AssertCount(3, true) && !msgSplit[2].Contains("%"))) return ModuleResponse.PASS; // only expect WHOX command
                if (!msg.Contains("%")) return ModuleResponse.PASS;

                // parse fields requested
                var req = new WhoX();
                req.nickname = info.Nickname;
                var rawNecessaryArgument = msgSplit.Last().Split('%').Last(); // WHO #general a%chtsunfra,152 -> chtsunfra,152

                var spl = rawNecessaryArgument.Split(',');
                if (spl.Length == 2)
                {
                    req.token = spl.Last();
                    req.fields = spl.First();
                } else
                {
                    req.fields = rawNecessaryArgument;
                }
                req.creationDate = IRCExtensions.UnixMS();

                lock (requests) requests.Add(req);
            }

            return ModuleResponse.PASS;
        }

        public static WhoX GetRequest(ConnectionInfo info)
        {
            WhoX whox;
            lock (requests)
            {
                requests.RemoveAll((z) => IRCExtensions.UnixMS() - z.creationDate >= 5000);
                whox = requests.FirstOrDefault((z) => z.nickname == info.Nickname && z.token is null);
            }
            return whox;
        }

        public static string? GetJoinField(List<string> splet, Fields fieldName)
        {
            List<Fields> fields = new List<Fields>() { Fields.Nick, Fields.User, Fields.Host, Fields.Reserved, Fields.Channel, Fields.Reserved, Fields.Realname };
            var req = fields.IndexOf(fieldName);

            if (req == -1) return null;

            var userParse = splet[0].Trim(':').ParseIdentifier();

            if (fieldName == Fields.Nick) return userParse.Item1;
            if (fieldName == Fields.User) return userParse.Item2;
            if (fieldName == Fields.Host) return userParse.Item3;

            int index = req - 2;

            return fieldName == Fields.Realname ? splet.ToArray().Join(index) : splet[index];
        }

        public static string? FakeJoinField(List<string> splet, Fields fieldName, string newValue)
        {
            List<Fields> fields = new List<Fields>() { Fields.Nick, Fields.User, Fields.Host, Fields.Reserved, Fields.Channel, Fields.Reserved, Fields.Realname };
            var req = fields.IndexOf(fieldName);

            if (req == -1) return null;

            var usString = splet[0].Trim(':');
            bool completeUser = splet[0].Contains("!");
            var userParse = usString.ParseIdentifier();

            int index = req - 2;

            switch (fieldName)
            {
                case Fields.Nick:
                    usString = $"{newValue}{(completeUser ? $"!{userParse.Item2}@{userParse.Item3}" : "")}"; break;
                case Fields.User:
                    usString = $"{userParse.Item1}{(completeUser ? $"!{newValue}@{userParse.Item3}" : "")}"; break;
                case Fields.Host:
                    usString = $"{userParse.Item1}{(completeUser ? $"!{userParse.Item2}@{newValue}" : "")}"; break;
                default:
                    splet[index] = newValue; break;
            }

            splet[0] = $":{usString}";
            if (fieldName == Fields.Realname) splet.RemoveRange(index + 1, splet.Count - (index + 1));

            return splet.ToArray().Join(0);
        }

        public static WhoX GetRequest(ConnectionInfo info, string token)
        {
            WhoX whox;
            lock (requests)
            {
                requests.RemoveAll((z) => IRCExtensions.UnixMS() - z.creationDate >= 5000);
                whox = requests.FirstOrDefault((z) => z.nickname == info.Nickname && z.token == token);
            }
            return whox;
        }

        public static string? GetWhoisUserField(ConnectionInfo info, List<string> splet, Fields fieldName)
        {
            // :irc.nk.ax 311 kolya123 kolya Kolya nk.ax * :Welcome to the deadlock of reality.

            List<Fields> fields = new List<Fields>() { Fields.Nick, Fields.User, Fields.Host, Fields.Reserved, Fields.Realname };
            var req = fields.IndexOf(fieldName);

            if (req == -1) return null;

            return fieldName == Fields.Realname ? splet.ToArray().Join(3 + req) : splet[3 + req];
        }

        public static string? GetWhoGenericField(ConnectionInfo info, List<string> splet, Fields fieldName)
        {
            if (splet.Assert("354", 1)) // WHOX
            {
                string token = splet[3];

                var req = GetRequest(info, token);
                if (req is null) req = GetRequest(info);
                if (req is null) return splet.ToArray().Join(0);

                var indx = GetWhoXFieldIndex(req, fieldName);
                if (!indx.HasValue) return null;

                string fieldVal = fieldName == Fields.Realname ? splet.ToArray().Join(3 + indx.Value) : splet[3 + indx.Value];

                return fieldVal;
            }
            else if (splet.Assert("352", 1)) // WHO
            {
                return GetWhoField(splet, fieldName);
            }
            return null;
        }

        // [token] [channel] [user] [ip] [host] [server] [nick] [flags] [hopcount] [idle] [account] [oplevel] [:realname]
        public static int? GetWhoXFieldIndex(WhoX req, Fields fieldName)
        {
            int index = fieldNames.IndexOf(fieldName);
            if (index == -1) return null;

            var indexMax = index;
            for (int i = 0; i < indexMax; i++)
            {
                string field = fields[i];
                if (!req.fields.Contains(field)) index--;
            }

            return index;
        }

        public static string? SetWhoXField(WhoX req, List<string> splet, Fields fieldName, string newValue)
        {
            var index = GetWhoXFieldIndex(req, fieldName);
            if (index is null) return splet.ToArray().Join(0);

            var ind = 3 + index.Value;
            splet[ind] = (fieldName == Fields.Realname ? ":" : "") + newValue;
            if (fieldName == Fields.Realname) splet.RemoveRange(ind + 1, splet.Count - (ind + 1));

            return splet.ToArray().Join(0);
        }

        public static string? GetWhoField(List<string> splet, Fields fieldName)
        {
            List<Fields> fieldValues = new List<Fields> { Fields.User, Fields.Host, Fields.Server, Fields.Nick, Fields.Flags, Fields.Hopcount, Fields.Realname };
            var req = fieldValues.IndexOf(fieldName);

            if (req == -1) return null;

            return fieldName == Fields.Realname ? splet.ToArray().Join(4 + req) : splet[4 + req];
        }

        public static string? SetWhoField(List<string> fields, Fields fieldName, string newValue)
        {
            List<Fields> fieldValues = new List<Fields>() { Fields.User, Fields.Host, Fields.Server, Fields.Nick, Fields.Flags, Fields.Hopcount, Fields.Realname };
            var req = fieldValues.IndexOf(fieldName);

            if (req == -1) return null;

            fields[4 + req] = (fieldName == Fields.Hopcount ? ":" : "") + newValue;
            if (fieldName == Fields.Realname) fields.RemoveRange(10, fields.Count - 10);

            return fields.ToArray().Join(0);
        }

        public static bool IsWhoResponse(List<string> splet)
        {
            return splet.AssertCount(3, true) && (splet.Assert("354", 1) || splet.Assert("352", 1)); // RPL_WHOSPCRPL or WHORPL_WHOREPLY
        }

        public static bool IsWhoisUserResponse(List<string> splet)
        {
            return splet.AssertCount(3, true) && splet.Assert("311", 1); // RPL_WHOISUSER
        }

        public static bool IsNamesResponse(List<string> splet)
        {
            return splet.AssertCount(3, true) && splet.Assert("353", 1); // RPL_NAMREPLY
        }

        public static bool IsJoinResponse(List<string> splet)
        {
            return splet.AssertCount(3, true) && splet.Assert("JOIN", 1); // JOIN
        }

        public static List<string> GetUsersFromNameResponse(ConnectionInfo info, List<string> splet)
        {
            var answ = new List<string>();

            if (splet.AssertCount(7, true)) // single-line NAMES
            {
                List<string> users = splet.GetRange(5, splet.Count - 5);

                users.ForEach((z) =>
                {
                    string u = z.Trim(':').Trim(new char[] { '~', '&', '@', '%', '+' });

                    answ.Add(u);
                });

                return answ;
            } else // multiline
            {
                string user = splet.Last().Trim(':');
                answ.Add(user);

                return answ;
            }
        }

        public static string FakeNamesField(List<string> splet, string nickname, Fields fieldName, string newValue)
        {
            // we need to handle both cases - when NAMES is single-line, and when NAMES is multi-line

            var regexp = new Regex("([:~&@%\\+]+)"); // used for matching nickname modifiers

            if (splet.AssertCount(7, true)) // single-line NAMES
            {
                List<string> users = splet.GetRange(5, splet.Count - 5);

                var user = users.FindIndex((z) =>
                {
                    string u = z.Trim(':').Trim(new char[] { '~', '&', '@', '%', '+' });
                    string nickU = u.Contains("!") ? u.Split('!').First() : u;

                    return nickU == nickname;
                });

                if (user == -1) return splet.ToArray().Join(0);

                string usString = splet[5 + user];
                string modifiers = regexp.Match(usString).Value;

                usString = usString.Trim(':').Trim(new char[] { '~', '&', '@', '%', '+' });

                var splt = usString.ParseIdentifier();

                bool completeUser = usString.Contains("!");

                switch (fieldName)
                {
                    case Fields.Nick:
                        usString = $"{modifiers}{newValue}{(completeUser ? $"!{splt.Item2}@{splt.Item3}" : "")}"; break;
                    case Fields.User:
                        usString = $"{modifiers}{splt.Item1}{(completeUser ? $"!{newValue}@{splt.Item3}" : "")}"; break;
                    case Fields.Host:
                        usString = $"{modifiers}{splt.Item1}{(completeUser ? $"!{splt.Item2}@{newValue}" : "")}"; break;
                }

                splet[5 + user] = usString;

                return splet.ToArray().Join(0);
            }
            else // multi-line NAMES like 353 {info.Nickname} = {cfg.ircChat} :{info.GetProperNickname(z)}
            {
                string user = splet.Last();

                bool completeUser = user.Contains("!");

                string modifiers = regexp.Match(user).Value;
                user = user.Trim(':').Trim(new char[] { '~', '&', '@', '%', '+' });

                var splt = user.ParseIdentifier();

                switch (fieldName)
                {
                    case Fields.Nick:
                        user = $"{modifiers}{newValue}{(completeUser ? $"!{splt.Item2}@{splt.Item3}" : "")}"; break;
                    case Fields.User:
                        user = $"{modifiers}{splt.Item1}{(completeUser ? $"!{newValue}@{splt.Item3}" : "")}"; break;
                    case Fields.Host:
                        user = $"{modifiers}{splt.Item1}{(completeUser ? $"!{splt.Item2}@{newValue}" : "")}"; break;
                }

                splet[splet.Count - 1] = user;

                return splet.ToArray().Join(0);
            }
        }

        public static string FakeWhoisField(List<string> splet, Fields fieldName, string newValue)
        {
            // :irc.nk.ax 311 kolya123 kolya Kolya nk.ax * :Welcome to the deadlock of reality.
            var fieldValues = new List<Fields>() { Fields.Nick, Fields.User, Fields.Host, Fields.Reserved, Fields.Realname };
            
            int index = fieldValues.IndexOf(fieldName);
            if (index == -1) return splet.ToArray().Join(0);

            var ind = 3 + index;
            splet[ind] = (fieldName == Fields.Realname ? ":" : "") + newValue;
            if (fieldName == Fields.Realname) splet.RemoveRange(8, splet.Count - 8);

            return splet.ToArray().Join(0);
        }

        public static string FakeWhoField(ConnectionInfo info, List<string> splet, Fields field, string newValue)
        {
            // :irc.nk.ax 354 kolya123 152 #kolya Kolya54355 iktm-A978989C irc.nk.ax kolya123 Hs 0 :This field is empty.

            if (splet.AssertCount(4, true))
            {
                if (splet.Assert("354", 1)) // WHOX
                {
                    string token = splet[3];

                    var req = GetRequest(info, token);
                    if (req is null) req = GetRequest(info);
                    if (req is null) return splet.ToArray().Join(0);

                    return SetWhoXField(req, splet, field, newValue);
                }
                else if (splet.Assert("352", 1)) // WHO
                {
                    // :irc.nk.ax 352 kolya123 * Kolya54355 iktm-A978989C irc.nk.ax kolya123 Hs :0 This field is empty.
                    return SetWhoField(splet, field, newValue);
                }
            }
            return "";
        }

        public static void OnEnable()
        {
            requests = new List<WhoX>();
            fieldNames = new List<Fields>() { Fields.Token, Fields.Channel, Fields.User, Fields.IP, Fields.Host, Fields.Server, Fields.Nick, Fields.Flags, Fields.Hopcount, Fields.Idle, Fields.Account, Fields.Oplevel, Fields.Realname };
            fields = new List<string>() { "t", "c", "u", "i", "h", "s", "n", "f", "d", "l", "a", "o", "r" };
        }

        public class WhoX
        {
            public string nickname;
            public string? token;
            public string fields;

            public long creationDate;
        }
    }
}