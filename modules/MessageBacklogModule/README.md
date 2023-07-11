# Message Backlog Module

## Purpose

This module is responsible for backlogging PRIVMSG messages sent to channels using modules. It is not useful on its own, however it is recommended you do not disable this module, as other modules might depend on it.

## Configuration

Primary configuration file of this module is `mod_backlog.json`. This module implements a database in `backlog_db.json`.

Configuration breakdown:
| Key | Type | Description |
|--|--|--|
| isEnabled | bool | Whether or not a module should be enabled |