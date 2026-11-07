using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace MasseterDrop.Services
{
    public class FileOperationService
    {
        public bool OpenFile(string filePath, out string? errorMessage)
        {
            errorMessage = null;

            if (string.IsNullOrWhiteSpace(filePath))
            {
                errorMessage = "File path is empty.";
                return false;
            }

            if (!File.Exists(filePath))
            {
                errorMessage = $"The file '{Path.GetFileName(filePath)}' no longer exists.";
                return false;
            }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = filePath,
                    UseShellExecute = true
                };
                Process.Start(psi);
                return true;
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1155) // No application associated
            {
                // Prompt with Windows Open With dialog
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "rundll32.exe",
                        Arguments = $"shell32.dll,OpenAs_RunDLL {filePath}",
                        UseShellExecute = true
                    });
                    return true;
                }
                catch (Exception fallbackEx)
                {
                    errorMessage = $"No application is associated with this file type: {fallbackEx.Message}";
                    return false;
                }
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 5) // Access denied
            {
                errorMessage = "Access denied. You do not have permission to open this file.";
                return false;
            }
            catch (Exception ex)
            {
                errorMessage = $"Could not open file: {ex.Message}";
                return false;
            }
        }

        public bool ShowInExplorer(string filePath, out string? errorMessage)
        {
            errorMessage = null;

            if (string.IsNullOrWhiteSpace(filePath))
            {
                errorMessage = "File path is empty.";
                return false;
            }

            try
            {
                if (File.Exists(filePath))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"/select,\"{filePath}\"",
                        UseShellExecute = true
                    });
                    return true;
                }
                else
                {
                    // If file is missing, open parent folder if it exists
                    string? dir = Path.GetDirectoryName(filePath);
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = "explorer.exe",
                            Arguments = $"\"{dir}\"",
                            UseShellExecute = true
                        });
                        return true;
                    }

                    errorMessage = "The file and its containing folder could not be found.";
                    return false;
                }
            }
            catch (Exception ex)
            {
                errorMessage = $"Could not reveal in File Explorer: {ex.Message}";
                return false;
            }
        }

        public bool OpenFolder(string folderPath, out string? errorMessage)
        {
            errorMessage = null;

            if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
            {
                errorMessage = "The folder path does not exist.";
                return false;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{folderPath}\"",
                    UseShellExecute = true
                });
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = $"Could not open folder: {ex.Message}";
                return false;
            }
        }

        public bool CopyFilePathToClipboard(string filePath, out string? errorMessage)
        {
            errorMessage = null;
            try
            {
                Clipboard.SetText(filePath);
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = $"Failed to copy file path to clipboard: {ex.Message}";
                return false;
            }
        }

        public bool CopyFileToClipboard(string filePath, out string? errorMessage)
        {
            errorMessage = null;

            if (!File.Exists(filePath))
            {
                errorMessage = "Cannot copy file to clipboard because it no longer exists.";
                return false;
            }

            try
            {
                var fileDropList = new StringCollection { filePath };
                Clipboard.SetFileDropList(fileDropList);
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = $"Failed to copy file to clipboard: {ex.Message}";
                return false;
            }
        }
    }
}
