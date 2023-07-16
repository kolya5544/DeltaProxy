using System.Net.Http;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Net.Security;
using static DeltaProxy.modules.ConnectionInfoHolderModule;
using System.Security.Cryptography;

namespace DeltaProxy
{
    public class Program
    {
        public static X509Certificate? serverCert;
        public static Config cfg = Config.LoadConfig("config.json");

        static void Main(string[] args)
        {
            Directory.CreateDirectory("conf");
            Directory.CreateDirectory("db");
            Directory.CreateDirectory("certs");
            Directory.CreateDirectory("usercerts");

            Log($"Enabling modules...");
            ModuleHandler.EnableModules();

            InitializeServer();
            if (cfg.allowPlaintext) InitializeServer(isSSL: false);

            while (true)
            {
                Thread.Sleep(60000);
            }
        }

        private static void InitializeServer(bool isSSL = true)
        {
            if (isSSL)
            {
                string pass = Guid.NewGuid().ToString();
                var certificate = X509Certificate2.CreateFromPemFile(cfg.SSLchain, cfg.SSLkey);
                serverCert = new X509Certificate2(certificate.Export(X509ContentType.Pfx, pass), pass);
            }

            var server = new TcpListener(IPAddress.Parse(cfg.localIP), isSSL ? cfg.localPort : cfg.portPlaintext);
            server.Start();

            Log($"Successfully initialized a TCP server (SSL = {isSSL})");

            new Thread(() =>
            {
                while (true)
                {
                    // first we initialize the client
                    var client = server.AcceptTcpClient();

                    new Thread(() => ProcessNewConnection(client, isSSL)).Start();
                }
            }).Start();
        }

