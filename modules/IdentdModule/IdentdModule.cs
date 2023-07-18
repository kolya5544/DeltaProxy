using DeltaProxy;
using DeltaProxy.modules.Bans;
using DeltaProxy.modules.FakeWhoNamesWhois;
using Newtonsoft.Json;
using System.Net;
using System.Net.Sockets;
using static DeltaProxy.ModuleHandler;
using static DeltaProxy.modules.ConnectionInfoHolderModule;

namespace DeltaProxy.modules.Identd
{
    public class IdentdModule
    {
        public static int CLIENT_PRIORITY = 3; // just so it doesn't break
        public static int SERVER_PRIORITY = 3;

        public static ModuleConfig cfg;
        public static TcpListener tcp;
        public static CancellationTokenSource cts;

        public static void HandleIncomingIdentd(TcpClient server)
        {
            try
            {
                var server_ns = server.GetStream(); // this stream belongs to SERVER. This is where we'll be pulling requests from
                server_ns.ReadTimeout = 2000;
                server_ns.WriteTimeout = 2000;
                var server_sr = new StreamReader(server_ns);
                var server_sw = new StreamWriter(server_ns); server_sw.NewLine = "\n"; server_sw.AutoFlush = true;

                string req = server_sr.ReadLine();

                Program.Log($"Got from server: {req}");

                ConnectionInfo? info = null;

                string[] param = req.Split(',');
                int localPort = int.Parse(param[1]);
                int ircDRemotePort = int.Parse(param[0]);

                int realProxyPort = Program.cfg.serverPortSSL == localPort ? Program.cfg.localPort : Program.cfg.portPlaintext;

                for (int att = 0; att < 3; att++)
                {
                    Thread.Sleep(250);
                    lock (Program.allConnections) info = Program.allConnections.FirstOrDefault((z) => z.localPort == realProxyPort && z.localPort_IRCd == ircDRemotePort);
                    if (info is not null) break;
                }

                if (info is null) { 
                    Program.Log($"Failed to find user with localPort == {realProxyPort} and IRCd port == {ircDRemotePort}");
                    lock (Program.allConnections) Program.allConnections.ForEach((z) => Program.Log($"{z.IP} {z.localPort} {z.localPort_IRCd}"));
                    return; 
                }

                string vrf = VerifyIdentdUsername(info);

                if (vrf is not null) server_sw.WriteLine(vrf); // woo! done?
            } catch
            {
                // fuck identd if nothing works
            } finally
            {
                if (server is not null) server.Close();
            }
        }

        public static string? VerifyIdentdUsername(ConnectionInfo info)
        {
            // connect
            string ip = info.IP;
            int port = 113;
            TcpClient tcp = null;

            try
            {
                tcp = new TcpClient();
                tcp.ConnectAsync(ip, port).Wait(2500);

                var ns = tcp.GetStream();

                ns.ReadTimeout = 2500; ns.WriteTimeout = 2500;

                var sr = new StreamReader(ns);
                var sw = new StreamWriter(ns); sw.AutoFlush = true; sw.NewLine = "\n";

                sw.WriteLine($"{info.remotePort}, {info.localPort}");
                string response = sr.ReadLine();

                Program.Log($"{response} @ {ip}:{port}!");

                return response;
            }
            catch (Exception ex)
            {
                Program.Log($"{ex.Message} @ {ip}:{port}!");
                return null;
            }
            finally
            {
                if (tcp is not null) tcp.Close();
            }
        }

        public static void OnEnable()
        {
            cfg = ModuleConfig.LoadConfig("mod_identd.json");

            cts = new CancellationTokenSource();

            tcp = new TcpListener(IPAddress.Parse(cfg.identdIP), cfg.identdPort);
            tcp.Start();

            new Thread(() =>
            {
                try
                {
                    while (true)
                    {
                        if (cts.IsCancellationRequested) return;

                        var client = tcp.AcceptTcpClient();

                        if (cts.IsCancellationRequested) return;

                        new Thread(() => HandleIncomingIdentd(client)).Start();
                    }
                } catch
                {

                }
               
            }).Start(); 
        }

        public static void OnDisable()
        {
            tcp.Stop();
            cts.Cancel();
        }

        public class ModuleConfig : ConfigBase<ModuleConfig>
        {
            public bool isEnabled = false;
            public string identdIP = "127.0.0.1";
            public int identdPort = 113;
        }
    }
}