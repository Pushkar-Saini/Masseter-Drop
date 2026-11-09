using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using MasseterDrop.Helpers;
using MasseterDrop.Models;
using MasseterDrop.Services;

namespace MasseterDrop.ViewModels
{
    public class DownloadsViewModel : INotifyPropertyChanged
    {
        private readonly SettingsService _settingsService;
        private readonly DownloadHistoryService _historyService;
        private readonly FileOperationService _fileOperationService;
        private DownloadMonitor? _downloadMonitor;

        private string _searchText = string.Empty;
        private DownloadItem? _selectedDownload;
        private string? _statusMessage;
        private bool _isStatusVisible;
        private readonly ObservableCollection<DownloadItem> _downloads = new();
        private readonly ICollectionView _filteredDownloadsView;
        private readonly object _collectionLock = new();

        public ObservableCollection<DownloadItem> Downloads => _downloads;
        public ICollectionView FilteredDownloads => _filteredDownloadsView;

        public event Action? RequestOpenSettings;
        public event Action<DownloadItem>? FileCompletedEvent;

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (_searchText != value)
                {
                    _searchText = value;
                    OnPropertyChanged(nameof(SearchText));
                    OnPropertyChanged(nameof(HasSearchText));
                    _filteredDownloadsView.Refresh();
                    OnPropertyChanged(nameof(FilteredCountDisplay));
                }
            }
        }

        public bool HasSearchText => !string.IsNullOrEmpty(_searchText);

        public DownloadItem? SelectedDownload
        {
            get => _selectedDownload;
            set
            {
                _selectedDownload = value;
                OnPropertyChanged(nameof(SelectedDownload));
            }
        }

        public string? StatusMessage
        {
            get => _statusMessage;
            set
            {
                _statusMessage = value;
                OnPropertyChanged(nameof(StatusMessage));
                OnPropertyChanged(nameof(IsStatusVisible));
            }
        }

        public bool IsStatusVisible
        {
            get => _isStatusVisible;
            set
            {
                _isStatusVisible = value;
                OnPropertyChanged(nameof(IsStatusVisible));
            }
        }

        public int TotalCount => _downloads.Count;

        public string TotalCountDisplay => TotalCount switch
        {
            0 => "No downloads",
            1 => "1 file",
            _ => $"{TotalCount} files"
        };

        public string FilteredCountDisplay
        {
            get
            {
                if (string.IsNullOrWhiteSpace(_searchText))
                    return TotalCountDisplay;

                int count = _downloads.Count(FilterPredicate);
                return count switch
                {
                    0 => "No matches",
                    1 => "1 match",
                    _ => $"{count} matches"
                };
            }
        }

        public string DownloadsFolderPath => _settingsService.GetEffectiveDownloadsPath();

        #region Commands

        public ICommand RefreshCommand { get; }
        public ICommand ClearSearchCommand { get; }
        public ICommand OpenFileCommand { get; }
        public ICommand ShowInExplorerCommand { get; }
        public ICommand CopyPathCommand { get; }
        public ICommand CopyFileCommand { get; }
        public ICommand RemoveItemCommand { get; }
        public ICommand OpenDownloadsFolderCommand { get; }
        public ICommand OpenSettingsCommand { get; }

        #endregion

        public DownloadsViewModel(
            SettingsService settingsService,
            DownloadHistoryService historyService,
            FileOperationService fileOperationService)
        {
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _historyService = historyService ?? throw new ArgumentNullException(nameof(historyService));
            _fileOperationService = fileOperationService ?? throw new ArgumentNullException(nameof(fileOperationService));

            _filteredDownloadsView = CollectionViewSource.GetDefaultView(_downloads);
            _filteredDownloadsView.Filter = item => item is DownloadItem di && FilterPredicate(di);
            BindingOperations.EnableCollectionSynchronization(_downloads, _collectionLock);

            RefreshCommand = new RelayCommand(ExecuteRefresh);
            ClearSearchCommand = new RelayCommand(() => SearchText = string.Empty);
            OpenFileCommand = new RelayCommand(p => ExecuteOpenFile(p as DownloadItem));
            ShowInExplorerCommand = new RelayCommand(p => ExecuteShowInExplorer(p as DownloadItem));
            CopyPathCommand = new RelayCommand(p => ExecuteCopyPath(p as DownloadItem));
            CopyFileCommand = new RelayCommand(p => ExecuteCopyFile(p as DownloadItem));
            RemoveItemCommand = new RelayCommand(p => ExecuteRemoveItem(p as DownloadItem));
            OpenDownloadsFolderCommand = new RelayCommand(ExecuteOpenDownloadsFolder);
            OpenSettingsCommand = new RelayCommand(() => RequestOpenSettings?.Invoke());

            InitializeData();
        }

        public void InitializeData()
        {
            string monitoredFolder = _settingsService.GetEffectiveDownloadsPath();
            var history = _historyService.LoadHistory(monitoredFolder, _settingsService.CurrentSettings.MaxRecentFiles);

            lock (_collectionLock)
            {
                _downloads.Clear();
                foreach (var item in history)
                {
                    _downloads.Add(item);
                }
            }

            OnPropertyChanged(nameof(TotalCount));
            OnPropertyChanged(nameof(TotalCountDisplay));
            OnPropertyChanged(nameof(FilteredCountDisplay));

            // Start monitoring
            StartMonitor(monitoredFolder);
        }

        public void StartMonitor(string folderPath)
        {
            if (_downloadMonitor != null)
            {
                _downloadMonitor.FileCompleted -= OnMonitorFileCompleted;
                _downloadMonitor.FileDownloading -= OnMonitorFileDownloading;
                _downloadMonitor.FileDeleted -= OnMonitorFileDeleted;
                _downloadMonitor.FileCancelled -= OnMonitorFileCancelled;
                _downloadMonitor.FileRenamed -= OnMonitorFileRenamed;
                _downloadMonitor.Dispose();
                _downloadMonitor = null;
            }

            if (Directory.Exists(folderPath))
            {
                _downloadMonitor = new DownloadMonitor(folderPath);
                _downloadMonitor.FileCompleted += OnMonitorFileCompleted;
                _downloadMonitor.FileDownloading += OnMonitorFileDownloading;
                _downloadMonitor.FileDeleted += OnMonitorFileDeleted;
                _downloadMonitor.FileCancelled += OnMonitorFileCancelled;
                _downloadMonitor.FileRenamed += OnMonitorFileRenamed;
            }
        }

        private void OnMonitorFileDownloading(DownloadItem item)
        {
            RunOnUIThread(() =>
            {
                lock (_collectionLock)
                {
                    // Match existing entry by stable Id, FullPath, or filename
                    var existing = _downloads.FirstOrDefault(d =>
                        string.Equals(d.Id, item.Id, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(d.FullPath, item.FullPath, StringComparison.OrdinalIgnoreCase));

                    if (existing != null)
                    {
                        existing.UpdateFrom(item);
                    }
                    else
                    {
                        _downloads.Insert(0, item);
                        UpdateCounts();
                    }
                }
            });
        }

        private void OnMonitorFileCompleted(DownloadItem item)
        {
            // Requirement 7: Validate file paths before creating completed download records
            if (string.IsNullOrWhiteSpace(item.FullPath) ||
                DownloadPathHelper.IsTemporaryOrPhantom(item.FullPath) ||
                DownloadPathHelper.IsTemporaryOrPhantom(item.FileName) ||
                !File.Exists(item.FullPath))
            {
                return;
            }

            // A genuinely new completion at this path makes a prior removal obsolete.
            _historyService.MarkDownloaded(item.FullPath);

            RunOnUIThread(() =>
            {
                lock (_collectionLock)
                {
                    // Find existing entry by:
                    // 1. Stable Id
                    // 2. FullPath
                    // 3. Or an in-progress download with the same filename
                    var existing = _downloads.FirstOrDefault(d =>
                        string.Equals(d.Id, item.Id, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(d.FullPath, item.FullPath, StringComparison.OrdinalIgnoreCase) ||
                        (d.IsDownloading && string.Equals(d.FileName, item.FileName, StringComparison.OrdinalIgnoreCase)));

                    DownloadItem completedItem;
                    if (existing != null)
                    {
                        existing.UpdateFrom(item);
                        existing.IsJustDownloaded = true;
                        int idx = _downloads.IndexOf(existing);
                        if (idx > 0)
                        {
                            _downloads.Move(idx, 0);
                        }
                        completedItem = existing;
                    }
                    else
                    {
                        item.IsJustDownloaded = true;
                        _downloads.Insert(0, item);
                        completedItem = item;
                    }

                    // Enforce max count
                    while (_downloads.Count > _settingsService.CurrentSettings.MaxRecentFiles)
                    {
                        _downloads.RemoveAt(_downloads.Count - 1);
                    }

                    UpdateCounts();
                    _historyService.SaveHistory(_downloads, _settingsService.CurrentSettings.MaxRecentFiles);
                    FileCompletedEvent?.Invoke(completedItem);
                }
            });
        }

        private void OnMonitorFileCancelled(string sessionId, string path)
        {
            RunOnUIThread(() =>
            {
                lock (_collectionLock)
                {
                    // Incomplete / temporary download was cancelled or cleaned up
                    var existing = _downloads.FirstOrDefault(d =>
                        string.Equals(d.Id, sessionId, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(d.FullPath, path, StringComparison.OrdinalIgnoreCase));

                    if (existing != null)
                    {
                        _downloads.Remove(existing);
                        UpdateCounts();
                    }
                }
            });
        }

        private void OnMonitorFileDeleted(string path)
        {
            RunOnUIThread(() =>
            {
                lock (_collectionLock)
                {
                    var existing = _downloads.FirstOrDefault(d => string.Equals(d.FullPath, path, StringComparison.OrdinalIgnoreCase));
                    if (existing != null)
                    {
                        // If it was a temporary / downloading file, completely remove it
                        if (existing.IsDownloading || DownloadPathHelper.IsTemporaryOrPhantom(existing.FullPath))
                        {
                            _downloads.Remove(existing);
                            UpdateCounts();
                        }
                        else
                        {
                            // Real completed file deleted -> mark missing
                            existing.IsAvailable = false;
                            existing.Status = DownloadStatus.Deleted;
                        }
                    }
                }
            });
        }

        private void OnMonitorFileRenamed(string oldPath, string newPath)
        {
            RunOnUIThread(() =>
            {
                lock (_collectionLock)
                {
                    var existing = _downloads.FirstOrDefault(d => string.Equals(d.FullPath, oldPath, StringComparison.OrdinalIgnoreCase));
                    if (existing != null && File.Exists(newPath))
                    {
                        var fi = new FileInfo(newPath);
                        existing.FullPath = fi.FullName;
                        existing.FileName = fi.Name;
                        existing.Extension = fi.Extension;
                        existing.Category = DownloadItem.CategorizeExtension(fi.Extension);
                        existing.DateAdded = fi.LastWriteTime;
                        existing.IsAvailable = true;
                        _historyService.SaveHistory(_downloads, _settingsService.CurrentSettings.MaxRecentFiles);
                    }
                }
            });
        }

        private void RunOnUIThread(Action action)
        {
            var app = Application.Current;
            if (app?.Dispatcher != null && !app.Dispatcher.CheckAccess() && !app.Dispatcher.HasShutdownStarted)
            {
                try
                {
                    app.Dispatcher.Invoke(action);
                    return;
                }
                catch { }
            }
            action();
        }

        private void UpdateCounts()
        {
            OnPropertyChanged(nameof(TotalCount));
            OnPropertyChanged(nameof(TotalCountDisplay));
            OnPropertyChanged(nameof(FilteredCountDisplay));
        }

        private bool FilterPredicate(DownloadItem item)
        {
            if (string.IsNullOrWhiteSpace(_searchText))
                return true;

            string query = _searchText.Trim();
            return item.FileName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                   item.Extension.Contains(query, StringComparison.OrdinalIgnoreCase);
        }

        private void ExecuteRefresh()
        {
            // Refresh is the user's explicit request to rescan disk, including older files
            // previously hidden by Remove from Recent List.
            _historyService.ClearRemovedItems();
            InitializeData();
            ShowToast("Downloads refreshed");
        }

        private void ExecuteOpenFile(DownloadItem? item)
        {
            item ??= SelectedDownload;
            if (item == null) return;

            if (!item.IsAvailable || !File.Exists(item.FullPath))
            {
                item.IsAvailable = false;
                item.Status = DownloadStatus.Deleted;
                ShowToast("File is no longer available on disk", isError: true);
                return;
            }

            if (!_fileOperationService.OpenFile(item.FullPath, out string? error))
            {
                ShowToast(error ?? "Could not open file", isError: true);
            }
        }

        private void ExecuteShowInExplorer(DownloadItem? item)
        {
            item ??= SelectedDownload;
            if (item == null) return;

            if (!_fileOperationService.ShowInExplorer(item.FullPath, out string? error))
            {
                ShowToast(error ?? "Could not locate file in File Explorer", isError: true);
            }
        }

        private void ExecuteCopyPath(DownloadItem? item)
        {
            item ??= SelectedDownload;
            if (item == null) return;

            if (_fileOperationService.CopyFilePathToClipboard(item.FullPath, out string? error))
            {
                ShowToast("File path copied to clipboard");
            }
            else
            {
                ShowToast(error ?? "Failed to copy path", isError: true);
            }
        }

        private void ExecuteCopyFile(DownloadItem? item)
        {
            item ??= SelectedDownload;
            if (item == null) return;

            if (_fileOperationService.CopyFileToClipboard(item.FullPath, out string? error))
            {
                ShowToast("File copied to clipboard");
            }
            else
            {
                ShowToast(error ?? "Failed to copy file", isError: true);
            }
        }

        private void ExecuteRemoveItem(DownloadItem? item)
        {
            item ??= SelectedDownload;
            if (item == null) return;

            _downloads.Remove(item);
            OnPropertyChanged(nameof(TotalCount));
            OnPropertyChanged(nameof(TotalCountDisplay));
            OnPropertyChanged(nameof(FilteredCountDisplay));

            _historyService.RemoveFromHistory(item.FullPath, _downloads, _settingsService.CurrentSettings.MaxRecentFiles);
            ShowToast("Removed from recent list");
        }

        private void ExecuteOpenDownloadsFolder()
        {
            string folder = _settingsService.GetEffectiveDownloadsPath();
            if (!_fileOperationService.OpenFolder(folder, out string? error))
            {
                ShowToast(error ?? "Could not open downloads folder", isError: true);
            }
        }

        public void ShowToast(string message, bool isError = false)
        {
            StatusMessage = message;
            IsStatusVisible = true;

            Task.Run(async () =>
            {
                await Task.Delay(2600);
                RunOnUIThread(() =>
                {
                    if (StatusMessage == message)
                    {
                        IsStatusVisible = false;
                    }
                });
            });
        }

        public void ReloadSettings()
        {
            string newPath = _settingsService.GetEffectiveDownloadsPath();
            if (_downloadMonitor != null && !string.Equals(_downloadMonitor.MonitoredPath, newPath, StringComparison.OrdinalIgnoreCase))
            {
                StartMonitor(newPath);
            }
            InitializeData();
        }

        public void Dispose()
        {
            if (_downloadMonitor != null)
            {
                _downloadMonitor.FileCompleted -= OnMonitorFileCompleted;
                _downloadMonitor.FileDownloading -= OnMonitorFileDownloading;
                _downloadMonitor.FileDeleted -= OnMonitorFileDeleted;
                _downloadMonitor.FileCancelled -= OnMonitorFileCancelled;
                _downloadMonitor.FileRenamed -= OnMonitorFileRenamed;
                _downloadMonitor.Dispose();
                _downloadMonitor = null;
            }
        }

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion
    }
}
