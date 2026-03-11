using Diplom.Models;
using Diplom.Services;
using System;
using System.Collections.Generic;
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
        private readonly DiscoveryService _discoveryService;
        private string _statusMesssage;
        private string _selectedPeerIP;
        private bool _isServerRunning;

        public ObservableCollection<Peer> Peers { get; }
        public ObservableCollection<FileTransfer> Transfers { get; }

        private Dictionary<string, FileTransfer> _activeTransfers = new Dictionary<string, FileTransfer>();

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

                UpdateMyStatus();
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
            _discoveryService = new DiscoveryService();

            _networkService.LogMessage += OnLogMessage;
            _networkService.FileReceived += OnFileReceived;
            _networkService.FileReceiveStarted += OnFileReceiveStarted;
            _networkService.FileReceiveProgress += OnFileReceiveProgress;
            _networkService.ServerStarted += () => IsServerRunning = true;
            _networkService.ServerStopped += () => IsServerRunning = false;

            _discoveryService.PeerDiscovered += OnPeerDiscovered;

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

            _discoveryService.StartDiscovery(MyName);

            StatusMessage = "Поиск пользователей в сети...";
        }

        private void OnPeerDiscovered(string name, string ip, string status)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                var existingPeer = Peers.FirstOrDefault(p => p.IPAddress == ip);

                if (status == "Offline")
                {
                    if (existingPeer != null)
                    {
                        existingPeer.IsOnline = false;
                    }
                    return;
                }

                if (existingPeer == null)
                {
                    var peer = new Peer
                    {
                        Name = string.IsNullOrEmpty(name) ? $"Пользователь {ip}" : name,
                        IPAddress = ip,
                        IsOnline = true,
                        LastSeen = DateTime.Now,
                        Status = PeerStatus.ClientOnly
                    };
                    Peers.Add(peer);
                    StatusMessage = $"Новый пользователь: {peer.DisplayName}";
                }
                else
                {
                    existingPeer.IsOnline = true;
                    existingPeer.Name = name;
                    existingPeer.LastSeen = DateTime.Now;
                }
            });
        }

        private void UpdateMyStatus()
        {
            // Здесь можно отправить дополнительную информацию о своем статусе
            // Но пока просто перезапустим обнаружение
            _discoveryService.StartDiscovery(MyName);
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
                var myPeer = Peers.FirstOrDefault(p => p.IPAddress == MyIP);
                if (myPeer != null)
                {
                    myPeer.Status = PeerStatus.ServerReady;
                }

                StatusMessage = $"Сервер запущен. Ваш IP: {MyIP}";
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

                var myPeer = Peers.FirstOrDefault(p => p.IPAddress == MyIP);
                if (myPeer != null)
                {
                    myPeer.Status = PeerStatus.ClientOnly;
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

                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    Transfers.Add(transfer);
                    StatusMessage = $"Отправка файла: {fileName} на {SelectedPeerIP}";
                });

                var progress = new Progress<double>(p =>
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        transfer.Progress = p;
                        OnPropertyChanged(nameof(Transfers));
                    });
                });

                // Отправляем файл в фоновом потоке
                Task.Run(async () =>
                {
                    try
                    {
                        await _networkService.SendFileAsync(
                            openFileDialog.FileName,
                            SelectedPeerIP,
                            MyName,
                            progress);

                        // Обновляем статус в UI потоке
                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        {
                            transfer.Status = FileTransfer.TransferStatus.Completed;
                            transfer.Progress = 100;
                            OnPropertyChanged(nameof(Transfers));

                            StatusMessage = $"Файл '{fileName}' отправлен на {SelectedPeerIP}";
                        });
                    }
                    catch (Exception ex)
                    {
                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        {
                            transfer.Status = FileTransfer.TransferStatus.Failed;
                            OnPropertyChanged(nameof(Transfers));
                            StatusMessage = $"Ошибка отправки: {ex.Message}";
                        });
                    }
                });
            }
        }

        private void RefreshPeers()
        {
            _discoveryService.StartDiscovery(MyName);
             StatusMessage = "Поиск пользователей...";
        }

        private void OnLogMessage(string message)
        {
            // Обновляем статус в UI потоке
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                StatusMessage = message;
            });
        }

        private void OnFileReceiveStarted(string fileName, long fileSize, string senderName, string senderIP)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                var transfer = new FileTransfer
                {
                    FileName = fileName,
                    FileSize = fileSize,
                    Sender = senderIP,
                    Receiver = MyName,
                    Timestamp = DateTime.Now,
                    Status = FileTransfer.TransferStatus.InProgress,
                    Progress = 0
                };

                Transfers.Add(transfer);
                StatusMessage = $"Получение файла: {fileName} от {senderIP}";

                string key = $"{fileName}_{senderIP}";
                if (!_activeTransfers.ContainsKey(key))
                {
                    _activeTransfers.Add(key, transfer);
                }
            });
        }

        // остановился здесь
        private void OnFileReceived(string filePath, System.Net.Sockets.TcpClient client, string senderIP)
        {
            var fileName = System.IO.Path.GetFileName(filePath);

            // Добавляем историю в UI потоке
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                var transform = new FileTransfer
                {
                    FileName = fileName,
                    Sender = senderIP,
                    Receiver = MyName,
                    Timestamp = DateTime.Now,
                    Status = FileTransfer.TransferStatus.Completed,
                    Progress = 100
                };

                Transfers.Add(transform);
                OnPropertyChanged(nameof(Transfers));
                StatusMessage = $"Получен файл: {fileName} от {senderIP}";
            });
        }

        private void OnFileReceiveProgress(string fileName, double progress)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                var transfer = Transfers.FirstOrDefault(t =>
                    t.FileName == fileName &&
                    t.Status == FileTransfer.TransferStatus.InProgress);

                if (transfer != null)
                {
                    transfer.Progress = progress;
                    OnPropertyChanged(nameof(Transfers));
                }
            });
        }

        public string MyIP => GetMyLocalIP();

        public string GetMyLocalIP()
        {
            try
            {
                var host = System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName());
                foreach (var ip in host.AddressList)
                {
                    if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    {
                        return ip.ToString();
                    }
                }
            }
            catch { }
            return "127.0.0.1";
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
