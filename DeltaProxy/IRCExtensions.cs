﻿using System;
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
            return splet[index].Trim(':').Equals(cmp.Trim(':'), StringComparison.OrdinalIgnoreCase);
        }

        public static bool AssertCount(this List<string> splet, int count, bool orMore = false)
        {
            return splet.Count == count || (orMore && splet.Count >= count);
        }

        public static string[] SplitLong(this string msg)
        {
            int encLength = Encoding.UTF8.GetByteCount(msg);
            int cutLength = (encLength == msg.Length) ? 440 : 210 - 20;

            if (msg.Length < cutLength)
            {
                return msg.Split('\n');
            }
            else
            {
                var m = msg.Replace("\n", " ");
                List<string> parts = new();
                for (int i = 0; i < (m.Length / cutLength) + 1; i++)
                {
                    int start = i * cutLength;
                    int length = Math.Abs(Math.Min(cutLength, m.Length - cutLength * i));

                    parts.Add(m.Substring(start, length));
                }
                return parts.ToArray();
            }
        }

        public static string GetLongString(this string message)
        {
            int index = message.IndexOf(':');
            if (index == -1) return "";
            string preReady = message.Substring(index); if (preReady.StartsWith(":")) preReady = preReady.Substring(1);
            return preReady;
        }

        public static string Join(this string[] arr, int start, char delim = ' ', int length = -1)
        {
            return arr.JoinStr(start, delim.ToString(), length);
        }

        public static string JoinStr(this string[] arr, int start, string delim = " ", int length = -1)
        {
            string res = "";
            for (int i = start; i < (length == -1 ? arr.Length : Math.Min(arr.Length, length)); i++)
            {
                res += arr[i] + delim;
            }
            return res.Substring(0, Math.Max(0, res.Length - delim.Length));
        }

        public static void SendPostClientMessage(this ConnectionInfo sw, string message)
        {
            lock (sw.postClientQueue) sw.postClientQueue.Add($":{Program.cfg.serverHostname} {message}");
        }

        public static void SendClientMessage(this ConnectionInfo sw, string message, bool flush = true)
        {
            SendRawClientMessage(sw, $":{Program.cfg.serverHostname} {message}", flush);
        }

        public static void SendRawClientMessage(this ConnectionInfo sw, string message, bool flush = true)
        {
            lock (sw.clientQueue) sw.clientQueue.Add(message);
            if (flush) sw.FlushClientQueue();
        }

        public static void SendClientMessage(this ConnectionInfo sw, string sender, string receiver, string message, bool flush = true)
        {
            var fullUser = $"{sender}!proxy@{Program.cfg.serverHostname}";
            lock (sw.clientQueue) sw.clientQueue.Add($":{sw.GetProperNickname(fullUser)} NOTICE {receiver} :{message}");
            if (flush) sw.FlushClientQueue();
        }

        public static bool AssertPartOfChannel(this ConnectionInfo info, string channel)
        {
            lock (channelUsers) return channelUsers[channel].Contains(info);
        }

        public static bool AssertCorrectPerson(this List<string> id, ConnectionInfo info)
        {
            if (info is null || string.IsNullOrEmpty(info.Nickname) || string.IsNullOrEmpty(info.Username) || string.IsNullOrEmpty(info.VHost)) return false;
            if (!id.AssertCount(1, true)) return false;

            var tuple = id[0].ParseIdentifier();

            if (tuple.Item1 == info.Nickname && tuple.Item3 == info.VHost) return true;
            if (info is null || info.capabilities.Count == 0) return false;
            if (!info.capabilities.Contains("userhost-in-names") && tuple.Item1 == info.Nickname) return true;
            return false;
        }

        public static bool HasChannel(this string joinSequence, string channel)
        {
            return joinSequence.Trim(':').Split(',').Any((z) => z == channel);
        }

        public static string PreserveMultiChannel(this string joinStr, string? remove = null)
        {
            if (remove is null) return joinStr;

            // expect string of JOIN #a,#b,#c or JOIN #a
            string[] joinSplet = joinStr.Split(" ");
            if (joinSplet.Length < 2) return joinStr;
            string channels = joinSplet[1];
            List<string> chanList = channels.Split(',').ToList();

            chanList.Remove(remove);

            if (chanList.Count == 0) return "";

            channels = chanList.ToArray().JoinStr(0, ",");

            return $"JOIN {channels}{(joinSplet.Length > 1 ? $" {joinSplet.JoinStr(2, " ")}" : "")}";
        }

        public static void FlushClientQueueAsync(this ConnectionInfo sw)
        {
            new Thread(() => sw.FlushClientQueue()).Start();
        }

        public static Tuple<string, string, string> ParseIdentifier(this string id)
        {
            if (!id.Contains("!")) return new Tuple<string, string, string>(id, null, null);
            var splet = id.Trim().Trim(':').Split('@');
            var final = splet.First().Split('!');
            var tuple = new Tuple<string, string, string>(final.First(), final.Last(), splet.Last());
            return tuple;
        }

        public static string GetTimeString(ConnectionInfo info)
        {
            var t = UnixMS();
            return info.capabilities.Contains("server-time") ? $"@time={DateTimeOffset.FromUnixTimeMilliseconds(t).ToString("yyyy-MM-ddTHH:mm:ss.fffZ")} " : "";
        }

        public static string GetTimeString(ConnectionInfo info, long timestampMS)
        {
            return info.capabilities.Contains("server-time") ? $"@time={DateTimeOffset.FromUnixTimeMilliseconds(timestampMS).ToString("yyyy-MM-ddTHH:mm:ss.fffZ")} " : "";
        }

        public static string GetProperNickname(this ConnectionInfo info)
        {
            if (info.capabilities.Contains("userhost-in-names")) return $"{info.Nickname}!{info.Username}@{info.VHost}";
            return info.Nickname;
        }

        public static string GetProperNickname(this ConnectionInfo info, string fullUser)
        {
            var parse = ParseIdentifier(fullUser);

            if (info.capabilities.Contains("userhost-in-names")) return $"{parse.Item1}!{parse.Item2}@{parse.Item3}";
            return parse.Item1;
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

        public static string Clamp(this string c, int length, int lines = 99999, bool threedots = true)
        {
            string[] larr = c.Split('\n');

            if (c.Length <= length && larr.Length <= lines) return c;

            string ps = c;

            if (larr.Length > lines)
            {
                ps = larr.Join(0, '\n', lines);
            }

            return ps.Substring(0, Math.Min(length, ps.Length)) + (threedots ? "..." : "");
        }

        public static string FileSize(long v)
        {
            if (v >= 1024 * 1024 * 1024) return $"{Math.Round(v / (1024 * 1024 * 1024d), 2)} GB";
            if (v >= 1024 * 1024) return $"{Math.Round(v / (1024 * 1024d), 2)} MB";
            if (v >= 1024) return $"{Math.Round(v / 1024d, 2)} KB";
            return $"{v} B";
        }
    }
}
