﻿using System.Net.Http;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Net.Security;
using static DeltaProxy.ConnectionInfoHolderModule;

namespace DeltaProxy
{
    internal class Program
    {
        public static X509Certificate? serverCert;
        public static Config cfg = Config.LoadConfig("config.json");

        static void Main(string[] args)
        {
            Directory.CreateDirectory("conf");
            Directory.CreateDirectory("db");
            Directory.CreateDirectory("certs");

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

            int defaultTimeout = 4000;
            int authedTimeout = 120000;

            string ip_address = ((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString();
            string hostname = Dns.GetHostByAddress(ip_address).HostName;

            Stream? client_stream = null;
            Stream? server_stream = null;
            TcpClient server_tcp;

            Console.WriteLine($"New connection {ip_address} & {hostname}");
            ConnectionInfo info = new ConnectionInfo();
            info.IP = ip_address;
            info.ConnectionTimestamp = IRCExtensions.Unix();

            try
            {
                // initialize the client
                if (isSSL)
                {
                    SslStream sslStream = new SslStream(client.GetStream(), false);
                    sslStream.AuthenticateAsServer(serverCert, clientCertificateRequired: false, checkCertificateRevocation: true);

                    client_stream = sslStream;
                } else
                {
                    client_stream = client.GetStream();
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
                    sslStream.AuthenticateAsClient(cfg.serverHostname);

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

                // WebIRC auth
                server_sw.WriteLine($"WEBIRC {cfg.serverPass} cgiirc {hostname} {ip_address}");

                Exception clientException = null;

                int sentCounter = 0; // increments after every message sent to client by server. Once it reaches 10, client is considered authed. Anti-random connections.

                info.Client = client;
                info.Writer = client_sw;
                info.Reader = client_sr;
                info.Stream = client_stream;

                // and now we forward user -> server, indefinitely.
                new Thread(() =>
                {
                    try
                    {
                        while (true)
                        {
                            var cmd = client_sr.ReadLine();
                            if (string.IsNullOrEmpty(cmd)) throw new Exception("Broken pipe");
                            Log($"<< {cmd}");

                            // check all CLIENT-side modules
                            var moduleResponse = ModuleHandler.ProcessClientMessage(info, cmd);
                            if (moduleResponse) { server_sw.WriteLine(cmd); } // only let the message through if all modules allowed it.
                        }
                    } catch (Exception ex)
                    {
                        clientException = ex;
                    }
                }).Start();

                // server -> user forwarding
                while (true)
                {
                    var cmd = server_sr.ReadLine();
                    if (string.IsNullOrEmpty(cmd)) throw new Exception("Broken pipe");
                    if (clientException is not null) throw clientException;
                    Log($">> {cmd}");

                    // check all SERVER-side modules
                    ModuleHandler.ProcessServerMessage(info, cmd);

                    sentCounter += 1;
                    if (sentCounter == 10)
                    {
                        client_stream.ReadTimeout = authedTimeout;
                        client_stream.WriteTimeout = authedTimeout;
                    }
                    client_sw.WriteLine(cmd);
                }
            } catch (Exception ex)
            {
                Console.WriteLine($"{ex.Message} {ex.StackTrace}");
            } finally
            {
                client_stream.Close();
                server_stream.Close();
                client.Close();
            }
        }

        public static void Log(string msg)
        {
            string s = $"[{DateTime.UtcNow:HH:mm:ss}] {msg}";
            Console.WriteLine(s);
        }
    }
}