        private static void ProcessNewConnection(TcpClient client, bool isSSL)
        {
            StreamWriter client_sw;
            StreamReader client_sr;

            StreamWriter server_sw;
            StreamReader server_sr;

            int defaultTimeout = 7000;
            int authedTimeout = 120000;

            string ip_address = ((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString();

            Stream? client_stream = null;
            Stream? server_stream = null;
            TcpClient server_tcp;

            Console.WriteLine($"New connection from {ip_address}");
            ConnectionInfo info = new ConnectionInfo();
            info.IP = ip_address;
            info.ConnectionTimestamp = IRCExtensions.Unix();
            info.isSSL = isSSL;

            try
            {
                // initialize the client
                X509Certificate remoteCertificate = null;
                if (isSSL)
                {
                    SslStream sslStream = new SslStream(client.GetStream(), false, VerifyClientCertificate);
                    sslStream.AuthenticateAsServer(serverCert, clientCertificateRequired: true, checkCertificateRevocation: false);
                    remoteCertificate = sslStream.RemoteCertificate;

                    client_stream = sslStream;
                } else
                {
                    client_stream = client.GetStream();
                }

                X509CertificateCollection xes = new X509CertificateCollection();
                string certHash = null;
                if (remoteCertificate is not null)
                {
                    certHash = remoteCertificate.GetCertHashString(HashAlgorithmName.SHA256).ToLower();

                    X509Certificate2 userCert = null;
                    string userCertLoc = $"usercerts/{certHash}.pem";

                    if (File.Exists(userCertLoc))
                    {
                        var data = File.ReadAllBytes(userCertLoc);
                        userCert = new X509Certificate2(data, certHash);
                    } else
                    {
                        var ecdsa = ECDsa.Create(); // generate asymmetric key pair
                        var req = new CertificateRequest($"CN={certHash}", ecdsa, HashAlgorithmName.SHA256);
                        var cert = req.CreateSelfSigned(DateTimeOffset.Now, DateTimeOffset.Now.AddYears(5));

                        var data = cert.Export(X509ContentType.Pfx, certHash); // some weird hack?
                        userCert = new X509Certificate2(data, certHash);

                        File.WriteAllBytes(userCertLoc, data);
                    }

                    xes.Add(userCert);
                    info.clientCert = certHash;
                }

                client_stream.ReadTimeout = defaultTimeout;
                client_stream.WriteTimeout = defaultTimeout;

                client_sw = new StreamWriter(client_stream); client_sw.NewLine = "\n"; client_sw.AutoFlush = true;
                client_sr = new StreamReader(client_stream);

                // initialize the connection to the server
                if (isSSL)
                {
                    server_tcp = new TcpClient(cfg.serverIP, cfg.serverPortSSL);
                    SslStream sslStream = new SslStream(server_tcp.GetStream(), false);

                    if (remoteCertificate is null)
                    {
                        sslStream.AuthenticateAsClient(cfg.serverHostname);
                    } else
                    {
                        sslStream.AuthenticateAsClient(cfg.serverHostname, xes, checkCertificateRevocation: false);
                    }

                    server_stream = sslStream;
                } else
                {
                    server_tcp = new TcpClient(cfg.serverIP, cfg.serverPortPlaintext);

                    server_stream = server_tcp.GetStream();
                }

                server_stream.ReadTimeout = authedTimeout;
                server_stream.WriteTimeout = authedTimeout;

                server_sw = new StreamWriter(server_stream); server_sw.NewLine = "\n"; server_sw.AutoFlush = true;
                server_sr = new StreamReader(server_stream);

                Exception clientException = null;

                int sentCounter = 0; // increments after every message sent FROM server TO client. Once it reaches 5, client is considered authed. Anti-random connections.

                info.Client = client;
                info.Writer = client_sw;
                info.Stream = client_stream;
                info.ServerWriter = server_sw;

                // and now we forward user -> server, indefinitely.
                new Thread(() =>
                {
                    try
                    {
                        while (true)
                        {
                            var cmd = client_sr.ReadLine();
                            if (string.IsNullOrEmpty(cmd)) throw new Exception("Broken pipe");
                            if (cfg.LogRaw) Log($"<< {cmd}");

                            // check all CLIENT-side modules
                            var moduleResponse = ModuleHandler.ProcessClientMessage(info, cmd);
                            if (moduleResponse)
                            {  // only let the message through if all modules allowed it.
                                lock (info.serverQueue) info.serverQueue.Add(cmd); 
                                info.FlushServerQueue();
                                info.FlushPostServerQueue(); // this is where we send messages buffered by plugins to be sent AFTER the initial message
                            } 
                        }
                    } catch (Exception ex)
                    {
                        Log($"{ex.Message} {ex.StackTrace} {(ex.InnerException is not null ? $"({ex.InnerException.Message} {ex.StackTrace})" : "")}");
                        clientException = ex;
                    }
                }).Start();

                // server -> user forwarding
                while (true)
                {
                    var cmd = server_sr.ReadLine();
                    if (string.IsNullOrEmpty(cmd)) throw new Exception("Broken pipe");
                    if (clientException is not null) throw clientException;
                    if (cfg.LogRaw) Log($">> {cmd}");

                    // check all SERVER-side modules
                    var moduleResponse = ModuleHandler.ProcessServerMessage(info, cmd);

                    if (moduleResponse)
                    {
                        sentCounter += 1;
                        if (sentCounter == 5)
                        {
                            client_stream.ReadTimeout = authedTimeout;
                            client_stream.WriteTimeout = authedTimeout;
                        }
                        lock (info.clientQueue) info.clientQueue.Add(cmd);
                        info.FlushClientQueue();
                        info.FlushPostClientQueue(); // this is where we send messages buffered by plugins to be sent AFTER the initial message
                    }
                }
            } catch (Exception ex)
            {
                Log($"{ex.Message} {ex.StackTrace}");
                if (ex.InnerException is not null) Log($"{ex.InnerException.Message} {ex.InnerException.StackTrace}");
            } finally
            {
                lock (connectedUsers) connectedUsers.Remove(info);
                lock (channelUsers)
                {
                    lock (info.Channels)
                    {
                        info.Channels.ForEach((z) => channelUsers[z].Remove(info));
                    }
                }
                if (client_stream is not null) client_stream.Close();
                if (server_stream is not null) server_stream.Close();
                client.Close();
            }
        }

        private static bool VerifyClientCertificate(object sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors sslPolicyErrors)
        {
            return true;
        }

        public static void Log(string msg)
        {
            string s = $"[{DateTime.UtcNow:HH:mm:ss}] {msg}";
            Console.WriteLine(s);
        }
    }
}