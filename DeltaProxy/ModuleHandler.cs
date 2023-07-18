using DeltaProxy.modules;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Reflection.Metadata.Ecma335;
using System.Security.AccessControl;
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
        };

        public static List<MethodInfo> hashed_server = new();
        public static List<MethodInfo> hashed_client = new();

        public enum ModuleResponse
        {
            PASS = 1, // passes the response to the next module in line - or server, if no modules are in line
            BLOCK_PASS = 2, // passes the response to the next module, but the request will not reach the server or client. Queue and post-queue will still flush
            BLOCK_MODULES = 4, // blocks the response from reaching the next module or server/client. Queue and post-queue will still flush
            BLOCK_ALL = 8, // blocks the response from reaching the next module or server/client. No queues will be flushed
        }

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
        /// <param name="callingMethod">If message was sent by a module, rather than a client, this should be equal to module's name</param>
        public static ModuleResponse ProcessServerMessage(ConnectionInfo info, ref string msg, Type callingMethod = null)
        {
            // making sure the msg doesn't contain @ prefixes like @time=2023-07-09T13:25:37.688Z
            string prefix = null;
            if (msg.StartsWith("@")) { prefix = msg.SplitMessage()[0]; msg = msg.SplitMessage().ToArray().Join(1); }

            object[] args = new object[] { info, msg };

            ModuleResponse mrp = ModuleResponse.PASS;

            foreach (var method in hashed_server)
            {
                if (callingMethod is not null && method.DeclaringType == callingMethod) continue;
                try
                {
                    ModuleResponse? executionResult = (ModuleResponse?)method.Invoke(null, args);

                    msg = (string)args.Last(); // allows modules to overwrite the original message

                    // some modules can prevent server messages if they return proper enum.
                    if (executionResult is null) continue;
                    if (executionResult.HasValue && executionResult.Value == ModuleResponse.PASS) continue;
                    if (executionResult.HasValue && executionResult.Value == ModuleResponse.BLOCK_PASS && (int)mrp < (int)executionResult.Value) mrp = executionResult.Value;
                    return executionResult.Value;
                }
                catch (Exception ex)
                {
                    Program.Log($"Fatal Exception by module {method.DeclaringType.Name}: {ex.Message} {ex.StackTrace} {(ex.InnerException is not null ? $"{ex.InnerException.Message} {ex.InnerException.StackTrace}" : "")}");
                }
            }

            // return the prefix string for the message
            if (!string.IsNullOrEmpty(prefix)) { msg = $"{prefix} {msg}"; }

            return mrp;
        }

        /// <summary>
        /// Processes client message
        /// </summary>
        /// <param name="info">Info about this connection</param>
        /// <param name="msg">Message sent by a client</param>
        /// <param name="callingMethod">If message was sent by a module, rather than a client, this should be equal to module's name</param>
        /// <returns>Whether or not the message should be forwarded to server.</returns>
        public static ModuleResponse ProcessClientMessage(ConnectionInfo info, ref string msg, Type callingMethod = null)
        {
            ModuleResponse mrp = ModuleResponse.PASS;

            object[] args = new object[] { info, msg };

            foreach (var method in hashed_client)
            {
                if (callingMethod is not null && method.DeclaringType == callingMethod) continue;
                try
                {
                    ModuleResponse? executionResult = (ModuleResponse?)method.Invoke(null, args);

                    msg = (string)args.Last(); // allows modules to overwrite the original message

                    if (executionResult is null) continue;
                    if (executionResult.HasValue && executionResult.Value == ModuleResponse.PASS) continue;
                    if (executionResult.HasValue && executionResult.Value == ModuleResponse.BLOCK_PASS && (int)mrp < (int)executionResult.Value) mrp = executionResult.Value;
                    return executionResult.Value;
                }
                catch (Exception ex)
                {
                    Program.Log($"Fatal Exception by module {method.DeclaringType.Name}: {ex.Message} {ex.StackTrace} {(ex.InnerException is not null ? $"{ex.InnerException.Message} {ex.InnerException.StackTrace}" : "")}");
                }
            }

            return mrp;
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
