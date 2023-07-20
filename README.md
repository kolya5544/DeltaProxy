# DeltaProxy
Advanced bot protection and module-based enhancements for your IRC server.

**DeltaProxy** is server-side software developed in C# that acts like a **reverse proxy** to your IRC server. It is designed to enhance user experience and provide additional features by utilizing *modules*.

## Features

There are different features provided by both DeltaProxy and built-in modules implemented. Some base features provided by **DeltaProxy** are:
- *Module-based system*, with each module being a separate DLL that can be dynamically loaded or unloaded, configured, can have its own storage, and provide some additional *functionality*.
- Proper reverse proxy *certificate implementation* for IRCd that do not support IRCv3 features

And most notable features provided by modules are:
- Phrase and **RegExp-based** blacklist with an ability to choose different punishment for each offense.
- An ability to set your own **pronouns** that will appear to other users in /WHOIS
- Simple and advanced anti-bot features like different types of **captcha**
- Message backlog system for modules, VK-IRC bridge, etc. (more modules coming soon!)

## Requirements and Installation

To run DeltaProxy, you will need at least **.NET 6** (.NET 7 not tested) and an IRC server running on any daemon (only UnrealIRCd was properly tested at this point in time).

You can build DeltaProxy from **source code (preferred)** or get it through Releases. To build DeltaProxy from source code, download the repository, then navigate to DeltaProxy directory and build it:
```bash
git clone https://github.com/kolya5544/DeltaProxy.git
cd DeltaProxy/DeltaProxy
dotnet build
```

Once done, you should receive binary files located in `/DeltaProxy/bin/Debug/net6.0`

## Configuration

Once you've installed DeltaProxy, you will need to create configuration files. For modules and DeltaProxy to create the most updated config files, you will first need to run DeltaProxy like this:
`dotnet DeltaProxy.dll`

To stop DeltaProxy you can use Ctrl+C. After stopping DeltaProxy, proceed to the configuration of your instance.

You can find the most detailed configuration guide there: [Quick Start DeltaProxy Configuration Guide](https://github.com/kolya5544/DeltaProxy/blob/master/CONFIG.md)

## Usage

After configuring your instance, you can start it using the same command: `dotnet DeltaProxy.dll`. Connect to your instance using IP and port you defined in configuration.

If the configuration is correct, you should reach your IRC server through the proxy server. Now your server is using DeltaProxy! You can explore the features and enhancements of each individual module using their respectable [README files](https://github.com/kolya5544/DeltaProxy/tree/master/modules). For example, there is one for [StaffAuthModule](https://github.com/kolya5544/DeltaProxy/blob/master/modules/StaffAuthModule/README.md).

Some commands implemented by default modules include:
- /pronouns by PronounsModule
- /proxyauth by StaffAuthModule
- /blacklist by WordBlacklistModule
- /admin by AdminConfigModule

There is also a command you can type into DeltaProxy's CLI: `exit`. **Please use `exit` for safe DeltaProxy shutdowns.**

## Developing for DeltaProxy

DeltaProxy modules are separate projects that are compiled to DLL and are dynamically loaded by DeltaProxy at launch. You can find a comprehensive and complete guide on developing DeltaProxy modules [here](https://github.com/kolya5544/DeltaProxy/blob/master/DEVELOPMENT.md). Alternatively, you can learn how modules work by looking at examples. A good and well-documented example is [PronounsModule](https://github.com/kolya5544/DeltaProxy/blob/master/modules/PronounsModule/PronounsModule.cs)

## Contributions

We do not accept any big contributions or major code changes. Feel free to use GitHub Issues to report bugs and request features to be implemented. You are also free to implement modules for DeltaProxy, although they may not be included in the base repository of this project.