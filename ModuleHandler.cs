using DeltaProxy.modules;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using System.Threading.Tasks;
using static DeltaProxy.modules.ConnectionInfoHolderModule;

namespace DeltaProxy
{
    public class ModuleHandler
    {
        public enum Active
        {
            No = 0, Server = 1, Client = 2
        }

        /// <summary>
        /// List of modules. The order which they are here is the order in which a message will be processed!
        /// </summary>
        public static List<Type> modules = new List<Type>() {
            typeof(ConnectionInfoHolderModule),
            typeof(BansModule),
            typeof(CaptchaModule),
            typeof(WordBlacklistModule),
            typeof(AdvertisementModule),
            typeof(FirstConnectionKillModule), 
            typeof(StaffAuthModule),
            typeof(AdminConfigModule)
        };

        private static List<MethodInfo> hashed_server = new();
        private static List<MethodInfo> hashed_client = new();

        /// <summary>
        /// Enables all modules by calling OnEnable within enabled modules.
        /// </summary>
        public static void EnableModules()
        {
            // first we enable modules
            foreach (var moduleType in modules)
            {
                Program.Log($"Enabling module {moduleType.Name}...");
                var onEnableMethod = moduleType.GetMethod("OnEnable", BindingFlags.Public | BindingFlags.Static);
                if (onEnableMethod is not null) // we enable ALL modules, regardless of whether or not they wish to be enabled, to init configs and databases
                {
                    onEnableMethod.Invoke(null, null);
                }
            }

            // then we hash the lists of enabled modules for server & client processors
            HashModules();
            // any module editing its isEnabled property during runtime will have to HashModules!!!!
        }

        /// <summary>
        /// Processes server message
        /// </summary>
        /// <param name="info">Info about this connection</param>
        /// <param name="msg">Message sent by server</param>
        public static bool ProcessServerMessage(ConnectionInfo info, string msg)
        {
            // making sure the msg doesn't contain @time timestamp like @time=2023-07-09T13:25:37.688Z
            if (msg.StartsWith("@time=")) { msg = msg.SplitMessage().ToArray().Join(1); }

            foreach (var method in hashed_server)
            {
                bool? executionResult = (bool?)method.Invoke(null, new object[] { info, msg });

                // some modules can prevent server messages if they return false boolean.
                if (executionResult is null) continue;
                if (executionResult.HasValue && !executionResult.Value) return false;
            }

            return true;
        }

        /// <summary>
        /// Processes client message
        /// </summary>
        /// <param name="info">Info about this connection</param>
        /// <param name="msg">Message sent by a client</param>
        /// <returns>Whether or not the message should be forwarded to server.</returns>
        public static bool ProcessClientMessage(ConnectionInfo info, string msg)
        {
            foreach (var method in hashed_client)
            {
                bool executionResult = (bool)method.Invoke(null, new object[] { info, msg });

                if (!executionResult) Program.Log($"{method.DeclaringType.Name} -> {executionResult}");

                if (!executionResult) return false; // halt execution as requested by a module
            }

            return true;
        }

        /// <summary>
        /// Builds a list of currently active and enabled modules, ready to receive server & client messages
        /// </summary>
        public static void HashModules()
        {
            lock (hashed_server)
            {
                lock (hashed_client)
                {
                    foreach (var moduleType in modules)
                    {
                        var tuple = IsModuleActive(moduleType);
                        var activity = tuple.Item1;

                        if (((int)activity & (int)Active.Server) != 0) hashed_server.Add(tuple.Item2[0]);
                        if (((int)activity & (int)Active.Client) != 0) hashed_client.Add(tuple.Item2[1]);
                    }
                }
            }
        }

        /// <summary>
        /// Checks whether or not a module implements client and/or server functionality, as well as returns the list of methods with said functionality implemented
        /// </summary>
        /// <param name="module"></param>
        /// <returns></returns>

        public static Tuple<Active, List<MethodInfo>> IsModuleActive(Type module)
        {
            int flag = 0;
            var list = new List<MethodInfo>();

            bool enabled = CheckIsEnabledCfg(module);
            if (!enabled) return new Tuple<Active, List<MethodInfo>>((Active)flag, list);

            var resolveServerMessageMethod = module.GetMethod("ResolveServerMessage", BindingFlags.Public | BindingFlags.Static);
            if (resolveServerMessageMethod is not null)
            {
                flag |= (int)Active.Server;
            }
            list.Add(resolveServerMessageMethod);
            var resolveClientMessageMethod = module.GetMethod("ResolveClientMessage", BindingFlags.Public | BindingFlags.Static);
            if (resolveClientMessageMethod is not null)
            {
                flag |= (int)Active.Client;
            }
            list.Add(resolveClientMessageMethod);

            return new Tuple<Active, List<MethodInfo>>((Active)flag, list);
        }

        public static bool CheckIsEnabledCfg(Type module)
        {
            var cfgField = module.GetField("cfg");
            if (cfgField is null) { return true; } // if no cfg is defined, call the module
            var cfgInstance = cfgField.GetValue(null);
            var isEnabledField = cfgField.FieldType.GetField("isEnabled");
            if (isEnabledField is null) { return true; } // if no isEnabled field is present, call the module
            if (isEnabledField.FieldType == typeof(bool))
            {
                var isEnabled = (bool)isEnabledField.GetValue(cfgInstance);

                // if cfg is present, isEnabled is present and it's true, call the module
                return isEnabled;
            }
            return true;
        }
    }
}
