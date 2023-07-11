# Word Blacklist Module

## Purpose

This module monitors for usage of blacklisted phrases (including RegExp expressions) in different messages. Use /blacklist as an admin to configure the wordlist.

## Configuration

Primary configuration file of this module is `mod_wordblacklist.json`. This module implements a database in `wordblacklist_db.json`.

Configuration breakdown:
| Key | Type | Description |
|--|--|--|
| isEnabled | bool | Whether or not a module should be enabled |
| checkChannels | bool | Whether or not a module should check for banned phrases in public channels |
| checkPMs | bool | Whether or not a module should check for banned phrases in private messages. Keep in mind, setting this to true will add a warning message to everyone connecting, mentioning their private messages are being checked. |
| minimumEntryLength | int | Minimum length for an entry in phrases database. It is designed to prevent accidents when a phrase too short is added, leading to accidental bans |
| warningMessage | string | Default message shown to user who violated a filter |
| kickMessage | string | Default message shown to user who violated a filter before getting kicked from the server |
| muteMessage | string | Default message shown to user who violated a filter before getting muted |
| banMessage | string | Default message shown to user who violated a filter before getting banned from the server |