# Pronouns Module

## Purpose

This is a module that implements pronouns in /WHOIS. Allows you to set your own preferred pronouns using /pronouns command.

## Configuration

Primary configuration file of this module is `mod_pronouns.json`. This module implements a database in `pronouns_db.json`.

Configuration breakdown:
| Key | Type | Description |
|--|--|--|
| isEnabled | bool | Whether or not a module should be enabled |
| maxLength | int | Max length of a pronouns a user can set |