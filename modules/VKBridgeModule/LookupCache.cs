using DeltaProxy;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VKBotExtensions;

namespace VKBridgeModule
{
    public class LookupCache
    {
        public List<LookupUser> lookup = new();
        public List<CachedPicture> cache = new();

        public string AcquireName(long id)
        {
            var user = lookup.FirstOrDefault(z => z.userID == id);
            if (user is null)
            {
                user = new LookupUser();
                user.lastUpdate = IRCExtensions.Unix();
                if (id < 0)
                {
                    var gr = VKBridgeModule.bot.GetGroupInfo(id);
                    user.fullName = gr.Name;
                }
                else
                {
                    var info = VKBridgeModule.bot.GetProfilePicture(id);
                    user.fullName = $"{info.FirstName} {info.LastName}";
                }
                lookup.Add(user);
            }
            if (user.lastUpdate + 3600 * 12 < IRCExtensions.Unix())
            {
                user.lastUpdate = IRCExtensions.Unix();
                if (id < 0)
                {
                    var gr = VKBridgeModule.bot.GetGroupInfo(id);
                    user.fullName = gr.Name;
                }
                else
                {
                    var info = VKBridgeModule.bot.GetProfilePicture(id);
                    user.fullName = $"{info.FirstName} {info.LastName}";
                }
            }
            return user.fullName;
        }

        public string Cache(string url, string type = "")
        {
            if (!VKBridgeModule.cfg.enableURLcache) return url;

            lock (cache) cache.RemoveAll(z => z.creationDate + VKBridgeModule.cfg.storageTime < IRCExtensions.Unix());

            CachedPicture cachedPicture;
            lock (cache) cachedPicture = cache.FirstOrDefault(z => z.fullURL == url);
            if (cachedPicture is not null) return cachedPicture.shortURL;
            cachedPicture = new CachedPicture();
            cachedPicture.creationDate = IRCExtensions.Unix();
            cachedPicture.fullURL = url;
            lock (cache)
            {
                do
                {
                    cachedPicture.ID = RandomID(8);
                } while (cache.Any(z => z.ID == cachedPicture.ID));
                cache.Add(cachedPicture);
            }
            return cachedPicture.shortURL + (string.IsNullOrEmpty(type) ? "" : $"#{type}");
        }

        public void PurgeCache(string id)
        {
            cache.RemoveAll((z) => z.ID == id);
        }

        public static Random rng = new();

        public static string RandomID(int length = 8)
        {
            const string allowedChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz0123456789";
            char[] chars = new char[length];

            for (int i = 0; i < length; i++)
            {
                chars[i] = allowedChars[rng.Next(0, allowedChars.Length)];
            }

            return new string(chars);
        }
    }

    public class CachedPicture
    {
        public string fullURL;
        public long creationDate;
        public string ID;
        public string shortURL
        {
            get
            {
                return $"{VKBridgeModule.cfg.publicURL}{ID}";
            }
        }
    }

    public class LookupUser
    {
        public long userID;
        public long lastUpdate;
        public string fullName;
    }
}
