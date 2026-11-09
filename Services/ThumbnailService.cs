using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MasseterDrop.Helpers;

namespace MasseterDrop.Services
{
    public static class ThumbnailService
    {
        private class CachedThumbnail
        {
            public ImageSource? Image { get; set; }
            public DateTime LastModified { get; set; }
        }

        private static readonly ConcurrentDictionary<string, CachedThumbnail> _cache = new(StringComparer.OrdinalIgnoreCase);

        private static readonly string[] DirectImageExtensions =
        {
            ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif", ".ico", ".tiff", ".tif", ".jfif"
        };

        public static async Task<ImageSource?> GetThumbnailAsync(string filePath, int targetSize = 256)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                return null;

            try
            {
                var fi = new FileInfo(filePath);
                DateTime lastWrite = fi.LastWriteTimeUtc;

                if (_cache.TryGetValue(filePath, out var cached) && cached.LastModified == lastWrite)
                {
                    return cached.Image;
                }

                ImageSource? result = await Task.Run(() => GenerateThumbnail(filePath, targetSize));

                if (result != null)
                {
                    _cache[filePath] = new CachedThumbnail
                    {
                        Image = result,
                        LastModified = lastWrite
                    };
                }

                return result;
            }
            catch
            {
                return null;
            }
        }

        private static ImageSource? GenerateThumbnail(string filePath, int targetSize)
        {
            try
            {
                string ext = Path.GetExtension(filePath).ToLowerInvariant();

                // 1. Direct Image Loader for standard image formats
                if (IsDirectImageExtension(ext))
                {
                    var img = LoadDirectImage(filePath, targetSize);
                    if (img != null) return img;
                }

                // 2. Windows Shell Thumbnail Factory (handles PDFs, videos, Office docs, images)
                var shellThumb = NativeMethods.GetShellThumbnail(filePath, targetSize);
                if (shellThumb != null)
                {
                    return shellThumb;
                }

                // If no actual thumbnail could be rendered, return null so the UI cleanly
                // displays the category/file icon instead of a generic or overlapping icon.
                return null;
            }
            catch
            {
                // Graceful fallback
            }

            return null;
        }

        private static bool IsDirectImageExtension(string ext)
        {
            for (int i = 0; i < DirectImageExtensions.Length; i++)
            {
                if (string.Equals(ext, DirectImageExtensions[i], StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static ImageSource? LoadDirectImage(string filePath, int targetSize)
        {
            try
            {
                byte[] bytes;
                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    using (var ms = new MemoryStream())
                    {
                        fs.CopyTo(ms);
                        bytes = ms.ToArray();
                    }
                }

                if (bytes.Length == 0) return null;

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.DecodePixelWidth = targetSize;
                bitmap.StreamSource = new MemoryStream(bytes);
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch
            {
                return null;
            }
        }

        public static void InvalidateCache(string filePath)
        {
            _cache.TryRemove(filePath, out _);
        }
    }
}
