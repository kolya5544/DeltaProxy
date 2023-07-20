using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using DeltaProxy.modules.StaffAuth;
using static DeltaProxy.modules.ConnectionInfoHolderModule;
using static DeltaProxy.ModuleHandler;

namespace DeltaProxy.modules.AdminConfig
{
    /// <summary>
    /// This module allows staff members (authed using StaffAuthModule) to configure and manage modules from within the IRC interface. It is a CLIENT-side module. Use /admin to access it.
    /// </summary>
    public class AdminConfigModule
    {
        public static ModuleConfig cfg;
        public static List<AdminChoice> choices;

        public static ModuleResponse ResolveClientMessage(ConnectionInfo info, string msg)
        {
            var msgSplit = msg.SplitMessage();

            // check if it's an admin command /admin
            if (msgSplit.Assert("ADMIN", 0))
            {
                lock (StaffAuthModule.authedStaff) if (!StaffAuthModule.authedStaff.Contains(info)) { info.SendClientMessage("DeltaProxy", info.Nickname, $"Access denied."); return ModuleResponse.BLOCK_PASS; }

                AdminChoice ac;
                lock (choices) ac = choices.FirstOrDefault((z) => z.Nickname == info.Nickname);
                if (ac is null)
                {
                    ac = new AdminChoice() { Nickname = info.Nickname };
                    lock (choices) choices.Add(ac);
                }

                if (msgSplit.AssertCount(1, true)) info.SendClientMessage("DeltaProxy", info.Nickname, $"[A] = = = = = Admin Module = = = = =");

                if (msgSplit.AssertCount(1)) // /admin
                {
                    info.SendClientMessage("DeltaProxy", info.Nickname, $"[A] = {(ac.ModuleChosen is null ? "Welcome to DeltaProxy Admin Panel!" : $"Currently selected module: {ac.ModuleChosen.Name}.")} Here's what you can do:");
                    info.SendClientMessage("DeltaProxy", info.Nickname, $"[A] = /admin modules -> List modules");
                    info.SendClientMessage("DeltaProxy", info.Nickname, $"[A] = /admin select [module] -> Select a module");
                    info.SendClientMessage("DeltaProxy", info.Nickname, $"[A] = /admin enable -> Enable selected module");
                    info.SendClientMessage("DeltaProxy", info.Nickname, $"[A] = /admin disable -> Disable selected module");
                    info.SendClientMessage("DeltaProxy", info.Nickname, $"[A] = /admin reload -> Reload selected module (risky!)");
                    info.SendClientMessage("DeltaProxy", info.Nickname, $"[A] = /admin load [module] -> Load a new module (risky!)");
                    info.SendClientMessage("DeltaProxy", info.Nickname, $"[A] = /admin cfg -> See all possible configuration for selected module");
                    info.SendClientMessage("DeltaProxy", info.Nickname, $"[A] = /admin cfg [name] [value] -> Change configuration value");
                    info.SendClientMessage("DeltaProxy", info.Nickname, $"[A] = /admin shutdown - Powers off DeltaProxy. Equivalent to 'exit' console command.");
                }
                if (msgSplit.AssertCount(2)) // /admin subcmd
                {
                    if (msgSplit.Assert("modules", 1))
                    {
                        foreach (var module in ModuleHandler.modules)
                        {
                            var isEnabled = ModuleHandler.CheckIsEnabledCfg(module);
                            info.SendClientMessage("DeltaProxy", info.Nickname, $"[A] = Module '{module.Name}' -> [{(isEnabled ? "ONLINE" : "OFFLINE")}]");
                        }
                    }
                    else if (msgSplit.Assert("enable", 1))
                    {
                        ToggleModule(info, ac, true);
                    }
                    else if (msgSplit.Assert("disable", 1))
                    {
                        ToggleModule(info, ac, false);
                    }
                    else if (msgSplit.Assert("reload", 1))
                    {
                        ReloadModule(info, ac);
                    }
                    else if (msgSplit.Assert("shutdown", 1))
                    {
                        ShutdownAllModules();
                    }
                    else if (msgSplit.Assert("cfg", 1))
                    {
                        if (ac.ModuleChosen is null)
                        {
                            info.SendClientMessage("DeltaProxy", info.Nickname, cfg.error_no_module); return ModuleResponse.BLOCK_PASS;
                        }

                        info.SendClientMessage("DeltaProxy", info.Nickname, $"[A] = To edit a property use /admin cfg [name] [value]. For Lists of System.String, use a comma-separated string like `connection,nickserv,chanserv`. Other, more complex data (like classes and lists of classes) is not supported yet.");

                        var cfgField = ac.ModuleChosen.GetField("cfg");
                        var cfgInstance = cfgField.GetValue(null);
                        var cfgProperties = cfgInstance.GetType().GetFields();
                        foreach (var property in cfgProperties)
                        {
                            var value = property.GetValue(cfgInstance);

                            string strValue = value.ToString();
                            if (property.FieldType == typeof(List<string>))
                            {
                                var arr = ((List<string>)value).ToArray();
                                strValue = $"new string[{arr.Length}] {{ {arr.Join(0, ',')} }}";
                            }

                            info.SendClientMessage("DeltaProxy", info.Nickname, $"[A] = Property '{property.Name}', type: {property.FieldType.Name}, value: {strValue}");
                        }
                    }
                }
                if (msgSplit.AssertCount(3)) // /admin subcmd arg
                {
                    if (msgSplit.Assert("select", 1))
                    {
                        var moduleName = msgSplit[2];
                        var module = ModuleHandler.modules.FirstOrDefault((z) => z.Name == moduleName);

                        if (module is null)
                        {
                            info.SendClientMessage("DeltaProxy", info.Nickname, cfg.error_not_found); return ModuleResponse.BLOCK_PASS;
                        }

                        ac.ModuleChosen = module;
                        info.SendClientMessage("DeltaProxy", info.Nickname, $"[A] = Successfully chosen module '{ac.ModuleChosen.Name}'!");
                    }
                    if (msgSplit.Assert("load", 1))
                    {
                        var moduleName = msgSplit[2];
                        
                        if (File.Exists($"modules/{moduleName}.dll"))
                        {
                            LoadModule(info, moduleName);
                        }
                    }
                }
                if (msgSplit.AssertCount(4, true)) // /admin subcmd arg arg ...
                {
                    if (msgSplit.Assert("cfg", 1))
                    {
                        var name = msgSplit[2];
                        var value = msgSplit[3];

                        var cfgField = ac.ModuleChosen.GetField("cfg");
                        var cfgInstance = cfgField.GetValue(null);
                        var cfgProperties = cfgInstance.GetType().GetFields();

                        var prop = cfgProperties.FirstOrDefault(x => x.Name == name);

                        if (prop is null)
                        {
                            info.SendClientMessage("DeltaProxy", info.Nickname, cfg.error_no_propery); return ModuleResponse.BLOCK_PASS;
                        }

                        TypeConverter typeConverter = TypeDescriptor.GetConverter(prop.FieldType);

                        if (prop.FieldType == typeof(List<string>))
                        {
                            var arr = value.Split(',').ToList();
                            prop.SetValue(cfgInstance, arr);
                        }
                        else
                        {
                            var obj = typeConverter.ConvertFromString(value);
                            prop.SetValue(cfgInstance, obj);
                        }

                        info.SendClientMessage("DeltaProxy", info.Nickname, $"[A] = Successfully changed the value of '{prop.Name}' in module '{ac.ModuleChosen.Name}'!");
                        SaveConfig(ac.ModuleChosen);
                    }
                }

                return ModuleResponse.BLOCK_PASS;
            }

            return ModuleResponse.PASS;
        }

