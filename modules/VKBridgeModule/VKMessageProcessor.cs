using DeltaProxy;
using dotVK;
using EmbedIO;
using Swan.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using VKBotExtensions;
using static DeltaProxy.modules.ConnectionInfoHolderModule;
using static DeltaProxy.modules.VKBridge.VKBridgeModule;

namespace DeltaProxy.modules.VKBridge
{
    public class VKMessageProcessor
    {
        /// <summary>
        /// Sends an update to all user based on delta online status and whether or not someone left or joined, as well as nickname collision checks
        /// </summary>
        public static void UpdateUsers(bool firstTime = false)
        {
            var members = bot.GetConversationMembers(VKBridgeModule.cfg.vkChat);
            var newMembers = new List<SharedVKUserHolder>();
            members.profiles.ForEach((z) =>
            {
                ConnectionInfo connectedPerson;
                lock (connectedUsers) connectedPerson = connectedUsers.FirstOrDefault((x) => x.Nickname is not null && x.Nickname.Equals(z.screen_name, StringComparison.OrdinalIgnoreCase));
                newMembers.Add(new SharedVKUserHolder()
                {
                    screenName = connectedPerson is null ? z.screen_name : $"{VKBridgeModule.cfg.collisionPrefix}{z.screen_name}",
                    fullName = $"{z.first_name} {z.last_name}",
                    isBot = false,
                    id = z.id,
                    isOnline = z.online == 1,
                    lastStatusChange = z.online_info.last_seen.HasValue ? z.online_info.last_seen.Value : -1
                });
            });
            members.groups.ForEach((z) =>
            {
                ConnectionInfo connectedPerson;
                lock (connectedUsers) connectedPerson = connectedUsers.FirstOrDefault((x) => x.Nickname is not null && x.Nickname.Equals(z.screen_name, StringComparison.OrdinalIgnoreCase));
                newMembers.Add(new SharedVKUserHolder()
                {
                    screenName = connectedPerson is null ? z.screen_name : $"{VKBridgeModule.cfg.collisionPrefix}{z.screen_name}",
                    fullName = $"{z.name}",
                    isBot = true,
                    id = -z.id,
                    isOnline = true,
                    lastStatusChange = 0
                });
            });

            // locally hash new members
            Dictionary<long, SharedVKUserHolder> localHashmap = new Dictionary<long, SharedVKUserHolder>();
            newMembers.ForEach((z) => localHashmap.Add(z.id, z));

            if (firstTime)
            {
                vkMembers = newMembers;
                hashedMembers = localHashmap;
            }

            // now we shall calculate the delta

            // first, we should add new members that weren't present
            newMembers.ForEach((z) =>
            {
                if (hashedMembers.ContainsKey(z.id)) return;
                // we got a new member! tell everyone they've joined, but don't flush yet.
                lock (bridgeMembers) { bridgeMembers.ForEach((x) => { lock (x.clientQueue) x.clientQueue.Add($"{IRCExtensions.GetTimeString(x)}:{z.GetActualUser(x, false, true)} JOIN {VKBridgeModule.cfg.ircChat} * :VK user {z.fullName} from https://vk.com/{z.screenName}"); }); vkMembers.Add(z); }

                // hash
                hashedMembers.Add(z.id, z);
            });

            // then, we should delete all members that were present before
            hashedMembers.ToList().ForEach((z) =>
            {
                if (localHashmap.ContainsKey(z.Key)) return;
                // we lost a member. alert
                lock (bridgeMembers) { bridgeMembers.ForEach((x) => { lock (x.clientQueue) x.clientQueue.Add($"{IRCExtensions.GetTimeString(x)}:{z.Value.GetActualUser(x, false, true)} QUIT :Goodbye!"); }); vkMembers.Remove(z.Value); }

                // hash
                hashedMembers.Remove(z.Key);
            });

            // we should change screenname of all users in accordance with whether or not they got nickname collisioned
            localHashmap.ToList().ForEach((z) =>
            {
                if (hashedMembers[z.Key].screenName == z.Value.screenName) return;
                // nickname change was noticed!
                lock (bridgeMembers) { bridgeMembers.ForEach((x) => { lock (x.clientQueue) x.clientQueue.Add($"{IRCExtensions.GetTimeString(x)}:{hashedMembers[z.Key].GetActualUser(x, false, true)} NICK :{z.Value.screenName}"); }); }
            });

            if (VKBridgeModule.cfg.sendOnlineUpdatesIRC)
            {
                // we should set +v on people that had been online for more than 5 minutes, and remove +v from people that were offline for more than 20 minutes
                localHashmap.ToList().ForEach((z) =>
                {
                    if (z.Value.isBot || z.Value.lastStatusChange == -1) return; // don't update bots or people with disabled online status
                    if (hashedMembers[z.Key].isOnline == z.Value.isOnline) return;
                    if ((IRCExtensions.Unix() - z.Value.lastStatusChange <= 1200 && !z.Value.isOnline) || (IRCExtensions.Unix() - z.Value.lastStatusChange <= 300 && z.Value.isOnline)) return;
                    // online status was noticed!
                    lock (bridgeMembers) { bridgeMembers.ForEach((x) => { lock (x.clientQueue) x.clientQueue.Add($"{IRCExtensions.GetTimeString(x)}:DeltaProxy!DeltaProxy@DeltaProxy MODE {VKBridgeModule.cfg.ircChat} {(z.Value.isOnline ? " + " : "-")}v {z.Value.screenName}"); }); }
                });
            }

            // rehash
            lock (hashedMembers)
            {
                hashedMembers.Clear();
                newMembers.ForEach((z) => hashedMembers.Add(z.id, z));
            }
            lock (vkMembers) vkMembers = newMembers;

            lock (bridgeMembers) bridgeMembers.ForEach((x) => x.FlushClientQueueAsync());
        }

