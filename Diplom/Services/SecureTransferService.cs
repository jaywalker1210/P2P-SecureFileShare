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

        // Хранилище сессионных ключей для получателей (IP -> AES ключ)
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
        private void OnSecureFileReceived(string senderIP, string fileName, byte[] encryptedFile, byte[] expectedHash, byte[] signature)
        {
            try
            {
                // Запускаем обработку в фоне, чтобы не блокировать сетевой поток
                Task.Run(async () =>
                {
                    // Получаем сессионный ключ для отправителя
                    if (!_sessionKeys.TryGetValue(senderIP, out byte[] aesKey))
                    {
                        throw new Exception("Нет установленного защищённого соединения с отправителем");
                    }

                    // 1. Расшифровываем файл
                    byte[] decryptedFile = DecryptWithAes(encryptedFile, aesKey);

                    // 2. Считаем хеш полученного файла
                    byte[] actualHash;
                    using (SHA256 sha256 = SHA256.Create())
                    {
                        actualHash = sha256.ComputeHash(decryptedFile);
                    }

                    // 3. Сравниваем хеши
                    if (!CompareHashes(actualHash, expectedHash))
                    {
                        throw new Exception("Хеш файла не совпадает! Файл повреждён или подменён.");
                    }

                    // 4. Сохраняем файл
                    string downloadsPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                        "P2PDownloads");
                    Directory.CreateDirectory(downloadsPath);

                    string filePath = Path.Combine(downloadsPath, fileName);
                    await File.WriteAllBytesAsync(filePath, decryptedFile);

                    Console.WriteLine($"Secure file received: {fileName}");
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Secure file receive error: {ex.Message}");
            }
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

                byte[] iv = new byte[12]; // GCM рекомендует 12 байт для IV
                using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
                {
                    rng.GetBytes(iv);
                }

                byte[] tag = new byte[16];
                byte[] ciphertext = new byte[data.Length];

                using (var encryptor = aes.CreateEncryptor())
                {
                    encryptor.TransformBlock(data, 0, data.Length, ciphertext, 0);
                }

                // Объединяем IV + шифротекст + тег
                byte[] result = new byte[iv.Length + ciphertext.Length + tag.Length];
                Buffer.BlockCopy(iv, 0, result, 0, iv.Length);
                Buffer.BlockCopy(ciphertext, 0, result, iv.Length, ciphertext.Length);
                Buffer.BlockCopy(tag, 0, result, iv.Length + ciphertext.Length, tag.Length);

                return result;
            }
        }

        private byte[] DecryptWithAes(byte[] encryptedData, byte[] key)
        {
            using (Aes aes = Aes.Create())
            {
                aes.Key = key;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                // Извлекаем IV (первые 12 байт)
                byte[] iv = new byte[12];
                Buffer.BlockCopy(encryptedData, 0, iv, 0, 12);

                // Извлекаем тег (последние 16 байт)
                byte[] tag = new byte[16];
                Buffer.BlockCopy(encryptedData, encryptedData.Length - 16, tag, 0, 16);

                // Извлекаем шифротекст
                byte[] ciphertext = new byte[encryptedData.Length - 12 - 16];
                Buffer.BlockCopy(encryptedData, 12, ciphertext, 0, ciphertext.Length);

                byte[] plaintext = new byte[ciphertext.Length];

                using (var decryptor = aes.CreateDecryptor())
                {
                    decryptor.TransformBlock(ciphertext, 0, ciphertext.Length, plaintext, 0);
                }

                return plaintext;
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
