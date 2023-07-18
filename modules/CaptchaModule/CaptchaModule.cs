using DeltaProxy.modules.Bans;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using static DeltaProxy.ModuleHandler;
using static DeltaProxy.modules.Bans.BansModule;
using static DeltaProxy.modules.ConnectionInfoHolderModule;

namespace DeltaProxy.modules.Captcha
{
    /// <summary>
    /// This CLIENT-side module implements different types of text captcha. It can be used with Anope to implement captcha on NickServ or ChanServ operations.
    /// </summary>
    public class CaptchaModule
    {
        public static ModuleConfig cfg;
        public static List<CaptchaInstance> captchas;
        public static Random rng;

        public static ModuleResponse ResolveClientMessage(ConnectionInfo info, string msg)
        {
            var msgSplit = msg.SplitMessage();

            var captchaActions = cfg.captchaActions.Split(',');

            // remove captchas with used-up passes
            if (cfg.captchaPass != -1)
            {
                lock (captchas) captchas.RemoveAll((z) => z.passUsed >= cfg.captchaPass);
            }

            string currentAction = "none";

            if (msgSplit.Assert("NICK", 0) && !info.ChangedNickname) // expecting first NICK)
            {
                currentAction = "connection";
            }
            if (msgSplit.Assert("PRIVMSG", 0) && (msgSplit.Assert("NickServ", 1) || msgSplit.Assert("ChanServ", 1)) && msgSplit.AssertCount(4, true)) // expecting a chanserv or nickserv message
            {
                string action = msgSplit[2].Trim(':').ToLower();
                if (action == "register") // expecting a register (of a nick or channel) with proper usage arguments
                {
                    currentAction = msgSplit[1].ToLower(); // using the receiver of the message for action
                }
            }

            // captcha is binded to an IP address
            CaptchaInstance activeCaptcha;
            lock (captchas) activeCaptcha = captchas.FirstOrDefault((z) => z.IP == info.IP);
            if (activeCaptcha is not null && !activeCaptcha.passed && activeCaptcha.action != currentAction)
            {
                if (IRCExtensions.Unix() - activeCaptcha.issued > cfg.timeLimit) // failed to solve captcha in time! too bad
                {
                    info.SendClientMessage("CaptchSRV", info.Nickname, cfg.no_time_msg);
                    lock (captchas) captchas.Remove(activeCaptcha);
                    BansModule.ProperDisconnect(info, "Failed to solve captcha");
                    return ModuleResponse.BLOCK_MODULES;
                }

                if (msgSplit.Assert("PRIVMSG", 0) && msgSplit.Assert("CaptchSRV", 1) && msgSplit.AssertCount(3, true)) // expect a PRIVMSG CaptchSRV with captcha solution
                {
                    string solution = msgSplit[2].Trim(':');

                    if (activeCaptcha.solution == solution) // success!
                    {
                        info.SendClientMessage("CaptchSRV", info.Nickname, cfg.success_msg);
                        activeCaptcha.passed = true;
                        if (activeCaptcha.action == "connection") activeCaptcha.passUsed += 1;
                        return ModuleResponse.BLOCK_MODULES;
                    }

                    // not success
                    if (activeCaptcha.captchaAttempts == cfg.maxAttempts)
                    {
                        info.SendClientMessage("CaptchSRV", info.Nickname, cfg.no_attempts_msg);
                        lock (captchas) captchas.Remove(activeCaptcha);
                        BansModule.ProperDisconnect(info, "Failed to solve captcha");
                        return ModuleResponse.BLOCK_MODULES;
                    }
                    info.SendClientMessage("CaptchSRV", info.Nickname, cfg.incorrect_msg);
                    AlertCaptcha(info, activeCaptcha);
                    activeCaptcha.captchaAttempts += 1;
                    return ModuleResponse.BLOCK_MODULES;
                }

                var cblockHit = cfg.preventCommands.Split(',').FirstOrDefault((z) => msgSplit.Assert(z.Trim(), 0));
                if (cfg.captchaBlock && cblockHit is not null) // a captcha block is enabled! prevent commands defined in config
                {
                    if ((cblockHit.ToLower() == "nick" && !info.ChangedNickname) || cblockHit.ToLower() != "nick")
                    {
                        AlertCaptcha(info, activeCaptcha); return ModuleResponse.BLOCK_MODULES;
                    }
                }
            }

            if (captchaActions.Contains(currentAction))
            {
                if (activeCaptcha is not null && activeCaptcha.passed && activeCaptcha.action == currentAction) { activeCaptcha.passUsed += 1; return ModuleResponse.PASS; }
                if (activeCaptcha is not null && activeCaptcha.action == currentAction) { AlertCaptcha(info, activeCaptcha); return currentAction == "connection" ? ModuleResponse.PASS : ModuleResponse.BLOCK_MODULES; }
                var newCaptcha = GenerateCaptcha(info, currentAction);
                lock (captchas) captchas.Add(newCaptcha);
                AlertCaptcha(info, newCaptcha);
                return currentAction == "connection" ? ModuleResponse.PASS : ModuleResponse.BLOCK_MODULES; // we do want to let NICK through for this
            }

            return ModuleResponse.PASS;
        }

