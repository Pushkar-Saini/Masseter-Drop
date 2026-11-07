using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MasseterDrop.Helpers;
using MasseterDrop.Models;

namespace MasseterDrop.Services
{
    public class DownloadSession
    {
        public string Id { get; } = Guid.NewGuid().ToString("N");
        public string CurrentPath { get; set; } = string.Empty;
        public string TargetDisplayName { get; set; } = string.Empty;
        public DownloadItem Item { get; set; } = null!;
        public DateTime StartTime { get; } = DateTime.Now;
    }

    public class DownloadMonitor : IDisposable
    {
        private FileSystemWatcher? _watcher;
        private string _monitoredPath;
        private readonly ConcurrentDictionary<string, DownloadSession> _sessionsByPath = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, DownloadSession> _sessionsById = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, CancellationTokenSource> _activeDebouncers = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, DateTime> _recentlyCompleted = new(StringComparer.OrdinalIgnoreCase);
        private bool _isDisposed = false;

        public event Action<DownloadItem>? FileCompleted;
        public event Action<DownloadItem>? FileDownloading;
        public event Action<string>? FileDeleted;
        public event Action<string, string>? FileCancelled; // (sessionId, path)
        public event Action<string, string>? FileRenamed;   // (oldPath, newPath)

        public string MonitoredPath => _monitoredPath;

        public DownloadMonitor(string path)
        {
            _monitoredPath = path ?? throw new ArgumentNullException(nameof(path));
            InitializeWatcher();
        }

        public void UpdatePath(string newPath)
        {
            if (string.Equals(_monitoredPath, newPath, StringComparison.OrdinalIgnoreCase))
                return;

            _monitoredPath = newPath;
            InitializeWatcher();
        }

        private void InitializeWatcher()
        {
            try
            {
                if (_watcher != null)
                {
                    _watcher.EnableRaisingEvents = false;
                    _watcher.Created -= OnFileCreated;
                    _watcher.Changed -= OnFileChanged;
                    _watcher.Renamed -= OnFileRenamed;
                    _watcher.Deleted -= OnFileDeleted;
                    _watcher.Error -= OnWatcherError;
                    _watcher.Dispose();
                    _watcher = null;
                }

                if (!Directory.Exists(_monitoredPath))
                {
                    return;
                }

                _watcher = new FileSystemWatcher(_monitoredPath)
                {
                    NotifyFilter = NotifyFilters.FileName |
                                   NotifyFilters.LastWrite |
                                   NotifyFilters.Size |
                                   NotifyFilters.CreationTime,
                    IncludeSubdirectories = false,
                    InternalBufferSize = 64 * 1024
                };

                _watcher.Created += OnFileCreated;
                _watcher.Changed += OnFileChanged;
                _watcher.Renamed += OnFileRenamed;
                _watcher.Deleted += OnFileDeleted;
                _watcher.Error += OnWatcherError;

                _watcher.EnableRaisingEvents = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to start FileSystemWatcher: {ex.Message}");
            }
        }

        private void OnFileCreated(object sender, FileSystemEventArgs e)
        {
            string fullPath = NormalizePath(e.FullPath);
            if (string.IsNullOrWhiteSpace(fullPath) || IsIgnoredAttributes(fullPath))
                return;

            if (DownloadPathHelper.IsTemporaryOrPhantom(fullPath))
            {
                // Temporary download file created by browser
                var session = GetOrCreateSession(fullPath);
                FileDownloading?.Invoke(session.Item);
            }
            else
            {
                // Direct non-temporary file creation
                DebounceAndVerifyFile(fullPath, session: null, isCompletionRename: false);
            }
        }

        private void OnFileChanged(object sender, FileSystemEventArgs e)
        {
            string fullPath = NormalizePath(e.FullPath);
            if (string.IsNullOrWhiteSpace(fullPath) || IsIgnoredAttributes(fullPath))
                return;

            if (_sessionsByPath.TryGetValue(fullPath, out var session))
            {
                // Active temporary download written to
                try
                {
                    var fi = new FileInfo(fullPath);
                    if (fi.Exists)
                    {
                        session.Item.FileSize = fi.Length;
                    }
                }
                catch { }

                FileDownloading?.Invoke(session.Item);
            }
            else if (!DownloadPathHelper.IsTemporaryOrPhantom(fullPath))
            {
                // Non-temporary file changed
                DebounceAndVerifyFile(fullPath, session: null, isCompletionRename: false);
            }
        }

