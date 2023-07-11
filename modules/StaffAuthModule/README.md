# Staff Auth Module

## Purpose

This is a module that is used by staff (admins and moderators) to auth as such using a password defined in the config. This module cannot be disabled. To auth, use /proxyauth password

## Configuration

Primary configuration file of this module is `mod_staff.json`. This module implements no database.

Configuration breakdown:
| Key | Type | Description |
|--|--|--|
| ShouldNicknameMatch | bool | Whether or not a nickname should match one defined in the config. If set to `false`, anyone knowing a password of any staff member can auth as staff |
| staff | List(Staff) | Implements a list of staff members that can authenticate |

Where every Staff object is defined like:
| Key | Type | Description |
|--|--|--|
| Nickname | string | Nickname of a staff member |
| Password | string | Private password of a staff member |