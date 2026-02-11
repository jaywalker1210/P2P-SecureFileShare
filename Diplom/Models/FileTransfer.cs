using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Diplom.Models
{
    public class FileTransfer
    {
        public string FileName { get; set; }
        public long FileSize { get; set; }
        public string Sender { get; set; }
        public string Receiver { get; set; }
        public DateTime Timestamp { get; set; }
        public TransferStatus Status { get; set; }
        public double Progress { get; set; }

        public enum TransferStatus
        {
            Pending,
            InProgress,
            Completed,
            Failed
        }
    }
}
