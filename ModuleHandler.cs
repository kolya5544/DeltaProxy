using DeltaProxy.modules;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static DeltaProxy.ConnectionInfoHolderModule;

namespace DeltaProxy
{
    public class ModuleHandler
    {
        /// <summary>
        /// Processes server message
        /// </summary>
        /// <param name="info">Info about this connection</param>
        /// <param name="msg">Message sent by server</param>
        public static void ProcessServerMessage(ConnectionInfo info, string msg)
        {
            ConnectionInfoHolderModule.ResolveServerMessage(info, msg);
            AdvertisementModule.ResolveServerMessage(info, msg);
        }

        /// <summary>
        /// Processes client message
        /// </summary>
        /// <param name="info">Info about this connection</param>
        /// <param name="msg">Message sent by a client</param>
        /// <returns>Whether or not the message should be forwarded to server.</returns>
        public static bool ProcessClientMessage(ConnectionInfo info, string msg)
        {
            bool result = true;

            result &= ConnectionInfoHolderModule.ResolveClientMessage(info, msg);
            result &= FirstConnectionKillModule.cfg.isEnabled ? FirstConnectionKillModule.ResolveClientMessage(info, msg) : true;

            return result;
        }
    }
}
