using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static DeltaProxy.ModuleHandler;
using static DeltaProxy.modules.ConnectionInfoHolderModule;

namespace DeltaProxy.modules.StaffAuth
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

        public static ModuleResponse ResolveClientMessage(ConnectionInfo info, string msg)
        {
            var msgSplit = msg.SplitMessage();

            if (msgSplit.Assert("PROXYAUTH", 0))
            {
                if (msgSplit.AssertCount(1)) { info.SendClientMessage("DeltaProxy", info.Nickname, $"USAGE: /proxyauth <password> - The command is used for staff to auth."); return ModuleResponse.BLOCK_PASS; }

                string password = msgSplit[1];
                var staff = cfg.staff.FirstOrDefault((z) => (z.Nickname == info.Nickname && z.Password == password) || (!cfg.ShouldNicknameMatch && z.Password == password));
                if (staff is null)
                {
                    info.SendClientMessage("DeltaProxy", info.Nickname, $"Incorrect PROXYAUTH password or your nickname doesn't match."); return ModuleResponse.BLOCK_PASS;
                }

                lock (authedStaff) authedStaff.Add(info);
                info.SendClientMessage("DeltaProxy", info.Nickname, $"Successfully authed as {staff.Nickname}!"); return ModuleResponse.BLOCK_PASS;
            }

            return ModuleResponse.PASS;
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
