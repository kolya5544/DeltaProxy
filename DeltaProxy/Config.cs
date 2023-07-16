using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DeltaProxy
{
    public class Config : ConfigBase<Config>
    {
        public string localIP = "0.0.0.0";
        public int localPort = 6697;

        public string SSLchain = "";
        public string SSLkey = "";

        public bool allowPlaintext = false;
        public int portPlaintext = 6667;

        public string serverIP = "127.0.0.1";
        public int serverPortSSL = 6697;
        public int serverPortPlaintext = 6667;
        public string serverHostname = "irc.example.com";
        public string serverPass = "deltaproxy_link";

        public bool LogRaw = true;
        public bool SendUsernameOverWebIRC = true;
    }
}
