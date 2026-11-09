using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MasseterDrop.Helpers;
using MasseterDrop.Models;

namespace MasseterDrop.Services
{
    public class DownloadHistoryService
    {
        private readonly string StorageDirectory;

        private readonly string HistoryFilePath;
        private readonly string RemovedItemsFilePath;
        private readonly object _lock = new object();

        public DownloadHistoryService(string? storageDirectory = null)
        {
            StorageDirectory = storageDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MasseterDrop");
            HistoryFilePath = Path.Combine(StorageDirectory, "history.json");
            RemovedItemsFilePath = Path.Combine(StorageDirectory, "removed-items.json");
        }

        public void RemoveFromHistory(string fullPath, IEnumerable<DownloadItem> remainingItems, int maxItems = 100)
        {
            lock (_lock)
            {
                var removed = LoadRemovedItems();
                removed.Add(Path.GetFullPath(fullPath));
                try
                {
                    Directory.CreateDirectory(StorageDirectory);
                    File.WriteAllText(RemovedItemsFilePath, JsonSerializer.Serialize(removed, new JsonSerializerOptions { WriteIndented = true }));
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to save removed download history: {ex.Message}");
                }
                SaveHistory(remainingItems, maxItems);
            }
        }

        public void ClearRemovedItems()
        {
            lock (_lock)
            {
                try { if (File.Exists(RemovedItemsFilePath)) File.Delete(RemovedItemsFilePath); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Failed to clear removed download history: {ex.Message}"); }
            }
        }

        public void MarkDownloaded(string fullPath)
        {
            lock (_lock)
            {
                var removed = LoadRemovedItems();
                if (removed.Remove(Path.GetFullPath(fullPath)) && removed.Count == 0)
                {
                    try { if (File.Exists(RemovedItemsFilePath)) File.Delete(RemovedItemsFilePath); }
                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Failed to update removed download history: {ex.Message}"); }
                }
                else if (removed.Count > 0)
                {
                    try { File.WriteAllText(RemovedItemsFilePath, JsonSerializer.Serialize(removed, new JsonSerializerOptions { WriteIndented = true })); }
                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Failed to update removed download history: {ex.Message}"); }
                }
            }
        }

        private HashSet<string> LoadRemovedItems()
        {
            try
            {
                if (File.Exists(RemovedItemsFilePath))
                {
                    var loaded = JsonSerializer.Deserialize<HashSet<string>>(File.ReadAllText(RemovedItemsFilePath));
                    return loaded == null
                        ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                        : new HashSet<string>(loaded, StringComparer.OrdinalIgnoreCase);
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Failed to load removed download history: {ex.Message}"); }
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        public List<DownloadItem> LoadHistory(string downloadsDirectory, int maxItems = 100)
        {
            lock (_lock)
            {
                var itemsMap = new Dictionary<string, DownloadItem>(StringComparer.OrdinalIgnoreCase);
                var removedItems = LoadRemovedItems();

                // 1. Try to load saved history, purging any phantom or temporary items
                try
                {
                    if (File.Exists(HistoryFilePath))
                    {
                        string json = File.ReadAllText(HistoryFilePath);
                        var loaded = JsonSerializer.Deserialize<List<DownloadItem>>(json);
                        if (loaded != null)
                        {
                            foreach (var item in loaded)
                            {
                                if (!string.IsNullOrWhiteSpace(item.FullPath) &&
                                    !DownloadPathHelper.IsTemporaryOrPhantom(item.FullPath) &&
                                    !DownloadPathHelper.IsTemporaryOrPhantom(item.FileName) &&
                                    item.Status == DownloadStatus.Completed &&
                                    File.Exists(item.FullPath) && !removedItems.Contains(Path.GetFullPath(item.FullPath)))
                                {
                                    item.IsAvailable = true;
                                    if (item.FileSize == 0)
                                    {
                                        try
                                        {
                                            var fi = new FileInfo(item.FullPath);
                                            item.FileSize = fi.Length;
                                        }
                                        catch { }
                                    }
                                    itemsMap[item.FullPath] = item;
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to load download history: {ex.Message}");
                }

                // 2. Discover recent valid files in downloads folder to populate on first run or missed downloads
                try
                {
                    if (Directory.Exists(downloadsDirectory))
                    {
                        var dirInfo = new DirectoryInfo(downloadsDirectory);
                        var diskFiles = dirInfo.EnumerateFiles("*", SearchOption.TopDirectoryOnly)
                            .Where(f => !IsTemporaryOrHidden(f) && !DownloadPathHelper.IsTemporaryOrPhantom(f.FullName))
                            .OrderByDescending(f => f.LastWriteTimeUtc)
                            .Take(maxItems)
                            .ToList();

                        foreach (var fi in diskFiles)
                        {
                            if (!itemsMap.ContainsKey(fi.FullName) && !removedItems.Contains(Path.GetFullPath(fi.FullName)))
                            {
                                itemsMap[fi.FullName] = DownloadItem.FromFileInfo(fi);
                            }
                            else
                            {
                                var existing = itemsMap[fi.FullName];
                                existing.IsAvailable = true;
                                existing.FileSize = fi.Length;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to scan downloads directory: {ex.Message}");
                }

                var sortedList = itemsMap.Values
                    .OrderByDescending(i => i.DateAdded)
                    .Take(maxItems)
                    .ToList();

                // Save sanitized state
                SaveHistory(sortedList, maxItems);

                return sortedList;
            }
        }

        public void SaveHistory(IEnumerable<DownloadItem> items, int maxItems = 100)
        {
            lock (_lock)
            {
                try
                {
                    if (!Directory.Exists(StorageDirectory))
                    {
                        Directory.CreateDirectory(StorageDirectory);
                    }

                    // Strict validation: never persist temporary, phantom, or non-existent files
                    var listToSave = items
                        .Where(i => !string.IsNullOrWhiteSpace(i.FullPath) &&
                                    i.Status == DownloadStatus.Completed &&
                                    i.IsAvailable &&
                                    !DownloadPathHelper.IsTemporaryOrPhantom(i.FullPath) &&
                                    !DownloadPathHelper.IsTemporaryOrPhantom(i.FileName) &&
                                    File.Exists(i.FullPath))
                        .Take(maxItems)
                        .ToList();

                    var options = new JsonSerializerOptions { WriteIndented = true };
                    string json = JsonSerializer.Serialize(listToSave, options);
                    File.WriteAllText(HistoryFilePath, json);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to save download history: {ex.Message}");
                }
            }
        }

        private static bool IsTemporaryOrHidden(FileInfo fi)
        {
            if ((fi.Attributes & FileAttributes.Hidden) != 0 ||
                (fi.Attributes & FileAttributes.System) != 0)
            {
                return true;
            }

            return DownloadPathHelper.IsTemporaryOrPhantom(fi.FullName);
        }
    }
}
