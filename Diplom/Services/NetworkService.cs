using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Diplom.Services
{
    public class NetworkService
    {
        private TcpListener _listener;
        private CancellationTokenSource _cancellationTokenSource;
        private int _port = 8888;

        public event Action<string> LogMessage;
        public event Action<string, TcpClient, string> FileReceived;

        public event Action ServerStarted;
        public event Action ServerStopped;

        public event Action<string, long, string,string> FileReceiveStarted;
        public event Action<string, double> FileReceiveProgress;

        public async Task StartServerAsync()
        {
            _cancellationTokenSource = new CancellationTokenSource();

            try
            {
                _listener = new TcpListener(System.Net.IPAddress.Any, _port);
                _listener.Start();

                LogMessage?.Invoke($"Сервер запущен на порту {_port}.");
                ServerStarted?.Invoke();

                while (!_cancellationTokenSource.Token.IsCancellationRequested)
                {
                    var client = await _listener.AcceptTcpClientAsync();
                    _ = Task.Run(() => HandleClientAsync(client));
                }
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke($"Ошибка сервера: {ex.Message}");
            }
        }

        public void StopServer()
        {
            _cancellationTokenSource?.Cancel();
            _listener?.Stop();
            LogMessage?.Invoke("Сервер остановлен.");
            ServerStopped?.Invoke();
        }

        private async Task HandleClientAsync(TcpClient client)
        {
            using (client)
            using (NetworkStream stream = client.GetStream())
            using (BinaryReader reader = new BinaryReader(stream))
            {
                try
                {
                    var clientEndPoint = client.Client.RemoteEndPoint.ToString();
                    var clientIP = clientEndPoint.Split(':')[0]; // Только IP-адрес без порта

                    // Читаем тип сообщения (1 = файл)
                    byte messageType = reader.ReadByte();

                    if (messageType == 1) // Файл
                    {
                        await ReceiveFileAsync(reader, client, clientIP);
                    }
                }
                catch (Exception ex)
                {
                    LogMessage?.Invoke($"Ошибка обработки клиента: {ex.Message}");
                }
            }
        }

        private async Task ReceiveFileAsync(BinaryReader reader, TcpClient client, string clientIP)
        {
            try
            {
                // Читаем метаданные
                string fileName = reader.ReadString();
                long fileSize = reader.ReadInt64();
                string senderName = reader.ReadString();

                LogMessage?.Invoke($"Получение файла '{fileName}' ({fileSize} байт) от {senderName} ({clientIP}).");

                // Создаем папку для загрузок
                string downloadsPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "P2PDownloads");
                Directory.CreateDirectory(downloadsPath);

                string filePath = Path.Combine(downloadsPath, fileName);

                FileReceiveStarted?.Invoke(fileName, fileSize, senderName, clientIP);

                // Читаем и сохраняем файл
                using (FileStream fileStream = new FileStream(filePath, FileMode.Create))
                {
                    byte[] buffer = new byte[8192];
                    long bytesReceived = 0;

                    while (bytesReceived < fileSize)
                    {
                        int bytesToRead = (int)Math.Min(buffer.Length, fileSize - bytesReceived);
                        int bytesRead = reader.Read(buffer, 0, bytesToRead);

                        if (bytesRead == 0) break;

                        await fileStream.WriteAsync(buffer, 0, bytesRead);
                        bytesReceived += bytesRead;

                        // Отправляем прогресс
                        double progress = (double)bytesReceived / fileSize * 100;
                        FileReceiveProgress?.Invoke(fileName, progress);
                    }
                }

                LogMessage?.Invoke($"Файл '{fileName}' успешно получен и сохранен в '{filePath}'.");
                FileReceived?.Invoke(filePath, client, clientIP);
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke($"Ошибка получения файла: {ex.Message}");
                throw;
            }
            
        }

        public async Task SendFileAsync(string filePath, string receiverIP, string senderName, IProgress<double> progress = null)
        {
            try
            {
                using (TcpClient client = new TcpClient())
                {
                    await client.ConnectAsync(receiverIP, _port);

                    using (NetworkStream stream = client.GetStream())
                    using (BinaryWriter writer = new BinaryWriter(stream))
                    using (FileStream fileStream = File.OpenRead(filePath))
                    {
                        // Отправляем тип сообщения (1 = файл)
                        writer.Write((byte)1);

                        // Отправляем метаданные
                        writer.Write(Path.GetFileName(filePath));
                        writer.Write(fileStream.Length);
                        writer.Write(senderName);

                        // Отправляем содержимое файла
                        byte[] buffer = new byte[8192];
                        int bytesRead;
                        long totalBytesSent = 0;

                        while ((bytesRead = await fileStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            writer.Write(buffer, 0, bytesRead);
                            totalBytesSent += bytesRead;

                            // Отправляем прогресс
                            if (progress != null)
                            {
                                double percent = (double)totalBytesSent / fileStream.Length * 100;
                                progress.Report(percent);
                            }
                        }
                    }

                    LogMessage?.Invoke($"Файл отправлен на {receiverIP}");
                }
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke($"Ошибка отправки: {ex.Message}");
                throw;
            }
        }
    }
}
