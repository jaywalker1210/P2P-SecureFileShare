using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Diplom.Services
{
    internal class DiscoveryService: IDisposable
    {
        private UdpClient _udpClient;
        private UdpClient _udpListener;
        private CancellationTokenSource _cts;
        private readonly int _discoveryPort = 8889;
        private readonly int _broadcastInterval = 5000;
        private readonly int _peerTimeout = 15000;

        public event Action<string, string, string, string, string> PeerDiscovered;

        private Dictionary<string, DateTime> _lastseen = new Dictionary<string, DateTime>();

        private Timer _cleanupTimer;

        public string MyPublicKeyBase64 { get; set; } = string.Empty;

        public void StartDiscovery(string myName)
        {
            _cts = new CancellationTokenSource();

            Task.Run(() => ListenForPeersAsync(myName, _cts.Token));

            Task.Run(() => BroadcastPresenceAsync(myName, _cts.Token));

            _cleanupTimer = new Timer(CleanupOfflinePeers, null, _peerTimeout, _peerTimeout);
        }

        public void BroadcastStatus(string myName, string status)
        {
            try
            {
                using (var client=new UdpClient())
                {
                    client.EnableBroadcast = true;
                    var endpoint = new IPEndPoint(IPAddress.Broadcast, _discoveryPort);

                    string message = $"P2P:STATUS:{myName}:{GetMyIP()}:{status}|{MyPublicKeyBase64}";
                    byte[] data = Encoding.UTF8.GetBytes(message);

                    client.Send(data, data.Length, endpoint);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Broadcast status error: {ex.Message}");
            }
        }

        private async Task BroadcastPresenceAsync(string myName, CancellationToken token)
        {
            using (var client = new UdpClient())
            {
                client.EnableBroadcast = true;
                var endpoint = new IPEndPoint(IPAddress.Broadcast, _discoveryPort);

                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        string message = $"P2P:DISCOVERY:{myName}:{GetMyIP()}|{MyPublicKeyBase64}";
                        byte[] data = Encoding.UTF8.GetBytes(message);

                        await client.SendAsync(data, data.Length, endpoint);

                        await Task.Delay(_broadcastInterval, token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Broadcast error: {ex.Message}");
                    }
                }
            }
        }

        private async Task ListenForPeersAsync(string myName, CancellationToken token)
        {
            using (var listener = new UdpClient(_discoveryPort))
            {
                var myIPs = GetAllMyIPs();
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        var result = await listener.ReceiveAsync();
                        string message = Encoding.UTF8.GetString(result.Buffer);
                        string senderIP = result.RemoteEndPoint.Address.ToString();

                        if (myIPs.Contains(senderIP))
                            continue;

                        ProcessDiscoveryMessage(message, senderIP, myName);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Listen error: {ex.Message}");
                    }
                }
            }
        }
        
        private List<string> GetAllMyIPs()
        {
            var ips = new List<string>();
            try
            {
                var host = Dns.GetHostEntry(Dns.GetHostName());
                foreach (var ip in host.AddressList)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork)
                    {
                        ips.Add(ip.ToString());
                    }
                }
            }
            catch { }

            ips.Add("127.0.0.1");
            ips.Add("192.168.56.1");

            return ips;
        }

        private void ProcessDiscoveryMessage(string message, string senderIP, string myName)
        {
            System.Diagnostics.Debug.WriteLine($"UDP от {senderIP}: {message}");
            if (message.StartsWith("P2P:DISCOVERY:"))
            {
                string withoutPrefix = message.Substring("P2P:DISCOVERY:".Length);
                int separatorIndex = withoutPrefix.LastIndexOf('|');

                string nameAndIP;
                string peerPublicKey = "";

                if (separatorIndex > 0)
                {
                    nameAndIP = withoutPrefix.Substring(0, separatorIndex);
                    peerPublicKey = withoutPrefix.Substring(separatorIndex + 1);
                }
                else
                {
                    nameAndIP = withoutPrefix;
                }

                string[] parts = nameAndIP.Split(':');
                string peerName = parts.Length >= 1 ? parts[0] : "Unknown";
                string peerIP = parts.Length >= 2 ? parts[1] : senderIP;

                _lastseen[senderIP] = DateTime.Now;
                PeerDiscovered?.Invoke(peerName, peerIP, "Online", "ClientOnly", peerPublicKey);
                RespondToPeer(senderIP, myName);
            }
            else if (message.StartsWith("P2P:RESPONSE:"))
            {
                string withoutPrefix = message.Substring("P2P:RESPONSE:".Length);
                int separatorIndex = withoutPrefix.LastIndexOf('|');

                string nameAndIP = separatorIndex > 0 ? withoutPrefix.Substring(0, separatorIndex) : withoutPrefix;
                string peerPublicKey = separatorIndex > 0 ? withoutPrefix.Substring(separatorIndex + 1) : "";

                string[] parts = nameAndIP.Split(':');
                string peerName = parts.Length >= 1 ? parts[0] : "Unknown";
                string peerIP = parts.Length >= 2 ? parts[1] : senderIP;

                _lastseen[senderIP] = DateTime.Now;
                PeerDiscovered?.Invoke(peerName, peerIP, "Online", "ClientOnly", peerPublicKey);
            }
            else if (message.StartsWith("P2P:STATUS:"))
            {
                string withoutPrefix = message.Substring("P2P:STATUS:".Length);

                int separatorIndex = withoutPrefix.LastIndexOf('|');

                string statusAndIP;
                string peerPublicKey = "";

                if (separatorIndex > 0)
                {
                    statusAndIP = withoutPrefix.Substring(0, separatorIndex);
                    peerPublicKey = withoutPrefix.Substring(separatorIndex + 1);
                }
                else
                {
                    statusAndIP = withoutPrefix;
                }

                string[] parts = statusAndIP.Split(':');
                string peerName = parts.Length >= 1 ? parts[0] : "Unknown";
                string peerIP = parts.Length >= 2 ? parts[1] : senderIP;
                string peerStatus = parts.Length >= 3 ? parts[2] : "ClientOnly";

                _lastseen[senderIP] = DateTime.Now;
                PeerDiscovered?.Invoke(peerName, senderIP, "Online", peerStatus, peerPublicKey);
            }
        }

        private void RespondToPeer(string peerIP, string myName)
        {
            try
            {
                using (var client = new UdpClient())
                {
                    var endpoint = new IPEndPoint(IPAddress.Parse(peerIP), _discoveryPort);
                    string message = $"P2P:RESPONSE:{myName}:{GetMyIP()}|{MyPublicKeyBase64}";
                    byte[] data = Encoding.UTF8.GetBytes(message);

                    client.Send(data, data.Length, endpoint);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Response error: {ex.Message}");
            }
        }

        private void CleanupOfflinePeers(object state)
        {
            var now = DateTime.Now;
            var offlinePeers = new List<string>();

            foreach (var kvp in _lastseen)
            {
                if ((now - kvp.Value).TotalMilliseconds > _peerTimeout)
                {
                    offlinePeers.Add(kvp.Key);
                }
            }

            foreach (var ip in offlinePeers)
            {
                _lastseen.Remove(ip);
                PeerDiscovered?.Invoke("", ip, "Offline", "", "");
            }
        }

        private string GetMyIP()
        {
            try
            {
                using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0))
                {
                    socket.Connect("8.8.8.8", 65530);
                    var endPoint = socket.LocalEndPoint as IPEndPoint;
                    if (endPoint != null)
                    {
                        string ipStr = endPoint.Address.ToString();
                        if (!ipStr.StartsWith("192.168.56.1") ||
                            !ipStr.StartsWith("172.") ||
                            !ipStr.StartsWith("169.254."))
                        {
                            return ipStr;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GetMyIP error: {ex.Message}");
            }
            return "127.0.0.1";
        }

        public void StopDiscovery()
        {
            _cts?.Cancel();
            _cleanupTimer?.Dispose();
        }

        public void Dispose()
        {
            StopDiscovery();
            _udpClient?.Dispose();
            _udpListener?.Dispose();
        }
    }
}
