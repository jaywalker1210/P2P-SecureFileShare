using System;
using System.Collections.Generic;
using System.Linq;
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

        public PeerType Type { get; set; } = PeerType.Unknown;

        public string StatusDisplay
        {
            get
            {
                if (!IsOnline) return "● Оффлайн";
                return Type switch
                {
                    PeerType.Server => "● Сервер запущен (готов принимать файлы)",
                    PeerType.Client => "● Готов отправлять файлы",
                    _ => "● Онлайн"
                };
            }
        }

        public string DisplayName => $"{Name} ({IPAddress})";
    }

    public enum PeerType
    {
        Unknown,
        Server, // Запустил сервер
        Client // Не запустил сервер
    }
}
