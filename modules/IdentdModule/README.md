# Identd Module

## Purpose

This module scans IDENT of newly connected users if IRCd refuses to scan IDENT of WebIRC-connected users. Allows for usage of identd.

## Configuration

Primary configuration file of this module is `mod_identd.json`. This module implements no database.

Configuration breakdown:
| Key | Type | Description |
|--|--|--|
| isEnabled | bool | Whether or not a module should be enabled |
| identdIP | string | IP address to host identd on |
| identdPort | int | Port to host identd on |