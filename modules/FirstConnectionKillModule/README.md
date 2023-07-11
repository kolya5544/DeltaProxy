# First Connection Kill Module

## Purpose

This is a module that kills all newly connected clients as a way to protect against bots under an assumption a bot will not reconnect using the same connection details.

## Configuration

Primary configuration file of this module is `mod_firstkill.json`. This module implements no database.

Configuration breakdown:
| Key | Type | Description |
|--|--|--|
| isEnabled | bool | Whether or not a module should be enabled |
| KillMessage | string | Default string shown to user being killed |
| IgnoreIP | bool | Whether or not to ignore IP addresses when taking kills into consideration. Setting this to true will prevent people from getting killed every time they use a new IP to connect (it is better to set this to `true` if a majority of your users use new IP for each new connection!) |