using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static DeltaProxy.modules.ConnectionInfoHolderModule;

namespace DeltaProxy.modules
{
    /// <summary>
    /// This is a SERVER-side module that inserts DeltaProxy advertisement into server's MOTD. It can be disabled safely.
    /// </summary>
    public class AdvertisementModule
    {
        public static ModuleConfig cfg = ModuleConfig.LoadConfig("mod_advertisement.conf");

        public static void ResolveServerMessage(ConnectionInfo info, string msg)
        {
            var msgSplit = msg.SplitMessage();

            if (msgSplit.Assert("376", 1) && msgSplit.AssertCount(5, true)) // expects server to end a MOTD
            {
                // now we just insert our own MOTD part at the end
                info.Writer.SendServerMessage($"372 {info.Nickname} :-");
                info.Writer.SendServerMessage($"372 {info.Nickname} :- This server is powered by DeltaProxy. Advanced bot protection and activity monitoring at {cfg.SourceCode}");
            }
        }

        public class ModuleConfig : ConfigBase<ModuleConfig>
        {
            public bool isEnabled = true;
            public string SourceCode = "https://github.com/kolya5544/DeltaProxy";
        }
    }
}