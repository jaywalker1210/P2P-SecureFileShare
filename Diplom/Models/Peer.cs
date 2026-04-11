using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Diplom.Models
{
    public class Peer
    {
        public string Name { get; set; } = string.Empty;
        public string IPAddress { get; set; } = string.Empty;
        public bool IsOnline { get; set; }
        public DateTime LastSeen { get; set; } = DateTime.Now;

        public PeerStatus Status { get; set; } = PeerStatus.Unknown;

        public RSAParameters? PublicKey { get; set; }
        public string PublicKeyBase64 { get; set; } = string.Empty;

        public string DisplayStatus
        {
            get
            {
                if (!IsOnline) return "● Оффлайн";
                return Status switch
                {
                    PeerStatus.ServerReady => "● Сервер готов",
                    PeerStatus.ClientOnly => "● Только клиент",
                    _ => "● Онлайн"
                };
            }
        }

        public string StatusColor
        {
            get
            {
                if (!IsOnline) return "Gray";
                return Status switch
                {
                    PeerStatus.ServerReady => "Green",
                    PeerStatus.ClientOnly => "Orange",
                    _ => "Blue"
                };
            }
        }

        public string DisplayName => $"{Name} ({IPAddress})";

        public string KeyFingerprint
        {
            get
            {
                if (string.IsNullOrEmpty(PublicKeyBase64)) return "Нет ключа";
                using (var sha256 = System.Security.Cryptography.SHA256.Create())
                {
                    byte[] hash = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(PublicKeyBase64));
                    return BitConverter.ToString(hash, 0, 6).Replace("-", "").ToUpper();
                }
            }
        }
    }

    public enum PeerStatus
    {
        Unknown,
        ServerReady, // Запустил сервер
        ClientOnly // Тоько клиент, не готов принимать
    }
}
