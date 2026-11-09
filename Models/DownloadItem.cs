using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Windows.Media;
using MasseterDrop.Helpers;

namespace MasseterDrop.Models
{
    public enum DownloadStatus
    {
        Completed,
        Downloading,
        Failed,
        Deleted
    }

    public enum FileCategory
    {
        Pdf,
        Image,
        Video,
        Audio,
        Archive,
        Document,
        Executable,
        Code,
        Other
    }

    public class DownloadItem : INotifyPropertyChanged
    {
        private string _id = Guid.NewGuid().ToString("N");
        private string _fileName = string.Empty;
        private string _fullPath = string.Empty;
        private string _extension = string.Empty;
        private long _fileSize = 0;
        private DateTime _dateAdded = DateTime.Now;
        private DownloadStatus _status = DownloadStatus.Completed;
        private bool _isAvailable = true;
        private FileCategory _category = FileCategory.Other;
        private double _progress = -1;
        private ImageSource? _thumbnail;
        private bool _thumbnailLoaded = false;

        public string Id
        {
            get => _id;
            set => SetField(ref _id, value);
        }

        public string FileName
        {
            get => _fileName;
            set => SetField(ref _fileName, value);
        }

        public string FullPath
        {
            get => _fullPath;
            set => SetField(ref _fullPath, value);
        }

        public string Extension
        {
            get => _extension;
            set => SetField(ref _extension, value);
        }

        public long FileSize
        {
            get => _fileSize;
            set
            {
                if (SetField(ref _fileSize, value))
                {
                    OnPropertyChanged(nameof(FormattedSize));
                }
            }
        }

        [JsonIgnore]
        public string FormattedSize => FormatBytes(_fileSize);

        public DateTime DateAdded
        {
            get => _dateAdded;
            set => SetField(ref _dateAdded, value);
        }

        public DownloadStatus Status
        {
            get => _status;
            set
            {
                if (SetField(ref _status, value))
                {
                    OnPropertyChanged(nameof(IsDownloading));
                    OnPropertyChanged(nameof(IsCompleted));
                    OnPropertyChanged(nameof(StatusDisplay));
                }
            }
        }

        public bool IsAvailable
        {
            get => _isAvailable;
            set => SetField(ref _isAvailable, value);
        }

        public FileCategory Category
        {
            get => _category;
            set => SetField(ref _category, value);
        }

        public double Progress
        {
            get => _progress;
            set => SetField(ref _progress, value);
        }

        [JsonIgnore]
        public bool IsDownloading => _status == DownloadStatus.Downloading;

        [JsonIgnore]
        public bool IsCompleted => _status == DownloadStatus.Completed && _isAvailable;

        [JsonIgnore]
        public string StatusDisplay => _status switch
        {
            DownloadStatus.Downloading => "Downloading...",
            DownloadStatus.Failed => "Download interrupted",
            DownloadStatus.Deleted => "File missing or deleted",
            _ => _isAvailable ? "Ready" : "Missing"
        };

        private bool _isJustDownloaded = false;

        [JsonIgnore]
        public bool IsJustDownloaded
        {
            get => _isJustDownloaded;
            set => SetField(ref _isJustDownloaded, value);
        }

        [JsonIgnore]
        public bool HasPreview => _thumbnail != null;

        [JsonIgnore]
        public ImageSource? Thumbnail
        {
            get
            {
                if (!_thumbnailLoaded)
                {
                    _thumbnailLoaded = true;
                    LoadThumbnail();
                }
                return _thumbnail;
            }
            set
            {
                if (SetField(ref _thumbnail, value))
                {
                    OnPropertyChanged(nameof(HasPreview));
                }
            }
        }

        public void LoadThumbnail()
        {
            try
            {
                if (string.IsNullOrEmpty(_fullPath))
                    return;

                // Load high-resolution thumbnail asynchronously without blocking UI.
                // If a real preview is not available (e.g. ZIP, EXE), Thumbnail remains null
                // and the UI cleanly falls back to the category icon.
                System.Threading.Tasks.Task.Run(async () =>
                {
                    try
                    {
                        var thumb = await Services.ThumbnailService.GetThumbnailAsync(_fullPath, 256);
                        if (thumb != null)
                        {
                            var app = System.Windows.Application.Current;
                            if (app?.Dispatcher != null && !app.Dispatcher.HasShutdownStarted)
                            {
                                app.Dispatcher.Invoke(() =>
                                {
                                    Thumbnail = thumb;
                                });
                            }
                            else
                            {
                                Thumbnail = thumb;
                            }
                        }
                    }
                    catch { }
                });
            }
            catch
            {
                Thumbnail = null;
            }
        }

        public static FileCategory CategorizeExtension(string ext)
        {
            string lower = ext.TrimStart('.').ToLowerInvariant();
            return lower switch
            {
                "pdf" => FileCategory.Pdf,
                "png" or "jpg" or "jpeg" or "gif" or "webp" or "bmp" or "svg" or "ico" or "tiff" or "tif" or "heic" or "avif" => FileCategory.Image,
                "mp4" or "mkv" or "mov" or "avi" or "webm" or "flv" or "wmv" or "m4v" or "3gp" => FileCategory.Video,
                "mp3" or "wav" or "m4a" or "flac" or "aac" or "ogg" or "wma" or "opus" => FileCategory.Audio,
                "zip" or "rar" or "7z" or "tar" or "gz" or "bz2" or "xz" or "iso" or "cab" or "tgz" => FileCategory.Archive,
                "doc" or "docx" or "xls" or "xlsx" or "ppt" or "pptx" or "txt" or "rtf" or "csv" or "odt" or "ods" or "odp" or "md" => FileCategory.Document,
                "exe" or "msi" or "bat" or "cmd" or "ps1" or "appx" or "msix" or "reg" or "vbs" => FileCategory.Executable,
                "cs" or "js" or "ts" or "html" or "htm" or "css" or "json" or "xml" or "py" or "java" or "cpp" or "c" or "h" or "hpp" or "rs" or "go" or "sql" or "sh" or "yaml" or "yml" => FileCategory.Code,
                _ => FileCategory.Other
            };
        }

        public static string FormatBytes(long bytes)
        {
            if (bytes < 0) return "0 B";
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int i = 0;
            double dbl = bytes;
            while (dbl >= 1024 && i < suffixes.Length - 1)
            {
                dbl /= 1024.0;
                i++;
            }
            return i == 0 ? $"{dbl:0} {suffixes[i]}" : $"{dbl.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)} {suffixes[i]}";
        }

        public static DownloadItem FromFileInfo(FileInfo fi)
        {
            string ext = fi.Extension;
            return new DownloadItem
            {
                Id = Guid.NewGuid().ToString("N"),
                FileName = fi.Name,
                FullPath = fi.FullName,
                Extension = ext,
                FileSize = fi.Exists ? fi.Length : 0,
                DateAdded = fi.Exists ? fi.LastWriteTime : DateTime.Now,
                Status = DownloadStatus.Completed,
                IsAvailable = fi.Exists,
                Category = CategorizeExtension(ext)
            };
        }

        public void UpdateFrom(DownloadItem other)
        {
            if (other == null) return;
            FileName = other.FileName;
            FullPath = other.FullPath;
            Extension = other.Extension;
            FileSize = other.FileSize;
            DateAdded = other.DateAdded;
            Status = other.Status;
            IsAvailable = other.IsAvailable;
            Category = other.Category;
            Progress = other.Progress;
            Thumbnail = other.Thumbnail;
        }

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        #endregion
    }
}
