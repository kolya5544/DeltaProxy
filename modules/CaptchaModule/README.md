# Captcha Module

## Purpose

This module implements different types of text captcha. It can be used with Anope to implement captcha on NickServ or ChanServ operations.

## Configuration

Primary configuration file of this module is `mod_captcha.json`. This module implements no database.

Configuration breakdown:
| Key | Type | Description |
|--|--|--|
| isEnabled | bool | Whether or not a module should be enabled |
| captchaActions | string | A comma-separated string of actions that will trigger a captcha. Supported values are: connection, nickserv (for REGISTERing a nickname), chanserv (for REGISTERing a channel) |
| captchaType | string | A type of captcha that will be used. Can be text or math. Text captcha requires you to re-type a string of digits, and math captcha requires you to solve a math equation |
| timeLimit | int | A time limit imposed on users to solve captcha. Inability to do so within time set results in a server disconnect |
| maxAttempts | int | Max attempts to solve captcha. Failure to do so within this amount of attempt will result in a server disconnect |
| captchaPass | int | How many actions of captchaActions will a user be able to complete until the next time a captcha appears? For example a captchaPass of 1 and captchaActions of | nickserv|  will only allow person to REGISTER once, with his next REGISTER attempt causing a new captcha. Use -1 for unlimited. |
| captchaBlock | bool | Will active captcha block all other commands (defined in a field below) until when a captcha is solved? Useful to prevent bots from ignoring connection captcha |
| preventCommands | string | A comma-separated string of IRC commands that will be blocked during when a captcha is active |
| incorrect_msg | string | Default message shown to user for incorrect captcha solution |
| no_attempts_msg | string | Default message shown to user for running out of attempts solving captcha |
| no_time_msg | string | Default message shown to user who ran out of time solving captcha |
| alert_msg | string | Default message shown to user to solve captcha. Placeholder of {captcha_task} is supported |
| success_msg | string | Default message shown to user for successfully solving captcha | 