        private static CaptchaInstance GenerateCaptcha(ConnectionInfo info, string act)
        {
            var captcha = new CaptchaInstance()
            {
                type = cfg.captchaType,
                action = act,
                issued = IRCExtensions.Unix(),
                IP = info.IP
            };

            if (cfg.captchaType == "text")
            {
                captcha.text = rng.Next(1000000, 9999999).ToString();
            }
            else if (cfg.captchaType == "math")
            {
                captcha.operandOne = rng.Next(1, 20);
                captcha.operandTwo = rng.Next(1, 20);
                captcha.mathOperator = rng.Next(0, 3);
            }

            return captcha;
        }

        private static void AlertCaptcha(ConnectionInfo info, CaptchaInstance activeCaptcha)
        {
            info.SendClientMessage("CaptchSRV", info.Nickname, ReplacePlaceholders(activeCaptcha, cfg.alert_msg));
        }

        public static string ReplacePlaceholders(CaptchaInstance captcha, string msg)
        {
            return msg.Replace("{captcha_task}", RenderTask(captcha));
        }

        public static string RenderTask(CaptchaInstance captcha)
        {
            if (captcha.type == "text") return $"Please send the next text in a PM to CaptchSRV: {captcha.solution}";
            if (captcha.type == "math") return $"Please find the result of this expression and send it in a PM to CaptchSRV: {captcha.operandOne} {captcha.mathOperatorChar} {captcha.operandTwo} = ?";
            return "N/A";
        }

        public static void OnEnable()
        {
            cfg = ModuleConfig.LoadConfig("mod_captcha.json");
            captchas = new();
            rng = new Random();
        }

        public class ModuleConfig : ConfigBase<ModuleConfig>
        {
            public bool isEnabled = false;
            public string captchaActions = "nickserv,chanserv"; // list of actions that will trigger a captcha. Available actions: "connection", "nickserv" (registration), "chanserv" (channel registration)
            public string captchaType = "text"; // available values: text (retype a code), math (a simple operation, like 21 + 9)
            public long timeLimit = 300; // 5 minutes to solve a captcha
            public long maxAttempts = 5; // how many attempts one can do before being disconnected from the server
            public long captchaPass = -1; // how many passes to ACTIONS does a user have? i.e. setting this to 2 will only let a user connect/register twice before next captcha. set to -1 for unlimited
            public bool captchaBlock = true; // should captcha block all common commands until user solves it? recommended to leave at true
            public string preventCommands = "privmsg,join,nick,part,notice,ns,cs,nickserv,chanserv"; // comma-seperated list of commands that will be blocked during a captcha block

            public string incorrect_msg = $"<!CAPTCHA!> Incorrect solution! Please try again.";
            public string no_attempts_msg = $"<!CAPTCHA!> Incorrect solution! You ran out of attempts. You will be disconnected.";
            public string no_time_msg = $"<!CAPTCHA!> You ran out of time to solve captcha. You will be disconnected.";
            public string alert_msg = "<!CAPTCHA!> You have to complete captcha before you can proceed! {captcha_task}";
            public string success_msg = "<!CAPTCHA!> Successfully completed! You can now proceed to your business as usual.";
        }

        public class CaptchaInstance
        {
            public string type;
            public string action;
            public long issued;
            public long captchaAttempts = 0;
            public string IP;

            public string? text;
            public int? operandOne;
            public int? operandTwo;
            public int? mathOperator;

            public char? mathOperatorChar
            {
                get
                {
                    switch (mathOperator)
                    {
                        case 0: return '+';
                        case 1: return '-';
                        case 2: return '*';
                    }
                    return ' ';
                }
            }

            public bool passed = false;
            public long passUsed = 0;

            public string solution
            {
                get
                {
                    if (type == "text") return text;
                    if (type == "math")
                    {
                        if (mathOperator == 0) return (operandOne + operandTwo).ToString(); // +
                        if (mathOperator == 1) return (operandOne - operandTwo).ToString(); // -
                        if (mathOperator == 2) return (operandOne * operandTwo).ToString(); // *
                    }
                    return "N/A";
                }
            }
        }
    }
}