        private static void LoadModule(ConnectionInfo info, string moduleName)
        {
            // let's first try to load the assembly
            var newModule = ModuleHandler.LoadModule($"modules/{moduleName}.dll");

            if (newModule is not null)
            {
                lock (ModuleHandler.modules)
                {
                    ModuleHandler.modules.Add(newModule);

                    ModuleHandler.modules = ModuleHandler.modules.OrderBy(m => ModuleHandler.GetLoadPriority(m)).ThenBy(m => m.Name).ToList();
                }

                var enable = ModuleHandler.GetEnableMethod(newModule);
                enable.Invoke(null, null);

                ModuleHandler.HashModules();

                Program.Log($"Successfully enabled {newModule.Name}...");

                info.SendClientMessage("DeltaProxy", info.Nickname, $"[A] = Successfully enabled module '{newModule.Name}'!");
            } else
            {
                info.SendClientMessage("DeltaProxy", info.Nickname, $"[A] = Fatal error encountered while trying to enable module '{newModule.Name}'!");
            }
        }

        public static void UnloadModule(ConnectionInfo info, AdminChoice ac)
        {
            if (ac.ModuleChosen is null)
            {
                info.SendClientMessage("DeltaProxy", info.Nickname, cfg.error_no_module); return;
            }

            info.SendClientMessage("DeltaProxy", info.Nickname, $"[A] = Attempting to disable '{ac.ModuleChosen.Name}'...");
            // we will first try to disable the module

            // for that, we first want to make sure method lists don't call it while it's off
            lock (ModuleHandler.hashed_client) ModuleHandler.hashed_client.RemoveAll((z) => z.DeclaringType.Name == ac.ModuleChosen.Name);
            lock (ModuleHandler.hashed_server) ModuleHandler.hashed_server.RemoveAll((z) => z.DeclaringType.Name == ac.ModuleChosen.Name);

            // then we'll call OnDisable if it's implemented.
            var disableMethod = ac.ModuleChosen.GetMethod("OnDisable", BindingFlags.Static | BindingFlags.Public);
            if (disableMethod is not null) disableMethod.Invoke(null, null);

            lock (ModuleHandler.modules) ModuleHandler.modules.RemoveAll((z) => z.Name == ac.ModuleChosen.Name);
        }

