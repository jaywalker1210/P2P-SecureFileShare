using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Diplom.Services
{
    internal class DiscoveryService: IDisposable
    {
        private UdpClient _udpClient;
        private UdpClient _udpListener;
        private CancellationTokenSource _cts;
        private readonly int _discoveryPort = 8889; // порт для обнаружения через UDP
        private readonly int _broadcastInterval = 5000; // 5 секунд 
        private readonly int _peerTimeout = 15000; // 15 секунд без ответа = офлайн

        public event Action<string, string, string> PeerDiscovered; // событие для уведомления об обнаружении нового пира (имя, IP, статус)

        private Dictionary<string, DateTime> _lastseen = new Dictionary<string, DateTime>();

        private Timer _cleanupTimer; // таймер для проверки онлайн статуса

        public void StartDiscovery(string myName)
        {
            _cts = new CancellationTokenSource();

            Task.Run(() => ListenForPeersAsync(myName, _cts.Token));

            Task.Run(() => BroadcastPresenceAsync(myName, _cts.Token));

            _cleanupTimer = new Timer(CleanupOfflinePeers, null, _peerTimeout, _peerTimeout);
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
                        string message = $"P2P:DISCOVERY:{myName}:{GetMyIP()}";
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
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        var result = await listener.ReceiveAsync();
                        string message = Encoding.UTF8.GetString(result.Buffer);
                        string senderIP = result.RemoteEndPoint.Address.ToString();

                        if (senderIP == GetMyIP())
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

        private void ProcessDiscoveryMessage(string message, string senderIP, string myName)
        {
            if (message.StartsWith("P2P:DISCOVERY:"))
            {
                string[] parts = message.Split(':');
                if (parts.Length >= 3)
                {
                    string peerName = parts[2];

                    _lastseen[senderIP] = DateTime.Now;

                    // Уведомляем о новом пользователе
                    PeerDiscovered?.Invoke(peerName, senderIP, "Online");

                    RespondToPeer(senderIP, myName);
                }
            }
            else if (message.StartsWith("P2P:RESPONSE:"))
            {
                string[] parts = message.Split(':');
                if (parts.Length >= 3)
                {
                    string peerName = parts[2];

                    _lastseen[senderIP] = DateTime.Now;

                    PeerDiscovered?.Invoke(peerName, senderIP, "Online");
                }
            }
        }
    }
}
