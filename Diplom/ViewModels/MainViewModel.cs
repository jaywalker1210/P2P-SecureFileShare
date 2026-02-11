using Diplom.Models;
using Diplom.Services;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Win32;
using System.Net;

namespace Diplom.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly NetworkService _networkService;
        private string _statusMesssage;
        private string _selectedPeerIP;
        private bool _isServerRunning;

        public ObservableCollection<Peer> Peers { get; }
        public ObservableCollection<FileTransfer> Transfers { get; }
        

        public string StatusMessage
        {
            get => _statusMesssage;
            set
            {
                _statusMesssage = value;
                OnPropertyChanged();
            }
        }

        public string SelectedPeerIP 
        { 
            get => _selectedPeerIP;
            set
            {
                _selectedPeerIP = value;
                OnPropertyChanged();
                UpdateSelectionPeerStatus();
            }
        }

        public bool IsServerRunning
        {
            get => _isServerRunning;
            set
            {
                _isServerRunning = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ServerStatusText));
                OnPropertyChanged(nameof(ServerStatusColor));
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public string ServerStatusText => IsServerRunning ? "Сервер запущен" : "Сервер остановлен";

        public string ServerStatusColor => IsServerRunning ? "Green" : "Red";

        public string MyName { get; set; } = Environment.UserName;

        public ICommand StartServerCommand { get; }
        public ICommand StopServerCommand { get; }
        public ICommand SendFileCommand { get; }
        public ICommand RefreshPeersCommand { get; }

        public MainViewModel()
        {
            // Инициализация коллекции в конструкторе
            Peers = new ObservableCollection<Peer>();
            Transfers = new ObservableCollection<FileTransfer>();

            _networkService = new NetworkService();
            _networkService.LogMessage += OnLogMessage;
            _networkService.FileReceived += OnFileReceived;
            _networkService.ServerStarted += () => IsServerRunning = true;
            _networkService.ServerStopped += () => IsServerRunning = false;

            StartServerCommand = new RelayCommand(
                async _ => await StartServer(),
                _ => !IsServerRunning);

            StopServerCommand = new RelayCommand(
                _ => StopServer(),
                _ => IsServerRunning);

            SendFileCommand = new RelayCommand(
                _ => SendFile(),
                _ => !string.IsNullOrEmpty(SelectedPeerIP) && IsServerRunning);

            RefreshPeersCommand = new RelayCommand(
                _ => RefreshPeers());

            InitializeTestPeers();
        }

        private void InitializeTestPeers()
        {
            Peers.Clear();

            if (Peers == null) return;


            // В реальном приложении здесь будет обнаружение в сети
            // Используем Dispatcher для работы с UI-потоком
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                Peers.Add(new Peer
                {
                    Name = "Компьютер Б (получатель)",
                    IPAddress = "192.168.1.61",
                    IsOnline = true,
                    LastSeen = DateTime.Now
                });
            });
        }

        private void UpdateSelectionPeerStatus()
        {
            if (!string.IsNullOrEmpty(SelectedPeerIP))
            {
                var peer = Peers.FirstOrDefault(p => p.IPAddress == SelectedPeerIP);
                if (peer != null)
                {
                    StatusMessage = $"Выбран: {peer.DisplayName}";
                }
            }
        }

        private async Task StartServer()
        {
            try
            {
                StatusMessage = "Запуск сервера...";
                await _networkService.StartServerAsync();

                // После запуска сервера меняем наш статус
                var myPeer = Peers.FirstOrDefault(p => p.Name == MyName);
                if (myPeer == null)
                {
                    // Добавляем себя в список
                    myPeer = new Peer
                    {
                        Name = MyName,
                        IPAddress = GetMyLocalIP(),
                        IsOnline = true,
                        Type = PeerType.Server
                    };
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        Peers.Add(myPeer);
                    });
                }
                else
                {
                    myPeer.Type = PeerType.Server;
                    myPeer.IsOnline = true;
                }

                StatusMessage = "Сервер запущен и готов принимать файлы";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Ошибка запуска: {ex.Message}";
            }
        }

        private void StopServer()
        {
            try
            {
                _networkService.StopServer();

                var myPeer = Peers.FirstOrDefault(p => p.Name == MyName);
                if (myPeer != null)
                {
                    myPeer.Type = PeerType.Client;
                }

                StatusMessage = "Сервер остановлен.";
            }
            catch(Exception ex)
            {
                StatusMessage = $"Ошибка остановки: {ex.Message}";
            }
        }

        private void SendFile()
        {
            if (string.IsNullOrEmpty(SelectedPeerIP))
            {
                StatusMessage = "Выберите получателя";
                return;
            }

            var openFileDialog = new OpenFileDialog
            {
                Title = "Выберите файл для отправки",
                Filter = "Все файлы (*.*)|*.*"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                var fileName = System.IO.Path.GetFileName(openFileDialog.FileName);
                var fileSize = new System.IO.FileInfo(openFileDialog.FileName).Length;

                var transfer = new FileTransfer
                {
                    FileName = fileName,
                    FileSize = fileSize,
                    Sender = MyName,
                    Receiver = SelectedPeerIP,
                    Timestamp = DateTime.Now,
                    Status = FileTransfer.TransferStatus.InProgress,
                    Progress = 0
                };

                // Добавляем в UI потоке
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    Transfers.Add(transfer);
                });

                var receiverPeer = Peers.FirstOrDefault(p => p.IPAddress == SelectedPeerIP);
                if (receiverPeer != null)
                {
                    receiverPeer.Type = PeerType.Client; // Тот, кому отправляем
                }

                // Отправляем файл в фоновом потоке
                Task.Run(async () =>
                {
                    try
                    {
                        await _networkService.SendFileAsync(
                            openFileDialog.FileName,
                            SelectedPeerIP,
                            MyName);

                        transfer.Status = FileTransfer.TransferStatus.Completed;
                        transfer.Progress = 100;

                        StatusMessage = $"Файл '{fileName}' отправителя";
                    }
                    catch (Exception ex)
                    {
                        transfer.Status = FileTransfer.TransferStatus.Failed;
                        StatusMessage = $"Ошибка отправки: {ex.Message}";
                    }
                });
            }
        }

        private void RefreshPeers()
        {
            // Здесь будет логика обнаружения пиров в сети
            // Пока просто обновим статусы
            foreach(var peer in Peers)
            {
                // В реальном приложении здесь пинг или UDP broadcast
                peer.LastSeen = DateTime.Now;
            }

            StatusMessage = "Список пользователей обновлён";
        }

        private void OnLogMessage(string message)
        {
            // Обновляем статус в UI потоке
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                StatusMessage = message;
            });
        }

        private void OnFileReceived(string filePath, System.Net.Sockets.TcpClient client)
        {
            var senderIP = client.Client.RemoteEndPoint.ToString();
            var fileName = System.IO.Path.GetFileName(filePath);

            var sender = Peers.FirstOrDefault(p => p.IPAddress == senderIP);

            // Добавляем историю в UI потоке
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                if (sender == null)
                {
                    sender = new Peer
                    {
                        Name = $"Пользователь {senderIP}",
                        IPAddress = senderIP,
                        IsOnline = true,
                        Type = PeerType.Server, // Отправил файл, значит у него работает сервер
                    };
                    Peers.Add(sender);
                }
                else
                {
                    sender.IsOnline = true;
                    sender.Type = PeerType.Server;
                    sender.LastSeen = DateTime.Now;
                }

                var transfer = new FileTransfer
                {
                    FileName = fileName,
                    Sender = senderIP,
                    Receiver = MyName,
                    Timestamp = DateTime.Now,
                    Status = FileTransfer.TransferStatus.Completed,
                    Progress = 100
                };

                Transfers.Add(transfer);
                StatusMessage = $"Файл получен: {fileName} от {senderIP}";
            });
        }

        public string MyIP => GetMyLocalIP();

        public string GetMyLocalIP()
        {
            try
            {
                var host = System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName());
                foreach(var ip in host.AddressList)
                {
                    if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    {
                        return ip.ToString();
                    }
                }
                return "127.0.0.1";
            }
            catch
            {
                return "127.0.0.1";
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class RelayCommand : ICommand
    {
        private readonly Action<object> _execute;
        private readonly Predicate<object> _canExecute;

        public RelayCommand(Action<object> execute, Predicate<object> canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

        public bool CanExecute(object parameter) => _canExecute == null || _canExecute(parameter);

        public void Execute(object parameter) => _execute?.Invoke(parameter);

        public event EventHandler CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }
    }
}
