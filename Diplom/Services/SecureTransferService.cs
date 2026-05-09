using Diplom.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Diplom.Services
{
    public class SecureTransferService
    {
        private readonly CryptoService _crypto;
        private readonly NetworkService _network;
        public event Action<string, string, long> OnSecureFileReceiveStarted;
        public event Action<string, string, string> OnSecureFileCompleted;
        public event Action<string, string, string> OnSecureFileFailed;
        public event Action<string, double> OnSecureFileReceiveProgress;

        private Dictionary<string, byte[]> _sessionKeys = new Dictionary<string, byte[]>();

        public SecureTransferService(CryptoService crypto, NetworkService network)
        {
            _crypto = crypto;
            _network = network;

            _network.HandshakeRequestReceived += OnHandshakeRequestReceived;
            _network.HandshakeResponseReceived += OnHandshakeResponseReceived;
            _network.SecureFileReceived += OnSecureFileReceived;
        }

        /// <summary>
        /// Инициировать Handshake с получателем
        /// </summary>
        public async Task<bool> InitiateHandshakeAsync(string receiverIP, RSAParameters receiverPublicKey)
        {
            try
            {
                // 1. Генерируем случайный AES-256 ключ
                byte[] aesKey = GenerateAesKey();

                // 2. Шифруем AES ключ публичным ключом получателя
                byte[] encryptedAesKey = _crypto.EncryptWithPublicKey(aesKey, receiverPublicKey);

                // 3. Создаем подпись для ключа (чтобы получатель знал, что это от нас)
                byte[] signature = _crypto.SignData(encryptedAesKey);

                // 4. Отправляем Handshake запрос
                await _network.SendHandshakeRequestAsync(receiverIP, encryptedAesKey, signature);

                // 5. Сохраняем ключ для этого получателя
                _sessionKeys[receiverIP] = aesKey;

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Handshake error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Обработка Handshake запроса (на стороне получателя)
        /// </summary>
        private void OnHandshakeRequestReceived(string senderIP, byte[] encryptedAesKey, byte[] signature)
        {
            try
            {
                // Расшифровываем AES ключ своим приватным ключом
                byte[] aesKey = _crypto.DecryptWithPrivateKey(encryptedAesKey);

                // Сохраняем ключ для этого отправителя
                _sessionKeys[senderIP] = aesKey;

                // Отправляем подтверждение (запускаем в фоне, не ждём)
                Task.Run(async () =>
                {
                    byte[] confirmationSignature = _crypto.SignData(encryptedAesKey);
                    await _network.SendHandshakeResponseAsync(senderIP, confirmationSignature);
                });

                Console.WriteLine($"Handshake completed with {senderIP}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Handshake receive error: {ex.Message}");
            }
        }

        /// <summary>
        /// Обработка Handshake ответа
        /// </summary>
        private void OnHandshakeResponseReceived(string senderIP, byte[] signature)
        {
            // Подтверждение получено, соединение установлено
            Console.WriteLine($"Handshake confirmed with {senderIP}");
        }

        /// <summary>
        /// Отправка файла с шифрованием
        /// </summary>
        public async Task SendFileSecureAsync(string filePath, string receiverIP, IProgress<double> progress = null)
        {
            try
            {
                // Получаем сессионный ключ для получателя
                if (!_sessionKeys.TryGetValue(receiverIP, out byte[] aesKey))
                {
                    throw new Exception("Нет установленного защищённого соединения. Сначала выполните Handshake.");
                }

                // 1. Читаем файл
                byte[] fileData = await File.ReadAllBytesAsync(filePath);

                // 2. Считаем SHA-256 хеш
                byte[] fileHash;
                using (SHA256 sha256 = SHA256.Create())
                {
                    fileHash = sha256.ComputeHash(fileData);
                }

                // 3. Подписываем хеш своим приватным ключом
                byte[] signature = _crypto.SignData(fileHash);

                // 4. Шифруем файл AES ключом
                byte[] encryptedFile = EncryptWithAes(fileData, aesKey);

                // 5. Отправляем
                await _network.SendSecureFileAsync(receiverIP, Path.GetFileName(filePath), encryptedFile, fileHash, signature, progress);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Send secure file error: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Обработка полученного защищённого файла
        /// </summary>
        /// <summary>
        /// Обработка полученного защищённого файла
        /// </summary>
        private void OnSecureFileReceived(string senderIP, string fileName, byte[] encryptedFile, byte[] expectedHash, byte[] signature)
        {
            // 1. СРАЗУ уведомляем UI о начале получения
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                OnSecureFileReceiveStarted?.Invoke(fileName, senderIP, encryptedFile.Length);
            });

            // 2. Запускаем расшифровку в фоне
            Task.Run(async () =>
            {
                try
                {
                    if (!_sessionKeys.TryGetValue(senderIP, out byte[] aesKey))
                    {
                        throw new Exception("Нет установленного защищённого соединения с отправителем");
                    }

                    // Расшифровываем файл с уведомлениями о прогрессе
                    byte[] decryptedFile = await DecryptWithAesProgressAsync(encryptedFile, aesKey, (progress) =>
                    {
                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        {
                            OnSecureFileReceiveProgress?.Invoke(fileName, progress);
                        });
                    });

                    // Считаем хеш
                    byte[] actualHash;
                    using (SHA256 sha256 = SHA256.Create())
                    {
                        actualHash = sha256.ComputeHash(decryptedFile);
                    }

                    // Сравниваем хеши
                    if (!CompareHashes(actualHash, expectedHash))
                    {
                        throw new Exception("Хеш файла не совпадает! Файл повреждён или подменён.");
                    }

                    // Сохраняем файл
                    string downloadsPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                        "P2PDownloads");
                    Directory.CreateDirectory(downloadsPath);

                    string filePath = Path.Combine(downloadsPath, fileName);
                    await File.WriteAllBytesAsync(filePath, decryptedFile);

                    // Уведомляем о завершении
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        OnSecureFileCompleted?.Invoke(filePath, senderIP, fileName);
                        OnSecureFileReceiveProgress?.Invoke(fileName, 100);
                    });
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Secure file receive error: {ex.Message}");
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        OnSecureFileFailed?.Invoke(fileName, senderIP, ex.Message);
                    });
                }
            });
        }

        private async Task<byte[]> DecryptWithAesProgressAsync(byte[] encryptedData, byte[] key, Action<double> onProgress)
        {
            return await Task.Run(() =>
            {
                using (Aes aes = Aes.Create())
                {
                    aes.Key = key;
                    aes.Mode = CipherMode.CBC;
                    aes.Padding = PaddingMode.PKCS7;

                    byte[] iv = new byte[16];
                    Buffer.BlockCopy(encryptedData, 0, iv, 0, 16);
                    aes.IV = iv;

                    byte[] ciphertext = new byte[encryptedData.Length - 16];
                    Buffer.BlockCopy(encryptedData, 16, ciphertext, 0, ciphertext.Length);

                    using (var ms = new MemoryStream(ciphertext))
                    using (var cs = new CryptoStream(ms, aes.CreateDecryptor(), CryptoStreamMode.Read))
                    using (var resultMs = new MemoryStream())
                    {
                        byte[] buffer = new byte[8192];
                        int bytesRead;
                        long totalRead = 0;

                        while ((bytesRead = cs.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            resultMs.Write(buffer, 0, bytesRead);
                            totalRead += bytesRead;

                            double progress = (double)totalRead / ciphertext.Length * 100;
                            onProgress?.Invoke(progress);
                        }

                        return resultMs.ToArray();
                    }
                }
            });
        }

        

        private byte[] GenerateAesKey()
        {
            byte[] key = new byte[32]; // AES-256 = 32 байта
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(key);
            }
            return key;
        }

        private byte[] EncryptWithAes(byte[] data, byte[] key)
        {
            using (Aes aes = Aes.Create())
            {
                aes.Key = key;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                byte[] iv = new byte[16];
                using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
                {
                    rng.GetBytes(iv);
                }
                aes.IV = iv;

                using (var encryptor = aes.CreateEncryptor())
                using (var ms = new MemoryStream())
                {
                    ms.Write(iv, 0, iv.Length);
        
                    using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
                    {
                        cs.Write(data, 0, data.Length);
                        cs.FlushFinalBlock();
                    }

                    return ms.ToArray();
                }
            }
        }

        private bool CompareHashes(byte[] hash1, byte[] hash2)
        {
            if (hash1.Length != hash2.Length) return false;
            for (int i = 0; i < hash1.Length; i++)
            {
                if (hash1[i] != hash2[i]) return false;
            }
            return true;
        }

        public bool HasSecureConnection(string ip)
        {
            return _sessionKeys.ContainsKey(ip);
        }
    }
}
