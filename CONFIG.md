# DeltaProxy Configuration 101

This page is designed to help you configure your DeltaProxy instance. Please read it carefully to avoid future service interruptions.

## Getting configuration files created

DeltaProxy and modules configuration files are located in `/conf` directory located in the same directory as one where the binary file `DeltaProxy.dll` is. If you can't find this directory, you didn't run DeltaProxy yet. You have to do so using this command:
```bash
dotnet DeltaProxy.dll
```
After the server starts, shut it down using Ctrl+C. After that, you should see several directories appear. Change your current directory to `/conf`.

## Configuration files structure

Configuration file is a JSON-formatted text file. Such file can belong either to a module (in which case its filename will begin with `mod_` and will slightly resemble the name of the module that manages it), or to base application itself, in which case config will be named `config.json`.

Configuration files are loaded once when a module gets enabled (or reloaded). While it is possible to edit configuration files from within the proxy itself (using `AdminConfigModule`), **it is recommended you properly set server up before launching it to avoid future server interruptions.**

## IRCd configuration

Before we begin configuring proxy, we must make sure IRCd is properly configured. First of all, you will have to set up [WebIRC](https://www.unrealircd.org/docs/WebIRC_block#UnrealIRCd-side). **Unfortunately, IRCd without WebIRC support are not supported!**

You are also **heavily recommended** to set up the `throttle` exemption and `ban` exemption blocks on WebIRC IP address (`127.0.0.1` in case your proxy runs on the same server as IRCd). Here's an example of how it's done in UnrealIRCd:
```
webirc {
	mask 127.0.0.1;
	password "some_secret_password";
};

except throttle {
	mask { 127.0.0.1; }
};
except ban {
        mask 127.0.0.1;
        type { connect-flood; handshake-data-flood; blacklist; };
};
```

Besides that, **you will want to limit non-proxified access to your IRCd** to ensure all traffic flows through DeltaProxy. If your DeltaProxy instance is running on the same server as IRCd, you can just make IRCd listen to localhost only:
```
listen {
        ip 127.0.0.1;
        port 6667;
}
listen {
        ip 127.0.0.1;
        port 6697;
        options { tls; ssl; };
        tls-options {
                certificate "...";
                key "...";
        }
}
```

**For cases where DeltaProxy and IRCd are located on different servers, please use firewall instructions to limit server access for everyone except DeltaProxy!**

Once you've set those up, you're ready to continue to the next step.

## Server configuration file (config.json)

`config.json` is the primary server configuration file. A server will not work correctly until you properly set the values inside of this file.

Here is a list of values contained within `config.json`:

| Key | Type | Description |
|--|--|--|
| localIP | string | IP address at which proxy will LISTEN. Use 0.0.0.0 for all, or 127.0.0.1 for localhost |
| localPort | int | SSL port at which proxy will LISTEN |
| SSLchain | string | Relative or absolute path to where SSL certificate (full chain) is located |
| SSLkey | string | Relative or absolute path to where SSL private key is located |
| allowPlaintext | bool | Whether or not proxy will support plaintext (non-SSL) connections |
| portPlaintext | int | Port at which proxy will LISTEN for plaintext connections |
| serverIP | string | IP to which the proxy will CONNECT to for connections. It is supposed to point to your IRC daemon's IP address (usually `127.0.0.1`, if you run proxy and IRCd on the same server) |
| serverPortSSL | int | SSL port to which the proxy will CONNECT to for connections |
| serverPortPlaintext | int | Plaintext port to which the proxy will CONNECT to for connections | serverHostname | string | Hostname that will be used by proxy. Has to match the actual hostname of server defined in your IRCd configuration! |
| serverPass | string | WEBIRC password you've defined in your IRCd configuration. [Don't know what this is?](https://www.unrealircd.org/docs/WebIRC_block#UnrealIRCd-side) |
| LogRaw | bool | Whether or not server will log raw TCP requests for debugging and troubleshooting purposes |
| SendUsernameOverWebIRC | bool | Whether or not server will send new client's USERname over WebIRC instead of default gateway name of "deltaproxy". Set this to true if all new users' usernames appear to be "deltaproxy" |

Set all the values in accordance to your needs. At this stage, DeltaProxy should already work out of the box. You can confirm that by saving the config file and launching DeltaProxy again:
```
dotnet DeltaProxy.dll
```
and then connecting to the `localIP`:`localPort` (with SSL enabled) pair you've defined in `config.json`. If your `config.json` is correct, you will successfully establish a connection to the server and will be able to use it normally. From now on, your connection will go through DeltaProxy.

## Module configuration

Each individual module has a configuration file. If a module lacks configuration file, it cannot be configured. You are **strongly recommended** to restart your DeltaProxy instance after editing modules. It ensured all the modules are properly reloaded by their respective plugins.

By default, some modules are enabled (like `BansModule`) and some are not (like `VKBridgeModule`). To enable a module, you need to set `isEnabled` property, which is present in all modules that can be disabled, to `true`. Make sure you also configure the respective module before enabling it to prevent any issues.

You can learn more about each specific module's configuration, purpose and details in [module's respective README file](https://github.com/kolya5544/DeltaProxy/blob/master/modules/StaffAuthModule/README.md).

You can find the list of all modules there: [List of all modules](https://github.com/kolya5544/DeltaProxy/tree/master/modules)

## Module databases

Some modules (like `BansModule`) create and manage their own databases. You can find databases in `/db` directory located in the root of DeltaProxy instance. Every database is a JSON text file. It is generally considered safe to manually edit or remove databases located here, however you should first shut down your DeltaProxy instance before interacting with databases to avoid service issues.

## Troubleshooting (FAQ)

#### Q: DeltaProxy doesn't even start - I get a `Socket address already in use` exception.

A: The ports or IP address you've defined in `localPort`, `portPlaintext` and `localIP` is not available. It is possible your IRCd already occupies these ports - make sure your IRCd doesn't listen on the same IP:port as your DeltaProxy instance (keep in mind, IRCd listening on `0.0.0.0` will occupy all IP addresses, even `127.0.0.1`!).

#### Q: I cannot connect to the server using DeltaProxy.

A: Make sure you've configured WebIRC properly and that your IRCd supports it. Make sure `serverPass` in your configuration matches one you've defined in your IRCd. Make sure there is no firewall misconfiguration. Make sure WebIRC blocks IP-match your DeltaProxy configuration (for example, if you connect to server by **specifying its IP address**, rather than using `127.0.0.1`, you'll have to **WebIRC-allow your server's IP address**, rather than localhost)

#### Q: Everything appears to be kind of working, but I occasionally get random errors DeltaProxy-side/it crashes/getting very frequent network-wide disconnects

A: It is possible your IRCd is not fully supported by DeltaProxy. Currently only a very small fraction of IRCd were tested for compatibility with DeltaProxy, and it's possible your one is not. Please, feel free to create bug reports on GitHub Issues!

If you believe your issue fits neither of these categories, you can always get live support from developers of DeltaProxy. You can find me at my IRC server at `irc.nk.ax:+6697` SSL or `:6667` plaintext in #chat. My nickname is `kolya` there and I will assist you in any way I can.

#### Q: Ident doesn't work properly!

A: Enable and properly configure the [IdentdModule](https://github.com/kolya5544/DeltaProxy/tree/master/modules/IdentdModule)