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
        public event Action<string, TcpClient> FileReceived;

        public event Action ServerStarted;
        public event Action ServerStopped;

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
                    // Читаем тип сообщения (1 = файл)
                    byte messageType = reader.ReadByte();

                    if (messageType == 1) // Файл
                    {
                        await ReceiveFileAsync(reader, client);
                    }
                }
                catch (Exception ex)
                {
                    LogMessage?.Invoke($"Ошибка обработки клиента: {ex.Message}");
                }
            }
        }

        private async Task ReceiveFileAsync(BinaryReader reader, TcpClient client)
        {
            // Читаем метаданные
            string fileName = reader.ReadString();
            long fileSize = reader.ReadInt64();
            string senderName = reader.ReadString();

            LogMessage?.Invoke($"Получение файла '{fileName}' ({fileSize} байт) от {senderName}.");

            // Создаем папку для загрузок
            string downloadsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "P2PDownloads");
            Directory.CreateDirectory(downloadsPath);

            string filePath = Path.Combine(downloadsPath, fileName);

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

                    // Можно отправлять прогресс на UI
                }
            }

            LogMessage?.Invoke($"Файл '{fileName}' успешно получен и сохранен в '{filePath}'.");
            FileReceived?.Invoke(filePath, client);
        }

        public async Task SendFileAsync(string filePath, string receiverIP, string senderName)
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

                        while((bytesRead=await fileStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            writer.Write(buffer, 0, bytesRead);
                        }
                    }

                    LogMessage?.Invoke($"Файл '{Path.GetFileName(filePath)}' успешно отправлен на {receiverIP}.");
                }
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke($"Ошибка отправки файла: {ex.Message}");
            }
        }
    }
}
