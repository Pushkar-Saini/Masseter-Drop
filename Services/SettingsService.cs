using System;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;
using MasseterDrop.Helpers;
using MasseterDrop.Models;

namespace MasseterDrop.Services
{
    public class SettingsService
    {
        private static readonly string SettingsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MasseterDrop");

        private static readonly string SettingsFilePath = Path.Combine(SettingsDirectory, "settings.json");
        private const string StartupRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AppRegistryName = "MasseterDrop";

        public AppSettings CurrentSettings { get; private set; } = new AppSettings();

        public AppSettings LoadSettings()
        {
            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    string json = File.ReadAllText(SettingsFilePath);
                    var loaded = JsonSerializer.Deserialize<AppSettings>(json);
                    if (loaded != null)
                    {
                        CurrentSettings = loaded;
                        return CurrentSettings;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load settings: {ex.Message}");
            }

            CurrentSettings = new AppSettings();
            return CurrentSettings;
        }

        public void SaveSettings(AppSettings settings)
        {
            CurrentSettings = settings ?? throw new ArgumentNullException(nameof(settings));
            try
            {
                if (!Directory.Exists(SettingsDirectory))
                {
                    Directory.CreateDirectory(SettingsDirectory);
                }

                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(settings, options);
                File.WriteAllText(SettingsFilePath, json);

                ApplyStartupSetting(settings.LaunchAtStartup);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save settings: {ex.Message}");
            }
        }

        public string GetEffectiveDownloadsPath()
        {
            if (!string.IsNullOrWhiteSpace(CurrentSettings.CustomDownloadsFolder) &&
                Directory.Exists(CurrentSettings.CustomDownloadsFolder))
            {
                return CurrentSettings.CustomDownloadsFolder;
            }

            return NativeMethods.GetDownloadsFolderPath();
        }

        private void ApplyStartupSetting(bool enable)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(StartupRegistryKey, true);
                if (key == null) return;

                if (enable)
                {
                    string? exePath = Environment.ProcessPath;
                    if (!string.IsNullOrEmpty(exePath))
                    {
                        key.SetValue(AppRegistryName, $"\"{exePath}\" --minimized");
                    }
                }
                else
                {
                    if (key.GetValue(AppRegistryName) != null)
                    {
                        key.DeleteValue(AppRegistryName, false);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to configure startup registry key: {ex.Message}");
            }
        }
    }
}
