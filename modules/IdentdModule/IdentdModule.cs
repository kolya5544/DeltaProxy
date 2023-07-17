using DeltaProxy;
using static DeltaProxy.modules.ConnectionInfoHolderModule;

namespace IdentdModule
{
    public class IdentdModule
    {
        public static ModuleConfig cfg;

        public static bool ResolveClientMessage(ConnectionInfo info, string msg)
        {
            return true;
        }

        public static void OnEnable()
        {
            cfg = ModuleConfig.LoadConfig("mod_identd.json");
        }

        public class ModuleConfig : ConfigBase<ModuleConfig>
        {
            public bool isEnabled = false;
            public bool insertTilda = true;
        }
    }
}