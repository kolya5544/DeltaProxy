using System;
using System.Collections.Generic;
using System.Data.SqlTypes;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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

        public static void SendServerMessage(this StreamWriter sw, string message)
        {
            sw.WriteLine($":{Program.cfg.serverHostname} {message}");
        }

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