        private void OnFileRenamed(object sender, RenamedEventArgs e)
        {
            string oldPath = NormalizePath(e.OldFullPath);
            string newPath = NormalizePath(e.FullPath);

            if (string.IsNullOrWhiteSpace(newPath) || IsIgnoredAttributes(newPath))
                return;

            // Scenario 1: oldPath was an actively tracked download session
            if (_sessionsByPath.TryRemove(oldPath, out var session))
            {
                CancelDebounce(oldPath);
                session.CurrentPath = newPath;
                _sessionsByPath[newPath] = session;

                if (DownloadPathHelper.IsTemporaryOrPhantom(newPath))
                {
                    // e.g. Unconfirmed 801882.crdownload -> image.png.crdownload
                    string cleanName = DownloadPathHelper.GetTargetDisplayName(newPath);
                    session.TargetDisplayName = cleanName;
                    session.Item.FileName = cleanName;
                    session.Item.FullPath = newPath;
                    session.Item.Extension = Path.GetExtension(cleanName);
                    session.Item.Category = DownloadItem.CategorizeExtension(session.Item.Extension);

                    FileDownloading?.Invoke(session.Item);
                }
                else
                {
                    // e.g. image.png.crdownload -> image.png (Download finished!)
                    session.TargetDisplayName = Path.GetFileName(newPath);
                    session.Item.FileName = session.TargetDisplayName;
                    session.Item.FullPath = newPath;
                    session.Item.Extension = Path.GetExtension(newPath);
                    session.Item.Category = DownloadItem.CategorizeExtension(session.Item.Extension);

                    DebounceAndVerifyFile(newPath, session, isCompletionRename: true);
                }
                return;
            }

            // Scenario 2: oldPath was not tracked as a session
            if (DownloadPathHelper.IsTemporaryOrPhantom(newPath))
            {
                // Renamed to a temporary file
                var newSession = GetOrCreateSession(newPath);
                FileDownloading?.Invoke(newSession.Item);
            }
            else
            {
                // Renamed to a regular file
                if (DownloadPathHelper.IsTemporaryOrPhantom(oldPath))
                {
                    // Old was temporary -> this is a completion rename
                    DebounceAndVerifyFile(newPath, session: null, isCompletionRename: true);
                }
                else
                {
                    // Regular file renamed by user (e.g. file.txt -> file_renamed.txt)
                    CancelDebounce(oldPath);
                    FileRenamed?.Invoke(oldPath, newPath);
                    DebounceAndVerifyFile(newPath, session: null, isCompletionRename: false);
                }
            }
        }

        private void OnFileDeleted(object sender, FileSystemEventArgs e)
        {
            string fullPath = NormalizePath(e.FullPath);
            CancelDebounce(fullPath);

            // If a tracked in-progress temporary download was deleted / cancelled:
            if (_sessionsByPath.TryRemove(fullPath, out var session))
            {
                _sessionsById.TryRemove(session.Id, out _);
                FileCancelled?.Invoke(session.Id, fullPath);
                return;
            }

            // If it was a temporary or phantom file that was deleted, do not report as a missing user download
            if (DownloadPathHelper.IsTemporaryOrPhantom(fullPath))
            {
                return;
            }

            // Otherwise, a legitimate completed file was deleted
            FileDeleted?.Invoke(fullPath);
        }

        private void OnWatcherError(object sender, ErrorEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"FileSystemWatcher error: {e.GetException()?.Message}");
            Task.Run(async () =>
            {
                await Task.Delay(2000);
                if (!_isDisposed)
                {
                    InitializeWatcher();
                }
            });
        }

        private DownloadSession GetOrCreateSession(string tempPath)
        {
            if (_sessionsByPath.TryGetValue(tempPath, out var existing))
            {
                return existing;
            }

            string targetName = DownloadPathHelper.GetTargetDisplayName(tempPath);
            string ext = Path.GetExtension(targetName);
            long size = 0;
            try
            {
                var fi = new FileInfo(tempPath);
                if (fi.Exists) size = fi.Length;
            }
            catch { }

            var session = new DownloadSession
            {
                CurrentPath = tempPath,
                TargetDisplayName = targetName,
                Item = new DownloadItem
                {
                    FileName = targetName,
                    FullPath = tempPath,
                    Extension = ext,
                    FileSize = size,
                    DateAdded = DateTime.Now,
                    Status = DownloadStatus.Downloading,
                    IsAvailable = true,
                    Category = DownloadItem.CategorizeExtension(ext)
                }
            };

            // Associate stable ID
            session.Item.Id = session.Id;

            _sessionsByPath[tempPath] = session;
            _sessionsById[session.Id] = session;

            return session;
        }

