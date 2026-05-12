using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Diplom.Services
{
    public class CryptoService
    {
        private readonly string _keysFolder;
        private RSAParameters _publicKey;
        private RSAParameters _privateKey;
        private bool _keysLoaded = false;

        public CryptoService()
        {
            _keysFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "P2PFileShare",
                "Keys");

            Directory.CreateDirectory(_keysFolder);
        }

        /// <summary>
        /// Генерирует или загружает пару RSA ключей при первым запуске
        /// </summary>
        public async Task InitializeKeysAsync()
        {
            string privateKeyPath = Path.Combine(_keysFolder, "private.key");
            string publicKeyPath = Path.Combine(_keysFolder, "public.key");

            if (File.Exists(privateKeyPath) && File.Exists(publicKeyPath))
            {
                await LoadKeysAsync(privateKeyPath, publicKeyPath);
            }
            else
            {
                await GenerateAndSaveKeysAsync(privateKeyPath, publicKeyPath);
            }
        }

        /// <summary>
        /// Генерация новой пары RSA-2048 ключей
        /// </summary>
        private async Task GenerateAndSaveKeysAsync(string privateKeyPath, string publicKeyPath)
        {
            await Task.Run(() =>
            {
                using (RSA rsa = RSA.Create(2048))
                {
                    byte[] privateKeyBytes = rsa.ExportRSAPrivateKey();
                    File.WriteAllBytes(privateKeyPath, privateKeyBytes);

                    byte[] publicKeyBytes = rsa.ExportRSAPublicKey();
                    File.WriteAllBytes(publicKeyPath, publicKeyBytes);

                    _publicKey = rsa.ExportParameters(false);
                    _privateKey = rsa.ExportParameters(true);
                    _keysLoaded = true;
                }
            });
        }

        /// <summary>
        /// Загрузка существующих ключей из файлов
        /// </summary>
        private async Task LoadKeysAsync(string privateKeyPath, string publicKeyPath)
        {
            await Task.Run(() =>
            {
                byte[] privateKeyBytes = File.ReadAllBytes(privateKeyPath);
                byte[] publicKeyBytes = File.ReadAllBytes(publicKeyPath);

                using (RSA rsa = RSA.Create())
                {
                    rsa.ImportRSAPrivateKey(privateKeyBytes, out _);
                    _privateKey = rsa.ExportParameters(true);

                    rsa.ImportRSAPublicKey(publicKeyBytes, out _);
                    _publicKey = rsa.ExportParameters(false);

                    _keysLoaded = true;
                }
            });
        }

        /// <summary>
        /// Получить публичный ключ в формате Base64 (для передачи по сети)
        /// </summary>
        public string GetPublicKeyBase64()
        {
            if (!_keysLoaded) return null;

            using (RSA rsa = RSA.Create())
            {
                rsa.ImportParameters(_publicKey);
                byte[] publicKeyBytes = rsa.ExportRSAPublicKey();
                return Convert.ToBase64String(publicKeyBytes);
            }
        }

        /// <summary>
        /// Импортировать публичный ключ другого пользователя из Base64
        /// </summary>
        public RSAParameters ImportPublicKeyFromBase64(string base64Key)
        {
            byte[] publicKeyBytes = Convert.FromBase64String(base64Key);
            using (RSA rsa = RSA.Create())
            {
                rsa.ImportRSAPublicKey(publicKeyBytes, out _);
                return rsa.ExportParameters(false);
            }
        }

        /// <summary>
        /// Получить отпечаток публичного ключа (для визуальной проверки)
        /// </summary>
        public string GetPublicKeyFingerprint()
        {
            string publicKeyBase64 = GetPublicKeyBase64();
            if (string.IsNullOrEmpty(publicKeyBase64)) return "Нет ключа";

            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(publicKeyBase64));
                return BitConverter.ToString(hash, 0, 8).Replace("-", "").ToUpper();
            }
        }

        /// <summary>
        /// Шифрование данных публичным ключом получателя
        /// </summary>
        public byte[] EncryptWithPublicKey(byte[] data, RSAParameters recipientPublicKey)
        {
            using (RSA rsa = RSA.Create())
            {
                rsa.ImportParameters(recipientPublicKey);
                return rsa.Encrypt(data, RSAEncryptionPadding.OaepSHA256);
            }
        }

        /// <summary>
        /// Расшифровка данных своим приватным ключом
        /// </summary>
        public byte[] DecryptWithPrivateKey(byte[] encryptedData)
        {
            using (RSA rsa = RSA.Create())
            {
                rsa.ImportParameters(_privateKey);
                return rsa.Decrypt(encryptedData, RSAEncryptionPadding.OaepSHA256);
            }
        }

        /// <summary>
        /// Подпись данных своим приватным ключом
        /// </summary>
        public byte[] SignData(byte[] data)
        {
            using (RSA rsa = RSA.Create())
            {
                rsa.ImportParameters(_privateKey);
                return rsa.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            }
        }

        /// <summary>
        /// Проверка подписи публичным ключом отправителя
        /// </summary>
        public bool VerifySignature(byte[] data, byte[] signature, RSAParameters senderPublicKey)
        {
            using (RSA rsa = RSA.Create())
            {
                rsa.ImportParameters(senderPublicKey);
                return rsa.VerifyData(data, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            }
        }
    }
}
