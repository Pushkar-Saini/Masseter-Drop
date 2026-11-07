using System;
using System.IO;
using System.Text.RegularExpressions;

namespace MasseterDrop.Helpers
{
    public static class DownloadPathHelper
    {
        // Matches GUIDs with or without curly braces, e.g. {80188241-1234-5678-90ab-cdef01234567}
        private static readonly Regex GuidPattern = new(
            @"^\{?[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\}?$",
            RegexOptions.Compiled);

        private static readonly string[] TempExtensions =
        {
            ".crdownload",
            ".part",
            ".tmp",
            ".~tmp",
            ".download",
            ".opdownload",
            ".partial",
            ".crswap"
        };

        public static bool IsTemporaryOrPhantom(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return true;

            string fileName = Path.GetFileName(path);
            if (string.IsNullOrWhiteSpace(fileName))
                return true;

            string ext = Path.GetExtension(fileName).ToLowerInvariant();

            // 1. Temporary browser / download extensions
            for (int i = 0; i < TempExtensions.Length; i++)
            {
                if (string.Equals(ext, TempExtensions[i], StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            // 2. Chromium / Office / System temporary prefixes
            if (fileName.StartsWith("Unconfirmed", StringComparison.OrdinalIgnoreCase) ||
                fileName.StartsWith("~") ||
                fileName.StartsWith(".org.chromium.", StringComparison.OrdinalIgnoreCase) ||
                fileName.StartsWith(".com.google.Chrome.", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // 3. GUID filename check (both full filename and filename without extension)
            if (GuidPattern.IsMatch(fileName))
                return true;

            string nameWithoutExt = Path.GetFileNameWithoutExtension(fileName).Trim('{', '}');
            if (GuidPattern.IsMatch(nameWithoutExt))
                return true;

            // 4. Ends with .tmp or .crdownload even if composite extension
            if (fileName.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) ||
                fileName.EndsWith(".crdownload", StringComparison.OrdinalIgnoreCase) ||
                fileName.EndsWith(".part", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        public static string GetTargetDisplayName(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return "Downloading...";

            string name = Path.GetFileName(path);

            // Strip known temporary extensions
            for (int i = 0; i < TempExtensions.Length; i++)
            {
                string tempExt = TempExtensions[i];
                if (name.EndsWith(tempExt, StringComparison.OrdinalIgnoreCase))
                {
                    name = name[..^tempExt.Length];
                    break;
                }
            }

            // If the name is still a phantom (e.g. "Unconfirmed 801882" or GUID), return clean placeholder
            if (IsTemporaryOrPhantom(name))
            {
                return "Downloading...";
            }

            return name;
        }
    }
}
