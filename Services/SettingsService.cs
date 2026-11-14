using System;
using System.IO;
using System.Text.Json;
using MasseterDrop.Helpers;
using MasseterDrop.Models;

namespace MasseterDrop.Services
{
    public class SettingsService
    {
        private static readonly string DefaultSettingsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MasseterDrop");

        private readonly string _settingsDirectory;
        private readonly string _settingsFilePath;

        public AppSettings CurrentSettings { get; private set; } = new AppSettings();

        public SettingsService(string? customSettingsDirectory = null)
        {
            _settingsDirectory = customSettingsDirectory ?? DefaultSettingsDirectory;
            _settingsFilePath = Path.Combine(_settingsDirectory, "settings.json");
        }

        public AppSettings LoadSettings()
        {
            try
            {
                if (File.Exists(_settingsFilePath))
                {
                    string json = File.ReadAllText(_settingsFilePath);
                    var loaded = JsonSerializer.Deserialize<AppSettings>(json);
                    if (loaded != null)
                    {
                        CurrentSettings = loaded;
                        EnsureInstallationId();
                        return CurrentSettings;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load settings: {ex.Message}");
            }

            CurrentSettings = new AppSettings();
            EnsureInstallationId();
            return CurrentSettings;
        }

        private void EnsureInstallationId()
        {
            if (string.IsNullOrWhiteSpace(CurrentSettings.AnonymousInstallationId))
            {
                CurrentSettings.AnonymousInstallationId = Guid.NewGuid().ToString("D");
                SaveSettings(CurrentSettings);
            }
        }

        public void SaveSettings(AppSettings settings)
        {
            CurrentSettings = settings ?? throw new ArgumentNullException(nameof(settings));
            try
            {
                if (!Directory.Exists(_settingsDirectory))
                {
                    Directory.CreateDirectory(_settingsDirectory);
                }

                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(settings, options);
                File.WriteAllText(_settingsFilePath, json);
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

        /// <summary>
        /// Called by the user (Settings window / tray menu): turns launch-at-startup on or off,
        /// clearing a Task Manager "Disabled" flag if they explicitly turned it back on.
        /// </summary>
        public bool SetLaunchAtStartup(bool enable)
        {
            bool ok = StartupManager.SetEnabled(enable, overrideTaskManager: true);
            if (ok || !StartupManager.CanManage)
            {
                CurrentSettings.LaunchAtStartup = enable;
                SaveSettings(CurrentSettings);
            }
            return ok;
        }

        /// <summary>
        /// Called once on every launch. Makes the registry match the saved setting:
        ///  - first run (or first run after this feature shipped): turns startup on by default;
        ///  - repairs the Run entry if the exe was moved/updated;
        ///  - if the user disabled us in Task Manager, reflects that in settings instead of fighting it.
        /// </summary>
        public void SyncStartupRegistration()
        {
            if (!StartupManager.CanManage) return;

            var s = CurrentSettings;
            bool changed = false;

            if (!s.StartupDefaultApplied)
            {
                s.LaunchAtStartup = true;
                s.StartupDefaultApplied = true;
                changed = true;
            }

            if (s.LaunchAtStartup && StartupManager.IsDisabledInTaskManager())
            {
                s.LaunchAtStartup = false;
                changed = true;
                // Leave the registry alone so Task Manager keeps showing it as "Disabled".
            }
            else
            {
                StartupManager.SetEnabled(s.LaunchAtStartup, overrideTaskManager: false);
            }

            if (changed)
            {
                SaveSettings(s);
            }
        }
    }
}
