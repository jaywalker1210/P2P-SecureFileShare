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
        private readonly CryptoService _cryptoService;

        private readonly SecureTransferService _secureTransfer;

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

        public ICommand EstablishSecureConnectionCommand { get; }

        public ICommand AddPeerManuallyCommand { get; }

        public MainViewModel()
        {
            // Инициализация коллекции в конструкторе
            Peers = new ObservableCollection<Peer>();
            Transfers = new ObservableCollection<FileTransfer>();

            _networkService = new NetworkService();
            _discoveryService = new DiscoveryService();
            _cryptoService = new CryptoService();

            _secureTransfer = new SecureTransferService(_cryptoService, _networkService);



            // Инициализируем крипто-ключи
            Task.Run(async () =>
            {
                await _cryptoService.InitializeKeysAsync();

                // Передаем публичный ключ в DiscoveryService
                _discoveryService.MyPublicKeyBase64 = _cryptoService.GetPublicKeyBase64();

                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    StatusMessage = $"ключи RSA загружены. Отпечаток: {_cryptoService.GetPublicKeyFingerprint()}";
                });
            });

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

            EstablishSecureConnectionCommand = new RelayCommand(
                async _ => await EstablishSecureConnection(),
                _ => !string.IsNullOrEmpty(SelectedPeerIP) && !IsConnectionEstablished(SelectedPeerIP));

            AddPeerManuallyCommand = new RelayCommand(_ => AddPeerManually());

            _discoveryService.StartDiscovery(MyName);

            StatusMessage = "Поиск пользователей в сети...";
        }

        private void OnPeerDiscovered(string name, string ip, string onlineStatus, string serverStatus, string publicKeyBase64)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                var existingPeer = Peers.FirstOrDefault(p => p.IPAddress == ip);

                if (onlineStatus == "Offline")
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
                        Status = serverStatus == "ServerReady" ? PeerStatus.ServerReady : PeerStatus.ClientOnly,
                        PublicKeyBase64 = publicKeyBase64
                    };

                    if (!string.IsNullOrEmpty(publicKeyBase64))
                    {
                        peer.PublicKey = _cryptoService.ImportPublicKeyFromBase64(publicKeyBase64);
                    }
                    Peers.Add(peer);
                    StatusMessage = $"Новый пользователь: {peer.DisplayName}";
                }
                else
                {
                    existingPeer.IsOnline = true;
                    existingPeer.Name = name;
                    existingPeer.LastSeen = DateTime.Now;
                    existingPeer.Status = serverStatus == "ServerReady" ? PeerStatus.ServerReady : PeerStatus.ClientOnly;

                    if (!string.IsNullOrEmpty(publicKeyBase64) && existingPeer.PublicKeyBase64 != publicKeyBase64)
                    {
                        existingPeer.PublicKeyBase64 = publicKeyBase64;
                        existingPeer.PublicKey = _cryptoService.ImportPublicKeyFromBase64(publicKeyBase64);

                        StatusMessage = $"Обновлен публичный ключ для {existingPeer.DisplayName}";
                    }
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

                _discoveryService.BroadcastStatus(MyName, "ServerReady");

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

                _discoveryService.BroadcastStatus(MyName, "ClientOnly");

                StatusMessage = "Сервер остановлен.";
            }
            catch(Exception ex)
            {
                StatusMessage = $"Ошибка остановки: {ex.Message}";
            }
        }

        private async void SendFile()
        {
            if (string.IsNullOrEmpty(SelectedPeerIP))
            {
                StatusMessage = "Выберите получателя";
                return;
            }

            // Проверяем, есть ли защищённое соединение
            if (!_secureTransfer.HasSecureConnection(SelectedPeerIP))
            {
                StatusMessage = "Сначала установите защищённое соединение (кнопка '🔒 Соединение')";
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
                    StatusMessage = $"Отправка файла: {fileName} на {SelectedPeerIP} (защищённо)";
                });

                var progress = new Progress<double>(p =>
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        transfer.Progress = p;
                        OnPropertyChanged(nameof(Transfers));
                    });
                });

                await Task.Run(async () =>
                {
                    try
                    {
                        await _secureTransfer.SendFileSecureAsync(
                            openFileDialog.FileName,
                            SelectedPeerIP,
                            progress);

                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        {
                            transfer.Status = FileTransfer.TransferStatus.Completed;
                            transfer.Progress = 100;
                            OnPropertyChanged(nameof(Transfers));
                            StatusMessage = $"Файл '{fileName}' отправлен защищённо на {SelectedPeerIP}";
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

        // Метод для ручного добавления
        private void AddPeerManually()
        {
            var dialog = new Microsoft.Win32.SaveFileDialog();
            dialog.Title = "Введите IP адрес получателя";
            dialog.FileName = ""; // Не сохраняем файл

            // Простой ввод через диалог
            string ip = Microsoft.VisualBasic.Interaction.InputBox(
                "Введите IP адрес компьютера получателя:",
                "Ручное добавление пира",
                "192.168.1.99");

            if (!string.IsNullOrEmpty(ip))
            {
                // Добавляем пира вручную
                var peer = new Peer
                {
                    Name = $"Пользователь {ip}",
                    IPAddress = ip,
                    IsOnline = true,
                    LastSeen = DateTime.Now,
                    Status = PeerStatus.ClientOnly,
                };

                Peers.Add(peer);
                StatusMessage = $"Добавлен пользователь: {peer.DisplayName}";
            }
        }


        private void OnLogMessage(string message)
        {
            // Обновляем статус в UI потоке
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                StatusMessage = message;
            });
        }

        private async Task EstablishSecureConnection()
        {
            if (string.IsNullOrEmpty(SelectedPeerIP)) return;

            var peer = Peers.FirstOrDefault(p => p.IPAddress == SelectedPeerIP);
            if (peer == null)
            {
                StatusMessage = "Нет такого получателя";
                return;
            }
            else if(peer.PublicKey == null)
            {
                StatusMessage = "Нет публичного ключа получателя";
                return;
            }

            StatusMessage = $"Установка защищённого соединения с {peer.DisplayName}...";

            bool success = await _secureTransfer.InitiateHandshakeAsync(SelectedPeerIP, peer.PublicKey.Value);

            if (success)
            {
                StatusMessage = $"Защищённое соединение с {peer.DisplayName} установлено";
            }
            else
            {
                StatusMessage = $"Не удалось установить соединение с {peer.DisplayName}";
            }
        }

        private bool IsConnectionEstablished(string ip)
        {
            return _secureTransfer.HasSecureConnection(ip);
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

        private void OnFileReceived(string filePath, System.Net.Sockets.TcpClient client, string senderIP)
        {
            var fileName = System.IO.Path.GetFileName(filePath);

            // Добавляем историю в UI потоке
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                var existingTransfer = Transfers.FirstOrDefault(t =>
                    t.FileName == fileName &&
                    t.Sender == senderIP &&
                    t.Status == FileTransfer.TransferStatus.InProgress);

                if (existingTransfer != null)
                {
                    existingTransfer.Status = FileTransfer.TransferStatus.Completed;
                    existingTransfer.Progress = 100;
                }
                else
                {
                    var transform = new FileTransfer
                    {
                        FileName = fileName,
                        FileSize = new System.IO.FileInfo(filePath).Length,
                        Sender = senderIP,
                        Receiver = MyName,
                        Timestamp = DateTime.Now,
                        Status = FileTransfer.TransferStatus.Completed,
                        Progress = 100
                    };

                    Transfers.Add(transform);
                }
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
