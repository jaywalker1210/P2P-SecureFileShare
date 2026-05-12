using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Diplom.Models
{
    public class FileTransfer: INotifyPropertyChanged
    {
        private string _fileName;
        private long _fileSize;
        private string _sender;
        private string _receiver;
        private DateTime _timestamp;
        private TransferStatus _status;
        private double _progress;

        public string FileName
        {
            get => _fileName;
            set
            {
                _fileName = value;
                OnPropertyChanged();
            }
        }

        public long FileSize
        {
            get => _fileSize;
            set
            {
                _fileSize = value;
                OnPropertyChanged();
            }
        }

        public string Sender
        {
            get => _sender;
            set
            {
                _sender = value;
                OnPropertyChanged();
            }
        }

        public string Receiver
        {
            get => _receiver;
            set
            {
                _receiver = value;
                OnPropertyChanged();
            }
        }

        public DateTime Timestamp
        {
            get => _timestamp;
            set
            {
                _timestamp = value;
                OnPropertyChanged();
            }
        }

        public TransferStatus Status
        {
            get => _status;
            set
            {
                _status = value;
                OnPropertyChanged();
            }
        }

        public double Progress
        {
            get => _progress;
            set
            {
                _progress = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public enum TransferStatus
        {
            Pending,
            InProgress,
            Completed,
            Failed
        }
    }
}
