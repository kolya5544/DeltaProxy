using System;
using System.Collections.Generic;
using System.Data.SqlTypes;
using System.Linq;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using System.Threading.Tasks;
using static DeltaProxy.modules.ConnectionInfoHolderModule;

namespace DeltaProxy
{
    public static class IRCExtensions
    {
        public static List<string> SplitMessage(this string message)
        {
            return message.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        }

        public static bool Assert(this List<string> splet, string cmp, int index)
        {
            if (splet.Count <= index) return false;
            return splet[index].Equals(cmp, StringComparison.OrdinalIgnoreCase);
        }

        public static bool AssertCount(this List<string> splet, int count, bool orMore = false)
        {
            return splet.Count == count || (orMore && splet.Count >= count);
        }

        public static string GetLongString(this string message)
        {
            int index = message.IndexOf(':');
            if (index == -1) return "";
            return message.Substring(index).Trim(':');
        }

        public static string Join(this string[] arr, int start, char delim = ' ')
        {
            string res = "";
            for (int i = start; i < arr.Length; i++)
            {
                res += arr[i] + delim;
            }
            return res.TrimEnd(delim);
        }

        public static void SendClientMessage(this ConnectionInfo sw, string message, bool flush = true)
        {
            sw.clientQueue.Add($":{Program.cfg.serverHostname} {message}");
            if (flush) sw.FlushClientQueue();
        }

        public static void SendClientMessage(this ConnectionInfo sw, string sender, string receiver, string message, bool flush = true)
        {
            sw.clientQueue.Add($":{sender}!proxy@{Program.cfg.serverHostname} NOTICE {receiver} :{message}");
            if (flush) sw.FlushClientQueue();
        }

        public static Tuple<string, string, string> ParseIdentifier(this string id)
        {
            var splet = id.Trim().Trim(':').Split('@');
            var final = splet.First().Split('!');
            var tuple = new Tuple<string, string, string>(final.First(), final.Last(), splet.Last());
            return tuple;
        }

        public static string ToDuration(this long? span)
        {
            if (span < 0 || span is null) return "(forever)";

            var t = TimeSpan.FromSeconds((double)span);
            
            if (t.TotalSeconds < 1)
            {
                return $@"less than a second";
            }
            if (t.TotalMinutes < 1)
            {
                return $@"{t:%s} second{(t.Seconds != 1 ? "s" : "")}";
            }
            if (t.TotalHours < 1)
            {
                return $@"{t:%m} minute{(t.Minutes != 1 ? "s" : "")} and {t:%s} second{(t.Seconds != 1 ? "s" : "")}";
            }
            if (t.TotalDays < 1)
            {
                return $@"{t:%h} hour{(t.Hours != 1 ? "s" : "")} and {t:%m} minute{(t.Minutes != 1 ? "s" : "")}";
            }
            if (t.TotalDays < 31)
            {
                return $@"{t:%d} day{(t.Days != 1 ? "s" : "")} and {t:%h} hour{(t.Hours != 1 ? "s" : "")}";
            }

            return $@"{t:%d} days";
        }

        public static string ToDuration(this int? span) => ToDuration((long?)span);

        public static long Unix()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }

        public static long UnixMS()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }
    }
}
