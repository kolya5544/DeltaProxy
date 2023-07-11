# Bans Module

## Purpose

This module keeps track of banned and muted (revoked talking rights) users for other modules. An important dependency of all modules. It is NOT recommended to disable this module.

## Configuration

Primary configuration file of this module is `mod_bans.json`. This module implements a database in `bans_db.json`.

Configuration breakdown:
| Key | Type | Description |
|--|--|--|
| isEnabled | bool | Whether or not a module should be enabled |
| talkMutedMessage | string | Default message displayed to people who tried to chat while muted. Supports {duration}, {reason} and {issuer} placeholders |
| banConnectMessage | string | Default message displayed to people who tried to connect while banned. Supports {duration}, {reason} and {issuer} placeholders |