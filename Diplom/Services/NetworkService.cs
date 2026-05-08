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

        // Новые события для Handshake и защищённой передачи
        public event Action<string, byte[], byte[]> HandshakeRequestReceived; // senderIP, encryptedAesKey, signature
        public event Action<string, byte[]> HandshakeResponseReceived; // senderIP, signature
        public event Action<string, string, byte[], byte[], byte[]> SecureFileReceived; // senderIP, fileName, encryptedFile, expectedHash, signature

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
                    var clientIP = clientEndPoint.Split(':')[0];

                    byte messageType = reader.ReadByte();

                    if (messageType == 1) // Обычный файл
                    {
                        await ReceiveFileAsync(reader, client, clientIP);
                    }
                    else if (messageType == 2) // Handshake запрос
                    {
                        int keyLen = reader.ReadInt32();
                        byte[] encryptedAesKey = reader.ReadBytes(keyLen);
                        int sigLen = reader.ReadInt32();
                        byte[] signature = reader.ReadBytes(sigLen);
                        HandshakeRequestReceived?.Invoke(clientIP, encryptedAesKey, signature);
                    }
                    else if (messageType == 3) // Handshake ответ
                    {
                        int sigLen = reader.ReadInt32();
                        byte[] signature = reader.ReadBytes(sigLen);
                        HandshakeResponseReceived?.Invoke(clientIP, signature);
                    }
                    else if (messageType == 4) // Защищённый файл
                    {
                        string fileName = reader.ReadString();
                        int fileLen = reader.ReadInt32();
                        byte[] encryptedFile = reader.ReadBytes(fileLen);
                        int hashLen = reader.ReadInt32();
                        byte[] expectedHash = reader.ReadBytes(hashLen);
                        int sigLen = reader.ReadInt32();
                        byte[] signature = reader.ReadBytes(sigLen);

                        FileReceiveStarted?.Invoke(fileName, encryptedFile.Length, "Неизвестно", clientIP);
                        SecureFileReceived?.Invoke(clientIP, fileName, encryptedFile, expectedHash, signature);

                        FileReceiveProgress?.Invoke(fileName, 100);
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

        /// <summary>
        /// Отправка Handshake запроса
        /// </summary>
        public async Task SendHandshakeRequestAsync(string receiverIP, byte[] encryptedAesKey, byte[] signature)
        {
            try
            {
                using (TcpClient client = new TcpClient())
                {
                    await client.ConnectAsync(receiverIP, _port);

                    using (NetworkStream stream = client.GetStream())
                    using (BinaryWriter writer = new BinaryWriter(stream))
                    {
                        writer.Write((byte)2); // Тип сообщения: Handshake запрос
                        writer.Write(encryptedAesKey.Length);
                        writer.Write(encryptedAesKey);
                        writer.Write(signature.Length);
                        writer.Write(signature);
                    }
                }
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke($"Handshake send error: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Отправка Handshake ответа
        /// </summary>
        public async Task SendHandshakeResponseAsync(string receiverIP, byte[] signature)
        {
            try
            {
                using (TcpClient client = new TcpClient())
                {
                    await client.ConnectAsync(receiverIP, _port);

                    using (NetworkStream stream = client.GetStream())
                    using (BinaryWriter writer = new BinaryWriter(stream))
                    {
                        writer.Write((byte)3); // Тип сообщения: Handshake ответ
                        writer.Write(signature.Length);
                        writer.Write(signature);
                    }
                }
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke($"Handshake response error: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Отправка защищённого файла
        /// </summary>
        public async Task SendSecureFileAsync(string receiverIP, string fileName, byte[] encryptedFile, byte[] expectedHash, byte[] signature, IProgress<double> progress = null)
        {
            try
            {
                using (TcpClient client = new TcpClient())
                {
                    await client.ConnectAsync(receiverIP, _port);

                    using (NetworkStream stream = client.GetStream())
                    using (BinaryWriter writer = new BinaryWriter(stream))
                    {
                        writer.Write((byte)4); // Тип сообщения: Защищённый файл
                        writer.Write(fileName);
                        writer.Write(encryptedFile.Length);
                        writer.Write(encryptedFile);
                        writer.Write(expectedHash.Length);
                        writer.Write(expectedHash);
                        writer.Write(signature.Length);
                        writer.Write(signature);
                    }
                }
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke($"Secure send error: {ex.Message}");
                throw;
            }
        }
    }
}
