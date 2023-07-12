using DeltaProxy.modules;
using System;
using System.Collections.Generic;
using System.IO;
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
            /*typeof(BansModule),
            typeof(CaptchaModule),
            typeof(WordBlacklistModule),
            typeof(AdvertisementModule),
            typeof(FirstConnectionKillModule), 
            typeof(StaffAuthModule),
            typeof(AdminConfigModule)*/
        };

        public static List<MethodInfo> hashed_server = new();
        public static List<MethodInfo> hashed_client = new();

        /// <summary>
        /// Enables all modules by calling OnEnable within enabled modules.
        /// </summary>
        public static void EnableModules()
        {
            // we first load the built-in ConnectionInfo module
            ConnectionInfoHolderModule.OnEnable();

            Program.Log($"Successfully enabled ConnectionInfoHolderModule");

            // don't forget to resolve dependencies
            AppDomain.CurrentDomain.AssemblyResolve += CurrentDomain_AssemblyResolve;

            // first we load all the dependencies modules might need from /deps/ folder
            var depsList = Directory.EnumerateFiles("deps", "*.dll").ToList();
            foreach (var dependency in depsList)
            {
                // load dependencies
                var lib = File.ReadAllBytes(dependency);
                var dep = Assembly.Load(lib);
            }

            // then we load the rest of modules in /modules/ folder
            var moduleList = Directory.EnumerateFiles("modules", "*.dll").ToList();

            foreach (var module in moduleList)
            {
                // that's one way to resolve dependencies
                var dllMod = LoadModule(module);

                if (dllMod is not null)
                {
                    modules.Add(dllMod);
                }
            }

            modules = modules.OrderBy(m => GetLoadPriority(m)).ThenBy(m => m.Name).ToList();

            foreach (var module in modules)
            {
                var enableMethod = GetEnableMethod(module);

                enableMethod.Invoke(null, null); // enable all modules in their defined order

                Program.Log($"Module {module.Name} enabled successfully!");
            }

            Program.Log($"Success enabling all modules! Building method lists...");

            // now we hash the lists of enabled modules for server & client processors
            HashModules();
            // any module editing its isEnabled property during runtime will have to HashModules!!!!
        }

        public static int GetLoadPriority(Type module)
        {
            var loadPriority = module.GetField("LOAD_PRIORITY");
            if (loadPriority is null) return int.MaxValue / 2;
            var priorityVal = (int?)loadPriority.GetValue(null);
            if (priorityVal is null) return int.MaxValue / 2;

            return priorityVal.Value;
        }

        public static MethodInfo GetEnableMethod(Type module)
        {
            // the method MUST be public static
            var mainMethod = module.GetMethod("OnEnable", BindingFlags.Static | BindingFlags.Public);

            if (mainMethod is null) { Program.Log($"Failure loading {module.Name}. Couldn't find public static OnEnable method."); return null; }

            return mainMethod;
        }

        public static Type LoadModule(string path)
        {
            try
            {
                var lib = File.ReadAllBytes(path);
                var dll = Assembly.Load(lib);

                var moduleTypes = dll.GetTypes();

                // we consider main class to be one containing "OnEnable" method. Thus, it has to be implemented by ALL modules.
                Type mainClass = moduleTypes.FirstOrDefault((z) => z.GetMethods().Any((x) => x.Name == "OnEnable"));

                if (mainClass is null) { Program.Log($"Failure loading {Path.GetFileName(path)}. Couldn't find main class."); return null; }

                return mainClass;
            } catch (Exception ex)
            {
                Program.Log($"Fatal exception while loading module from {path}! {ex.Message} {ex.Source}");
                if (ex.InnerException is not null) Program.Log($"{ex.InnerException.Message} {ex.InnerException.StackTrace}");
            }
            return null;
        }

        /// <summary>
        /// Sets up the priorities for modules. ConnectionInfoHolderModule will always have a priority of 0
        /// </summary>
        private static void SetPrioritiesUp()
        {
            lock (modules) modules = modules.OrderBy((z) => z.Name).ToList();

            lock (hashed_client) hashed_client = hashed_client.OrderBy((z) =>
            {
                return GetPriorityOfMethod(z, "CLIENT_PRIORITY");
            }).ThenBy((x) =>
            {
                return x.Module.Name; // for the same priority modules, sort by module name
            }).ToList();

            lock (hashed_server) hashed_server = hashed_server.OrderBy((z) =>
            {
                return GetPriorityOfMethod(z, "SERVER_PRIORITY");
            }).ThenBy((x) =>
            {
                return x.Module.Name; // for the same priority modules, sort by module name
            }).ToList();
        }

        private static int GetPriorityOfMethod(MethodInfo z, string fieldName)
        {
            var module = z.DeclaringType;

            var priority = module.GetField(fieldName);
            if (priority is null) { return int.MaxValue; } // if no priority, assume highest
            var pValue = priority.GetValue(null);
            if (pValue is null) { return int.MaxValue; } // same if no value is defined

            return (int)pValue;
        }

        private static Assembly? CurrentDomain_AssemblyResolve(object? sender, ResolveEventArgs args)
        {
            var assembly = ((AppDomain)sender).GetAssemblies().FirstOrDefault((z) => z.FullName == args.Name);
            return assembly;
        }

        /// <summary>
        /// Processes server message
        /// </summary>
        /// <param name="info">Info about this connection</param>
        /// <param name="msg">Message sent by server</param>
        public static bool ProcessServerMessage(ConnectionInfo info, string msg)
        {
            // making sure the msg doesn't contain @ prefixes like @time=2023-07-09T13:25:37.688Z
            string prefix = null;
            if (msg.StartsWith("@")) { prefix = msg.SplitMessage()[0]; msg = msg.SplitMessage().ToArray().Join(1); }

            lock (hashed_server)
            {
                foreach (var method in hashed_server)
                {
                    bool? executionResult = (bool?)method.Invoke(null, new object[] { info, msg });

                    // some modules can prevent server messages if they return false boolean.
                    if (executionResult is null) continue;
                    // also check for remote server block - if one is present, halt execution
                    if (info.RemoteBlockServer) { info.RemoteBlockServer = false; return false; }
                    if (executionResult.HasValue && !executionResult.Value) return false;
                }
            }

            // return msg the prefix string
            if (!string.IsNullOrEmpty(prefix)) { msg = $"{prefix} {msg}"; }

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
            lock (hashed_client)
            {
                foreach (var method in hashed_client)
                {
                    try
                    {
                        bool executionResult = (bool)method.Invoke(null, new object[] { info, msg });

                        // first check for remote client block - if one is present, halt execution
                        if (info.RemoteBlockClient) { info.RemoteBlockClient = false; return false; }
                        if (!executionResult) return false; // halt execution as requested by a module
                    }
                    catch (Exception ex)
                    {
                        Program.Log($"Fatal Exception by module {method.DeclaringType.Name}: {ex.Message} {ex.StackTrace}");
                    }
                }
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
                    hashed_client.Clear();
                    hashed_server.Clear();

                    foreach (var moduleType in modules)
                    {
                        var tuple = IsModuleActive(moduleType);
                        var activity = tuple.Item1;

                        if (((int)activity & (int)Active.Server) != 0) hashed_server.Add(tuple.Item2[0]);
                        if (((int)activity & (int)Active.Client) != 0) hashed_client.Add(tuple.Item2[1]);
                    }

                    // then we sort methods based on their priorities. It requires CLIENT_PRIORITY and SERVER_PRIORITY integers to be defined. Modules with LOWEST numbers will be ran first.
                    SetPrioritiesUp();
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
            if (isEnabledField is null || cfgInstance is null) { return true; } // if no isEnabled field is present, call the module
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