        private void DebounceAndVerifyFile(string fullPath, DownloadSession? session, bool isCompletionRename)
        {
            CancelDebounce(fullPath);

            // Deduplicate if completed within the last 1500ms
            if (_recentlyCompleted.TryGetValue(fullPath, out var lastTime) && (DateTime.Now - lastTime).TotalMilliseconds < 1500)
            {
                return;
            }

            var cts = new CancellationTokenSource();
            _activeDebouncers[fullPath] = cts;

            Task.Run(async () =>
            {
                try
                {
                    int initialDelay = isCompletionRename ? 250 : 600;
                    await Task.Delay(initialDelay, cts.Token);

                    bool isReady = await WaitUntilFileIsReadyAsync(fullPath, cts.Token);
                    if (isReady && !cts.Token.IsCancellationRequested)
                    {
                        // Strict validation: must physically exist, not be temporary/phantom, and reside in monitored folder
                        if (!File.Exists(fullPath) || DownloadPathHelper.IsTemporaryOrPhantom(fullPath))
                        {
                            return;
                        }

                        var fi = new FileInfo(fullPath);
                        if (!fi.Exists)
                            return;

                        DownloadItem item;
                        if (session != null)
                        {
                            item = session.Item;
                            item.FileName = fi.Name;
                            item.FullPath = fi.FullName;
                            item.Extension = fi.Extension;
                            item.FileSize = fi.Length;
                            item.DateAdded = fi.LastWriteTime;
                            item.Status = DownloadStatus.Completed;
                            item.IsAvailable = true;
                            item.Category = DownloadItem.CategorizeExtension(fi.Extension);

                            _sessionsByPath.TryRemove(fullPath, out _);
                            _sessionsById.TryRemove(session.Id, out _);
                        }
                        else
                        {
                            // Check if an existing session in memory matches this filename
                            item = DownloadItem.FromFileInfo(fi);
                        }

                        _recentlyCompleted[fullPath] = DateTime.Now;
                        FileCompleted?.Invoke(item);
                    }
                }
                catch (OperationCanceledException)
                {
                    // Debounce cancelled
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error verifying file {fullPath}: {ex.Message}");
                }
                finally
                {
                    _activeDebouncers.TryRemove(fullPath, out _);
                }
            }, cts.Token);
        }

        private async Task<bool> WaitUntilFileIsReadyAsync(string filePath, CancellationToken ct)
        {
            const int maxRetries = 30; // Max ~15 seconds polling
            const int retryIntervalMs = 500;
            long lastSize = -1;
            int stableCount = 0;

            for (int i = 0; i < maxRetries; i++)
            {
                if (ct.IsCancellationRequested) return false;

                if (!File.Exists(filePath) || DownloadPathHelper.IsTemporaryOrPhantom(filePath))
                {
                    return false;
                }

                try
                {
                    var fi = new FileInfo(filePath);
                    long currentSize = fi.Length;

                    if (currentSize == lastSize)
                    {
                        stableCount++;
                    }
                    else
                    {
                        stableCount = 0;
                        lastSize = currentSize;
                    }

                    // Must have stable size for at least 1-2 intervals and allow exclusive read or read/write sharing
                    if (stableCount >= 1 && CanOpenFileForRead(filePath))
                    {
                        return true;
                    }
                }
                catch
                {
                    // File is locked exclusively by downloading application
                }

                await Task.Delay(retryIntervalMs, ct);
            }

            return File.Exists(filePath) && !DownloadPathHelper.IsTemporaryOrPhantom(filePath);
        }

        private static bool CanOpenFileForRead(string filePath)
        {
            try
            {
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void CancelDebounce(string fullPath)
        {
            if (_activeDebouncers.TryRemove(fullPath, out var cts))
            {
                try
                {
                    cts.Cancel();
                    cts.Dispose();
                }
                catch { }
            }
        }

        private static string NormalizePath(string path)
        {
            try
            {
                return Path.GetFullPath(path);
            }
            catch
            {
                return path;
            }
        }

        private static bool IsIgnoredAttributes(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    var attr = File.GetAttributes(path);
                    return (attr & FileAttributes.Hidden) != 0 || (attr & FileAttributes.System) != 0;
                }
            }
            catch { }
            return false;
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            foreach (var cts in _activeDebouncers.Values)
            {
                try
                {
                    cts.Cancel();
                    cts.Dispose();
                }
                catch { }
            }
            _activeDebouncers.Clear();
            _sessionsByPath.Clear();
            _sessionsById.Clear();

            if (_watcher != null)
            {
                try
                {
                    _watcher.EnableRaisingEvents = false;
                    _watcher.Dispose();
                }
                catch { }
                _watcher = null;
            }
        }
    }
}