        private static void ReloadModule(ConnectionInfo info, AdminChoice ac)
        {
            // we'll try unloading the module
            UnloadModule(info, ac);

            info.SendClientMessage("DeltaProxy", info.Nickname, $"[A] = Disabled '{ac.ModuleChosen.Name}'. Trying to enable it back...");

            // then we'll try to load the assembly again
            LoadModule(info, ac.ModuleChosen.Name);
        }

        public static void SaveConfig(Type module)
        {
            var cfgField = module.GetField("cfg");
            var cfgInstance = cfgField.GetValue(null);
            var saveConfig = cfgInstance.GetType().GetMethod("SaveConfig");

            saveConfig.Invoke(cfgInstance, null);
        }

        public static void ToggleModule(ConnectionInfo info, AdminChoice ac, bool enable)
        {
            if (ac.ModuleChosen is null)
            {
                info.SendClientMessage("DeltaProxy", info.Nickname, cfg.error_no_module); return;
            }

            var cfgField = ac.ModuleChosen.GetField("cfg");
            if (cfgField is null) // if no cfg is defined, the module cannot be toggled
            {
                info.SendClientMessage("DeltaProxy", info.Nickname, enable ? cfg.error_cant_enable : cfg.error_cant_disable); return;
            }
            var cfgInstance = cfgField.GetValue(null);
            var isEnabledField = cfgField.FieldType.GetField("isEnabled");
            if (isEnabledField is null) // if no enabled field is defined, the module cannot be toggled
            {
                info.SendClientMessage("DeltaProxy", info.Nickname, enable ? cfg.error_cant_enable : cfg.error_cant_disable); return;
            }
            if (isEnabledField.FieldType == typeof(bool))
            {
                isEnabledField.SetValue(cfgInstance, (bool)enable);
                ModuleHandler.HashModules();
                info.SendClientMessage("DeltaProxy", info.Nickname, $"[A] = Module '{ac.ModuleChosen.Name}' was successfully {(enable ? "ENABLED" : "DISABLED")}!");
                SaveConfig(ac.ModuleChosen);
            }
            else
            {
                info.SendClientMessage("DeltaProxy", info.Nickname, enable ? cfg.error_cant_enable : cfg.error_cant_disable); return;
            }
        }

        public static void OnEnable()
        {
            cfg = ModuleConfig.LoadConfig("mod_admin.json");
            choices = new();
        }

        public class AdminChoice
        {
            public string Nickname;
            public Type ModuleChosen;
        }

        public class ModuleConfig : ConfigBase<ModuleConfig>
        {
            public bool isEnabled = true;
            public string error_no_module = "[A] = You've chosen no module! Use /admin select [module] first.";
            public string error_not_found = "[A] = This module was not found!";
            public string error_cant_disable = "[A] = This module CANNOT be disabled.";
            public string error_cant_enable = "[A] = This module CANNOT be enabled.";
            public string error_no_propery = "[A] = Property with this name was not found.";
        }
    }
}
