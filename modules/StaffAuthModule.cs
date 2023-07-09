﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static DeltaProxy.modules.ConnectionInfoHolderModule;

namespace DeltaProxy.modules
{
    /// <summary>
    /// This is a CLIENT-side module is used for staff (admins and moderators) to auth as such using a password. This module cannot be disabled. To auth, use /proxyauth password
    /// </summary>
    public class StaffAuthModule
    {
        public static ModuleConfig cfg;
        public static List<ConnectionInfo> authedStaff;

        public static void OnEnable()
        {
            cfg = ModuleConfig.LoadConfig("mod_staff.json");
            authedStaff = new List<ConnectionInfo>();
        }

        public static bool ResolveClientMessage(ConnectionInfo info, string msg)
        {
            var msgSplit = msg.SplitMessage();

            if (msgSplit.Assert("PROXYAUTH", 0))
            {
                if (msgSplit.AssertCount(1)) { info.Writer.SendServerMessage("DeltaProxy", info.Nickname, $"USAGE: /proxyauth <password> - The command is used for staff to auth."); return false; }

                string password = msgSplit[1];
                var staff = cfg.staff.FirstOrDefault((z) => (z.Nickname == info.Nickname && z.Password == password) || (!cfg.ShouldNicknameMatch && z.Password == password));
                if (staff is null)
                {
                    info.Writer.SendServerMessage("DeltaProxy", info.Nickname, $"Incorrect PROXYAUTH password or your nickname doesn't match."); return false;
                }

                lock (authedStaff) authedStaff.Add(info);
                info.Writer.SendServerMessage("DeltaProxy", info.Nickname, $"Successfully authed as {staff.Nickname}!"); return false;
            }

            return true;
        }

        public class ModuleConfig : ConfigBase<ModuleConfig>
        {
            public bool ShouldNicknameMatch = true;
            public List<Staff> staff = new();
        }

        public class Staff
        {
            public string Nickname;
            public string Password;
        }
    }
}
