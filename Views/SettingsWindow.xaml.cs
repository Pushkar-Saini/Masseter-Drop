using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using MasseterDrop.Helpers;
using MasseterDrop.Models;
using MasseterDrop.Services;

namespace MasseterDrop.Views
{
    public partial class SettingsWindow : Window
    {
        private readonly SettingsService _settingsService;
        private readonly Action _onSettingsSaved;

        public SettingsWindow(SettingsService settingsService, Action onSettingsSaved)
        {
            InitializeComponent();
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _onSettingsSaved = onSettingsSaved ?? throw new ArgumentNullException(nameof(onSettingsSaved));

            Loaded += SettingsWindow_Loaded;
            LoadCurrentValues();
        }

        private void SettingsWindow_Loaded(object sender, RoutedEventArgs e)
        {
            ThemeService.Apply(this, _settingsService.CurrentSettings.Theme);
        }

        private void LoadCurrentValues()
        {
            var s = _settingsService.CurrentSettings;

            TxtFolderPath.Text = _settingsService.GetEffectiveDownloadsPath();
            ChkStartup.IsChecked = s.LaunchAtStartup;
            ChkStartMinimized.IsChecked = s.StartMinimized;
            ChkKeepRunning.IsChecked = s.KeepRunningOnClose;
            ChkAutoDismiss.IsChecked = s.AutoDismissOnDeactivate;
        }

        private void BrowseFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new OpenFolderDialog
                {
                    Title = "Select Monitored Downloads Folder",
                    InitialDirectory = TxtFolderPath.Text
                };

                if (dialog.ShowDialog() == true)
                {
                    TxtFolderPath.Text = dialog.FolderName;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not open folder picker: {ex.Message}", "Masseter Drop", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void ResetFolder_Click(object sender, RoutedEventArgs e)
        {
            TxtFolderPath.Text = NativeMethods.GetDownloadsFolderPath();
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            var s = _settingsService.CurrentSettings;

            string selectedFolder = TxtFolderPath.Text.Trim();
            string defaultFolder = NativeMethods.GetDownloadsFolderPath();
            if (string.Equals(selectedFolder, defaultFolder, StringComparison.OrdinalIgnoreCase))
            {
                s.CustomDownloadsFolder = null;
            }
            else if (Directory.Exists(selectedFolder))
            {
                s.CustomDownloadsFolder = selectedFolder;
            }

            s.LaunchAtStartup = ChkStartup.IsChecked == true;
            s.StartMinimized = ChkStartMinimized.IsChecked == true;
            s.KeepRunningOnClose = ChkKeepRunning.IsChecked == true;
            s.AutoDismissOnDeactivate = ChkAutoDismiss.IsChecked == true;

            // Sensible internal defaults
            s.MaxRecentFiles = 100;
            s.Theme = "Dark";

            _settingsService.SaveSettings(s);
            _onSettingsSaved();

            Close();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{TxtFolderPath.Text}\"",
                    UseShellExecute = true
                };
                System.Diagnostics.Process.Start(psi);
            }
            catch { }
        }
    }
}
