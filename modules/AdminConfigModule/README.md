# Admin Config Module

## Purpose

This module allows staff members (authenticated using Staff Auth Module) to configure and manage modules from within the IRC interface.

You can access it using /ADMIN command

## Configuration

Primary configuration file of this module is `mod_admin.json`. This module implements no database.

Configuration breakdown:
| Key | Type | Description |
|--|--|--|
| isEnabled | bool | Whether or not a module should be enabled |
| error_no_module | string | Default error text for when user had chosen no module |
| error_not_found | string | Default error text for when user is trying to choose a module that doesn't exist |
| error_cant_disable | string | Default error text for when user is trying to disable a module that cannot be disabled |
| error_cant_enable | string | Default error text for when user is trying to enable a module that cannot be enabled |