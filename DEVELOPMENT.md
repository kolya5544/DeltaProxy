# Developing for DeltaProxy

You can enhance experience of DeltaProxy users by developing new useful modules. A module is a DLL that is loaded by DeltaProxy at runtime. A module acts as a separate program that can access and edit any data of any other module, as well as base DeltaProxy code, module manager, built-in ConnectionInfoHolder module and different type extensions.

## Creating a module

**Visual Studio is recommended**, and thus this entire guide will assume a modern version of Visual Studio Community is being used to create modules. Open DeltaProxy solution file `DeltaProxy.sln` in your IDE.

To create a module you need to create a new C# project, marked as `Class Library` (DLL). For that, you need to right click `DeltaProxy` solution in `Solution Explorer` (usually located in the right side of IDE), then go through `Add` -> `New Project` -> `Class Library`. You can change the project name as you will, however modules are commonly named like `Name + "Module"`, like `MyNewModule`. **Ensure the project file for your module is located in its own separate directory anywhere but inside `DeltaProxy` directory** (`DeltaProxy.sln` CANNOT be in a directory that is a part of module's filepath), `/modules/MyNewModule/` might be a good choice. If given choice to create new solution, choose **Add to solution** instead.

You will now be presented with your own module with a single `Class1.cs` file. Rename it to a more appropriate name (for example, `MyNewModule.cs`). Now, you will need to reference DeltaProxy in your project. Keep in mind, you will also have to reference any other modules this way, if your module will depend on any other modules! To do so, find `Dependencies` under your module's project file in `Solution Explorer`, right click it, choose "Add project reference", and add a reference to `DeltaProxy`. From now on, you're ready to code the logic of your future module

## Requirements for a module to qualify as such

Every module consists of several classes. One of module's classes is considered **primary** and should contain several system methods and (optionally) system properties to qualify as a proper module. This class will also have to reference DeltaProxy's only built-in module. It is designed to keep track of connected users for all the other modules: `using static DeltaProxy.modules.ConnectionInfoHolderModule;`

Bare minimum a module needs is a **`public static void OnEnable()`** method located in any class. The class containing **OnEnable** method is considered **primary** and should contain the rest of system methods and properties.

The rest of base methods define the behaviour of the module, and are OPTIONAL:
- If the primary class implements **`public static ModuleResponse ResolveServerMessage(ConnectionInfo info, string msg)`**, it will receive a call for every message sent FROM server TO client. A module can return any of ModuleResponse values, which will define further proxy behaviour.
- If the primary class implements **`public static ModuleResponse ResolveClientMessage(ConnectionInfo info, string msg)`**, it will receive a call for every message sent FROM client TO server. A module can return any of ModuleResponse values, which will define further proxy behaviour.
- If the primary class implements **`public static ModuleConfig cfg`** (with this exact field name!), where `ModuleConfig` is defined like `public class ModuleConfig : ConfigBase<ModuleConfig>`, the module is considered **configurable** and can be configured by admins using modules like `AdminConfigModule`. Additionally, if `ModuleConfig` implements **`public bool isEnabled = true`**, DeltaProxy will only load the module while `isEnable` is set to true. **It is important that you DO NOT initialize `cfg` and other non-static class fields in class constructor, but do it in OnEnable instead, to allow the module to be safely disabled and enabled**
- If the primary class implements **`public static Database db`**, where `Database` is defined like `public class Database : DatabaseBase<Database>`, the module will have its own **database**, where different types of data can be stored. Keep in mind you will still have to regularly call `db.SaveDatabase()` to avoid data loss, and you will also need to save the database on server shutdown.
- If the primary class implements `public static int CLIENT_PRIORITY = int.MaxValue / 2;`, this module will have a *client priority value* of one defined . Modules with *lowest* numbers get executed first when processing relevant messages. **`ConnectionInfoHolderModule`** has a *client priority* of 0.
- If the primary class implements `public static int SERVER_PRIORITY = int.MaxValue / 2;`, this module will have a *server priority value* of one defined. Modules with *lowest* numbers get executed first when processing relevant messages. **`ConnectionInfoHolderModule`** has a *server priority* of 0.
- If the primary class implements `public static int LOAD_PRIORITY = int.MaxValue / 2;`, this module will have a *load priority value* of one defined. Modules with *lowest* numbers get enabled first. **`ConnectionInfoHolderModule`** is *always* executed first. **`MessageBacklogModule`** has a *load priority* of 0, and thus will usually be enabled first.
- If the primary class implements `public static void OnDisable()`, this method will be run **both for DeltaProxy shutdowns and module reloads**. OnDisable must terminate any code running in the background, save the database and cancel tokens issued in OnEnable.

Variables defined here will affect the workflow of other modules. It is recommended to leave *priorities* at their default values unless it is REQUIRED that a module gets run before/after any other module. 

It is also important you initialize *any necessary variables* in **OnEnable()**, rather than class itself, to allow module to be safely *re-enabled* by calling **OnEnable()**. Don't forget to properly dispose of said variables in **OnDisable()** method!

## Module Responses

A module can return one of the following values:
- **ModuleResponse.PASS**, in which case the response will be passed to the next module with *higher priority* in line - or server/client if no modules are left.
- **ModuleResponse.BLOCK_PASS**, in which case the response will be passed to the next module with *higher priority* in line, but will not be passed to the server/client if no modules are left. *This is recommended for command handling.*
- **ModuleResponse.BLOCK_MODULES**, in which case the response will be prevented from reaching the next module with *higher priority*, as well as server/client. However queue and post-queue will still be sent if they were defined by modules with *lower priority*. *This is recommended for bans, or other actions after which the user is guaranteed to be disconnected*
- **ModuleResponse.BLOCK_ALL**, in which case the response will not reach any other modules, the server/client. All queued requests will be dropped, also.

## Using a module

Once you've implemented your module, you should be able to compile it and get a resulting .dll file. Now you can put your module in `/modules` of a DeltaProxy instance, and it should be enabled on proxy startup. If your module requires any dependencies, they must be put in `/deps` directory of a DeltaProxy instance.

## Troubleshooting

You can use `Log(string)` method implemented in DeltaProxy to log error messages and exception contents to simplify your troubleshooting process.