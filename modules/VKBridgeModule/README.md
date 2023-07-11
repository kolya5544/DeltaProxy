# VK Bridge Module

## Purpose

This module allows DeltaProxy to set up an IRC to VK bridge in the specified channel.

## Configuration

Primary configuration file of this module is `mod_vkbridge.json`. This module implements a database in `vkbridge_db.json`.

Configuration breakdown:
| Key | Type | Description |
|--|--|--|
| isEnabled | bool | Whether or not a module should be enabled |
| vkToken | string | Token of a VK bot that will manage the bridge, send messages. Can be acquired in VK group settings |
| vkGroup | int | ID of a VK group the bot is assigned to. Should NOT include a negative sign |
| vkChat | int | ID of the public chat with the bot in it. By default it is 2000000001 - but you will have to find out your own specific chat ID if it isn't the first chat the bot is in |
| vkBotName | string | The public and complete group name of the bot sending messages |
| ircChat | string | IRC-sided channel where the bridge will be located |
| ircSpoofUsers | bool | Make VK-sided users appear as IRC users. Do NOT touch this setting, it was NOT tested at all |
| collisionPrefix | string | Prefix to be given to users whose usernames match ones of people currently online on IRC. Choose wisely - no person will be able to connect with a prefix you choose!! |
| sendOnlineUpdatesIRC | bool | Update VK users whose online status had changed by changing their VOICE status respectfully (+v for online, -v for offline users) |
| allowOnline | bool | Allow the use of /online command VK-side |
| allowIgnoreVK | bool | Allow the use of /ignoreme command VK-side (TODO) |
| allowIgnoreIRC | bool | Allow the use of /ignoreme command IRC-side (TODO) |
| allowUserVK | bool | Allow the use of /user command VK-side (TODO) |
| allowUserIRC | bool | Allow the use of /whois command IRC-side on VK users (TODO) |
| allowPMs | bool | Allow the use of personal messages from VK to IRC and vice versa (TODO) |
| enableURLcache | bool | Enables a local web server to store and shorten long VK URLs by assigning them a shorter ID |
| publicURL | string | Public URL of such web server. It is assumed you use other means of linking a public server and local cache, for example, using nginx. An ID will be appended to the end of this public URL |
| cacheServer | string | HTTP URL at which local web server will listen. It is highly recommended to use 127.0.0.1 here and use other web servers (like nginx) to point to here |
| storageTime | int | How long to store links for |
| useBacklog | bool | Shall we store messages in a special backlog and send them back to newly connected users (IRC-only)? Only works if MessageBacklogModule is enabled. |
| backlogStoreTime | int | How long for, in seconds, should messages in backlog be stored? |
| backlogStoreAmount | int | Amount of messages that should be stored in backlog |