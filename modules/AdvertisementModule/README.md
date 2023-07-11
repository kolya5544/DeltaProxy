# Advertisement Module

## Purpose

This module adds a message to server's MOTD with a direct link to DeltaProxy's source code. This module can be disabled safely.

## Configuration

Primary configuration file of this module is `mod_advertisement.json`. This module implements no database.

Configuration breakdown:
| Key | Type | Description |
|--|--|--|
| isEnabled | bool | Whether or not a module should be enabled |
| SourceCode | string | Direct URL to where the source code of DeltaProxy can be found. |