        public static void HandleUpdate(Update u)
        {
            // this code was taken from my other project, LavaBridge
            // it is a very messy project, but it is decent enough

            string text = u.Object.Message.Text;
            long sender = u.Object.Message.FromId;
            long chat = u.Object.Message.PeerId;
            var act = u.Object.Message.Action;

            SharedVKUserHolder actualSender;
            lock (vkMembers) actualSender = vkMembers.FirstOrDefault((z) => z.id == sender);

            var mS = text.SplitMessage();

            if (text == "/ignoreme")
            {
                bool newStatus = false;
                lock (VKBridgeModule.db.lockObject)
                {
                    if (VKBridgeModule.db.ignoredVK.Contains(sender))
                    {
                        VKBridgeModule.db.ignoredVK.Remove(sender);
                    }
                    else
                    {
                        VKBridgeModule.db.ignoredVK.Add(sender);
                        newStatus = true;
                    }
                }
                bot.SendMessage(chat, $"Success! Your messages will {(newStatus ? "NO LONGER" : "now")} be seen by everyone on IRC.");
                VKBridgeModule.db.SaveDatabase();
                return;
            }
            else if (text.StartsWith("/msg") && mS.AssertCount(3, true) && chat < 2000000000)
            {
                if (actualSender is null)
                {
                    bot.SendMessage(sender, $"Couldn't find an instance of sender! You have to be a part of the public chat to send private messages."); return;
                }

                string receiver = mS[1];
                string message = mS.ToArray().Join(2);

                ConnectionInfo msgReceiver;
                lock (bridgeMembers) msgReceiver = bridgeMembers.FirstOrDefault((z) => z.Nickname == receiver);

                if (msgReceiver is null)
                {
                    bot.SendMessage(sender, $"Couldn't find reciever! The receiver has to be a part of the bridge channel to get PMs."); return;
                }

                bool isIgnored = false;
                lock (VKBridgeModule.db.lockObject) isIgnored = VKBridgeModule.db.ignores.Any((z) => z.name == msgReceiver.Nickname && z.id == sender);
                if (isIgnored)
                {
                    bot.SendMessage(sender, $"Your message was not send! You ignore the user, or are being ignored by the user.");
                    return;
                }

                string safeMsg = RemoveBadChar(text).Clamp(1024, 7);
                lock (msgReceiver.clientQueue) msgReceiver.clientQueue.Add($"{IRCExtensions.GetTimeString(msgReceiver)}:{actualSender.GetActualUser(msgReceiver, false)} PRIVMSG {msgReceiver.Nickname} :{message}");
                bot.SendMessage(sender, $"Successfully sent the private message!");
                return;
            } else if (text.StartsWith("/ignore") && mS.AssertCount(2))
            {
                string whom = mS[1];

                Ignore ignores;
                bool newStatus = false;
                lock (VKBridgeModule.db.lockObject) ignores = VKBridgeModule.db.ignores.FirstOrDefault((z) => z.createdOnIrc == false && z.id == sender && z.name == whom);
                if (ignores is null)
                {
                    newStatus = true;
                    ignores = new Ignore() { createdOnIrc = false, id = sender, name = whom };
                    lock (VKBridgeModule.db.lockObject) VKBridgeModule.db.ignores.Add(ignores);
                } else
                {
                    lock (VKBridgeModule.db.lockObject) VKBridgeModule.db.ignores.Remove(ignores);
                }

                bot.SendMessage(chat, $"Successfully {(newStatus ? "BLOCKED" : "unblocked")} user {whom}!");
                VKBridgeModule.db.SaveDatabase();
                return;
            }
            else if (chat != VKBridgeModule.cfg.vkChat && chat < 2000000000)
            {
                bot.SendMessage(sender, $"Unknown command! Commands available: /msg <user> <message>, /ignore <user>, /ignoreme"); return;
            }

            if (chat != VKBridgeModule.cfg.vkChat) return;

            if (act is not null && act.Type == "chat_kick_user")
            {
                var kickedName = lc.AcquireName(act.MemberID);
                text = $"*had kicked user {kickedName}*";
            }
            else if (act is not null && act.Type == "chat_invite_user")
            {
                var addedName = bot.GetName(act.MemberID);
                text = $"*had added user {addedName.FullName}*";

                // we have to create a fake user at this stage
                var connectedPerson = connectedUsers.FirstOrDefault((x) => x.Nickname.Equals(addedName.LinkName, StringComparison.OrdinalIgnoreCase));
                var newUser = new SharedVKUserHolder()
                {
                    screenName = connectedPerson is null ? addedName.LinkName : $"{VKBridgeModule.cfg.collisionPrefix}{addedName.LinkName}",
                    fullName = $"{addedName.FullName}",
                    isBot = act.MemberID < 0,
                    id = act.MemberID,
                    isOnline = true
                };

                lock (vkMembers) vkMembers.Add(newUser);
                lock (hashedMembers) hashedMembers.Add(newUser.id, newUser);

                // and let everyone know it exists
                lock (bridgeMembers)
                {
                    bridgeMembers.ForEach((x) =>
                    {
                        lock (x.clientQueue) x.clientQueue.Add($"{IRCExtensions.GetTimeString(x)}:{newUser.GetActualUser(x, false)} JOIN {VKBridgeModule.cfg.ircChat} * :{newUser.fullName}");
                    });
                }
            }

            if (VKBridgeModule.db.ignoredVK.Contains(sender)) return;

            if (actualSender is null) // user not currently present!!! we'll need to update users IMMEDIATELY!
            {
                UpdateUsers();
                actualSender = vkMembers.FirstOrDefault((z) => z.id == sender);
            }

            if (actualSender is null) // user is still null. We'll have to drop the message. We cannot afford delaying the workflow any further!
            {
                bot.SendMessage(chat, $"<!> Your message was not sent! It's possible the issues are on VK API's side. Please try again in a minute. <!>");
                return;
            }

            var safeName = lc.AcquireName(sender);

            var regex = new Regex(@"\[(id|club)(\d*)\|([^\]]*)]");
            var matches = regex.Matches(text).ToList();
            foreach (var mt in matches)
            {
                var type = mt.Groups[1].Value;
                var id = mt.Groups[2].Value;
                var mtTxt = mt.Groups[3].Value;

                text = text.Replace($"[{type}{id}|{mtTxt}]", mtTxt);
            }

            if (u.Object.Message.ReplyMessage is not null)
            {
                var tid = u.Object.Message.ReplyMessage;
                var repliedTo = u.Object.Message.ReplyMessage.FromId == -VKBridgeModule.cfg.vkGroup ? (tid.Text.StartsWith("=>") ? VKBridgeModule.cfg.vkBotName : tid.Text.Split('<')[1].Split('>')[0]) : lc.AcquireName(tid.FromId);
                safeName += $"->{repliedTo}";
            }

            var m = u.Object.Message;

            if ((text.StartsWith("/online") || text.StartsWith("/онлайн")) && VKBridgeModule.cfg.allowOnline)
            {
                StringBuilder sb = new();
                sb.AppendLine("=> Список пользователей IRC:");
                List<ConnectionInfo> uso;
                lock (bridgeMembers) uso = bridgeMembers.OrderBy((z) => z.Nickname).ToList();
                foreach (var us in uso)
                {
                    sb.AppendLine(us.Nickname);
                }
                bot.SendMessage(chat, sb.ToString()); return;
            }

            var attachment = u.Object.Message.Attachments.LastOrDefault();
            if (attachment is not null && attachment.Photo is not null)
            {
                PhotoSize bestSize = null; long bestRes = 0;
                PhotoSize originalSize = null;
                foreach (PhotoSize ps in attachment.Photo.Sizes)
                {
                    if ((ps.Width * ps.Height) > bestRes && ps.Width < 2048 && ps.Height < 2048) { bestSize = ps; bestRes = (ps.Width * ps.Height); }
                    if (ps.Type == "z") { originalSize = ps; }
                }

                var url = bestSize.Url;
                url = lc.Cache(url, "photo");
                text += text.Trim().Length == 0 ? url : $" (attached: {url})";
                //text += $" {attachment.Photo.}"
            }
            else if (attachment is not null && attachment.Sticker is not null)
            {
                var sticker = attachment.Sticker;
                PhotoSize bestSize = null; long bestRes = 0;
                PhotoSize originalSize = null;
                foreach (PhotoSize ps in sticker.Images)
                {
                    if ((ps.Width * ps.Height) > bestRes && ps.Width < 2048 && ps.Height < 2048) { bestSize = ps; bestRes = (ps.Width * ps.Height); }
                    if (ps.Type == "z") { originalSize = ps; }
                }

                var url = bestSize.Url;
                url = lc.Cache(url, "sticker");
                text = $"{url}";
            }
            else if (attachment is not null && attachment.Wall is not null)
            {
                var url = $"https://vk.com/";
                if (attachment.Wall.from is not null)
                {
                    url += $"{attachment.Wall.from.screen_name}?w=wall{attachment.Wall.from_id}_{attachment.Wall.id}";
                    url = lc.Cache(url, "post");
                }
                else
                {
                    url += "404";
                }
                text += text.Trim().Length == 0 ? url : $" (attached: {url})";
            }
            else if (attachment is not null && attachment.Video is not null)
            {
                var url = "";
                var shortName = attachment.Video.OwnerId > 0 ? "" : bot.GetGroupInfo(attachment.Video.OwnerId).ScreenName;
                if (attachment.Video.ContentRestricted != 1)
                {
                    url += $"https://vk.com/{(attachment.Video.OwnerId < 0 ? shortName : $"id{Math.Abs(attachment.Video.OwnerId)}")}?z=video{attachment.Video.OwnerId}_{attachment.Video.Id}";
                    url = lc.Cache(url, "video");
                }
                else
                {
                    url += $"video is unavailable, or is only available for https://vk.com/{(attachment.Video.OwnerId < 0 ? shortName : $"id{Math.Abs(attachment.Video.OwnerId)}")} subscribers";
                }
                text += text.Trim().Length == 0 ? url : $" (attached: {url})";
            }
            else if (attachment is not null && attachment.Audio is not null)
            {
                var duration = DateTimeOffset.FromUnixTimeSeconds(attachment.Audio.Duration).ToString("mm:ss");
                string trackFullName = $"{attachment.Audio.Artist} - {attachment.Audio.Title}, {duration}";
                text += text.Trim().Length == 0 ? trackFullName : $" (attached: {trackFullName})";
            }
            else if (attachment is not null && attachment.Doc is not null)
            {
                var f = attachment.Doc;
                var ext = f.ext;
                var fsize = IRCExtensions.FileSize(f.size);

                var cached = lc.Cache(f.url, "file");
                var final = $"file.{ext}, {fsize}. {cached}";

                text += text.Trim().Length == 0 ? final : $" (attached: {final})";
            }
            else if (attachment is not null && attachment.AudioMessage is not null)
            {
                var ts = TimeSpan.FromSeconds(attachment.AudioMessage.Duration).ToString(@"mm\:ss");
                text = $"(voice message {ts}: {lc.Cache(attachment.AudioMessage.LinkMp3, "audio")})";
            }
            else if (attachment is not null && attachment.Graffiti is not null)
            {
                var g = attachment.Graffiti;
                var url = lc.Cache(g.url, "graffiti");
                text += text.Trim().Length == 0 ? url : $" (attached: {url})";
            }
            if (m.FwdMessages is not null && m.FwdMessages.Count > 0)
            {
                if (m.FwdMessages.Count > 1)
                {
                    var cntIds = new List<long>();
                    m.FwdMessages.ForEach((z) => { if (!cntIds.Contains(z.FromId)) { cntIds.Add(z.FromId); } });
                    if (cntIds.Count > 6)
                    {
                        var txt = $"forwarded {m.FwdMessages.Count} messages";
                        text += text.Trim().Length == 0 ? $"({txt})" : $" ({txt})";
                    }
                    else
                    {
                        List<string> users = new List<string>();
                        foreach (var f in m.FwdMessages)
                        {
                            string n;
                            if (f.FromId == -VKBridgeModule.cfg.vkGroup)
                            {
                                n = f.Text.StartsWith("=>") ? "LavaBridge" : f.Text.Split('<')[1].Split('>')[0];
                            }
                            else
                            {
                                n = lc.AcquireName(f.FromId);
                            }
                            if (!users.Contains(n)) users.Add(n);
                        }
                        StringBuilder sb = new();
                        for (int i = 0; i < users.Count; i++)
                        {
                            var name = users[i];
                            if (i == users.Count - 2)
                            {
                                sb.Append($"{name} and ");
                            }
                            else
                            {
                                sb.Append($"{name}, ");
                            }
                        }
                        var rsl = sb.ToString().TrimEnd(' ').TrimEnd(',');
                        var prepend = $"Forwarded {m.FwdMessages.Count} messages by {rsl}\n";
                        text = prepend + text;
                    }
                }
                else
                {
                    var fwd = m.FwdMessages[0];
                    var fwdName = lc.AcquireName(fwd.FromId);
                    string url = fwd.Attachments.Count == 1 ? VKUtils.ResolveAttachmentURL(bot, fwd.Attachments.FirstOrDefault()) : "";
                    if (url != "")
                    {
                        if (url.StartsWith("https://"))
                        {
                            url = lc.Cache(url);
                        }
                        url = ": " + url;
                    }
                    var attachmentText = fwd.Attachments.Count == 0 ? "" : $" ({fwd.Attachments.Count} attachment" + (fwd.Attachments.Count > 1 ? "s" : "") + url + ")";
                    var prepend = $"Message forwarded from {fwdName}{(fwd.Text.Length > 0 ? $": {RemoveBadChar(fwd.Text).Clamp(250, 2)}" : "")}{attachmentText}\n";
                    text = prepend + text;
                }
            }

            string safeForIrc = RemoveBadChar(text).Clamp(1024, 7);

            string finalMessage = string.IsNullOrEmpty(safeForIrc) ? "(пустое сообщение)" : safeForIrc;
            string[] finalMsgSplit = finalMessage.Split('\n'); 
            string finalSender = $"{actualSender.screenName}!{(actualSender.isBot ? "vkbot" : "vkuser")}@vkbridge-user";
            finalMsgSplit.ToList().ForEach((x) =>
            {
                backlogChannel.AddMessageSafely(finalSender, x);
            });
            lock (bridgeMembers) bridgeMembers.ForEach((z) =>
            {
                finalMsgSplit.ToList().ForEach((x) =>
                {
                    lock (z.clientQueue) z.clientQueue.Add($"{IRCExtensions.GetTimeString(z)}:{z.GetProperNickname(finalSender)} PRIVMSG {VKBridgeModule.cfg.ircChat} :{x}");
                });
                z.FlushClientQueueAsync();
            });
        }
    }
}
