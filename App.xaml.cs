using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using Forms = System.Windows.Forms;
using MasseterDrop.Helpers;
using MasseterDrop.Services;
using MasseterDrop.ViewModels;
using MasseterDrop.Views;

namespace MasseterDrop
{
    public partial class App : System.Windows.Application
    {
        private const string AppUserModelId = "Masseter.Drop.App.v1";
        private const string MutexName = "Global\\MasseterDrop_SingleInstance_Mutex_v1";

        private static Mutex? _instanceMutex;
        private SettingsService? _settingsService;
        private IAnalyticsService? _analyticsService;
        private DownloadHistoryService? _historyService;
        private FileOperationService? _fileOperationService;
        private TaskbarService? _taskbarService;
        private DownloadsViewModel? _downloadsViewModel;
        private DownloadShelfWindow? _shelfWindow;
        private Forms.NotifyIcon? _notifyIcon;

        public App()
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
        }

        [STAThread]
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            // 1. Single Instance Check
            bool isNewInstance;
            try
            {
                _instanceMutex = new Mutex(true, MutexName, out isNewInstance);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Mutex initialization error: {ex.Message}");
                isNewInstance = true;
            }

            if (!isNewInstance)
            {
                // Signal running instance to activate panel
                TaskbarService.SignalExistingInstance();
                Shutdown();
                return;
            }

            // 2. Set AppUserModelID for Windows Taskbar Pinning
            try
            {
                NativeMethods.SetCurrentProcessExplicitAppUserModelID(AppUserModelId);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to set AppUserModelID: {ex.Message}");
            }

            // 3. Global Exception Handling
            DispatcherUnhandledException += (s, args) =>
            {
                System.Diagnostics.Debug.WriteLine($"Unhandled dispatcher exception: {args.Exception.Message}");
                args.Handled = true;
            };

            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                System.Diagnostics.Debug.WriteLine($"Unhandled AppDomain exception: {args.ExceptionObject}");
            };

            // 4. Initialize Core Services
            _settingsService = new SettingsService();
            _settingsService.LoadSettings();

            // Register (or repair) the "launch at Windows sign-in" entry so the app
            // starts silently in the tray every time the PC is turned on.
            _settingsService.SyncStartupRegistration();

            _analyticsService = new AnalyticsService(_settingsService);
            DragDropHelper.FileDragged += category => _analyticsService.TrackFileDragged(category);

            _historyService = new DownloadHistoryService();
            _fileOperationService = new FileOperationService();

            _taskbarService = new TaskbarService();
            _taskbarService.ActivationRequested += OnActivationRequested;
            _taskbarService.StartSingleInstanceServer();

            // 5. Initialize ViewModels and Main Shelf Window
            _downloadsViewModel = new DownloadsViewModel(_settingsService, _historyService, _fileOperationService, _analyticsService);
            _shelfWindow = new DownloadShelfWindow(_settingsService, _downloadsViewModel, _analyticsService);
            MainWindow = _shelfWindow;

            // 6. Setup System Tray Icon
            SetupNotifyIcon();

            // 7. Check whether to start minimized in tray
            bool startMinimized = e.Args.Any(a =>
                string.Equals(a, StartupManager.StartupArgument, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(a, "-minimized", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(a, "/minimized", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(a, "--tray", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(a, "/silent", StringComparison.OrdinalIgnoreCase)) ||
                _settingsService.CurrentSettings.StartMinimized;

            // 8. Analytics Consent Check & Initial Lifecycle Events
            if (_settingsService.CurrentSettings.AnalyticsConsent == null)
            {
                if (!startMinimized)
                {
                    PromptAnalyticsConsent();
                }
            }
            else if (_settingsService.CurrentSettings.AnalyticsConsent == true)
            {
                RecordStartupAnalytics(startMinimized);
            }

            if (!startMinimized)
            {
                _shelfWindow.ShowManual();
            }
        }

        private void SetupNotifyIcon()
        {
            try
            {
                _notifyIcon = new Forms.NotifyIcon
                {
                    Text = "Masseter Drop — Your downloads. One click away.",
                    Visible = true
                };

                // Load icon from disk or executable resource
                Icon? loadedIcon = null;
                string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Icons", "app.ico");
                if (File.Exists(iconPath))
                {
                    try { loadedIcon = new Icon(iconPath); } catch { }
                }

                if (loadedIcon == null)
                {
                    try
                    {
                        string? exe = Environment.ProcessPath;
                        if (!string.IsNullOrEmpty(exe) && File.Exists(exe))
                        {
                            loadedIcon = Icon.ExtractAssociatedIcon(exe);
                        }
                    }
                    catch { }
                }

                _notifyIcon.Icon = loadedIcon ?? SystemIcons.Application;

                // Context Menu
                var contextMenu = new Forms.ContextMenuStrip();

                var itemPanel = new Forms.ToolStripMenuItem("Download Shelf")
                {
                    Font = new Font(contextMenu.Font, System.Drawing.FontStyle.Bold)
                };
                itemPanel.Click += (s, e) => _shelfWindow?.ToggleShelf();

                var itemFolder = new Forms.ToolStripMenuItem("Open Downloads Folder");
                itemFolder.Click += (s, e) =>
                {
                    string folder = _settingsService?.GetEffectiveDownloadsPath() ?? string.Empty;
                    _fileOperationService?.OpenFolder(folder, out _);
                };

                var itemSettings = new Forms.ToolStripMenuItem("Settings...");
                itemSettings.Click += (s, e) =>
                {
                    _shelfWindow?.ShowManual();
                    _downloadsViewModel?.OpenSettingsCommand.Execute(null);
                };

                var itemStartup = new Forms.ToolStripMenuItem("Start with Windows")
                {
                    CheckOnClick = false,
                    Enabled = StartupManager.CanManage,
                    Checked = StartupManager.CanManage ? StartupManager.IsEnabled() : (_settingsService?.CurrentSettings.LaunchAtStartup ?? false)
                };
                itemStartup.Click += (s, e) =>
                {
                    bool enable = !StartupManager.IsEnabled();
                    _settingsService?.SetLaunchAtStartup(enable);
                    itemStartup.Checked = StartupManager.IsEnabled();
                };
                // Refresh the tick each time the menu opens (it may have changed in Settings or Task Manager).
                contextMenu.Opening += (s, e) => itemStartup.Checked = StartupManager.CanManage ? StartupManager.IsEnabled() : (_settingsService?.CurrentSettings.LaunchAtStartup ?? false);

                var itemExit = new Forms.ToolStripMenuItem("Exit");
                itemExit.Click += (s, e) => ExitApplication();

                contextMenu.Items.Add(itemPanel);
                contextMenu.Items.Add(itemFolder);
                contextMenu.Items.Add(itemSettings);
                contextMenu.Items.Add(itemStartup);
                contextMenu.Items.Add(new Forms.ToolStripSeparator());
                contextMenu.Items.Add(itemExit);

                _notifyIcon.ContextMenuStrip = contextMenu;

                // Left click toggles shelf
                _notifyIcon.MouseClick += (s, e) =>
                {
                    if (e.Button == Forms.MouseButtons.Left)
                    {
                        if (_settingsService?.CurrentSettings.AnalyticsConsent == null)
                        {
                            PromptAnalyticsConsent();
                        }
                        _shelfWindow?.ToggleShelf();
                    }
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to initialize notify icon: {ex.Message}");
            }
        }

        private void OnActivationRequested()
        {
            Dispatcher.Invoke(() =>
            {
                if (_settingsService?.CurrentSettings.AnalyticsConsent == null)
                {
                    PromptAnalyticsConsent();
                }
                _shelfWindow?.ToggleShelf();
            });
        }

        public void PromptAnalyticsConsent()
        {
            if (_settingsService?.CurrentSettings.AnalyticsConsent != null) return;

            var dialog = new AnalyticsConsentDialog
            {
                Owner = _shelfWindow?.IsVisible == true ? _shelfWindow : null,
                WindowStartupLocation = WindowStartupLocation.CenterScreen
            };

            bool? result = dialog.ShowDialog();
            bool consent = result == true;

            _analyticsService?.SetConsent(consent);

            if (consent)
            {
                RecordStartupAnalytics(startMinimized: false);
            }
        }

        private void RecordStartupAnalytics(bool startMinimized)
        {
            if (_analyticsService == null || !_analyticsService.IsEnabled || _settingsService == null) return;

            var s = _settingsService.CurrentSettings;
            if (s == null) return;

            if (!s.FirstLaunchEventTracked)
            {
                _analyticsService.TrackFirstLaunch();
                s.FirstLaunchEventTracked = true;
                _settingsService.SaveSettings(s);
            }

            _analyticsService.TrackAppStarted(startMinimized ? "minimized" : "standard");

            string currentVersion = _analyticsService.AppVersion;
            if (!string.IsNullOrEmpty(s.LastVersionRun) && !string.Equals(s.LastVersionRun, currentVersion, StringComparison.OrdinalIgnoreCase))
            {
                _analyticsService.TrackAppUpdated(s.LastVersionRun, currentVersion);
            }
            s.LastVersionRun = currentVersion;
            _settingsService.SaveSettings(s);
        }

        public void ExitApplication()
        {
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
                _notifyIcon = null;
            }

            _shelfWindow?.Close();

            _downloadsViewModel?.Dispose();
            _taskbarService?.Dispose();

            if (_analyticsService != null && _analyticsService.IsEnabled)
            {
                try
                {
                    _analyticsService.TrackAppShutdown("user");
                    _analyticsService.FlushAsync(TimeSpan.FromMilliseconds(1000)).GetAwaiter().GetResult();
                }
                catch { }
                finally
                {
                    _analyticsService.Dispose();
                    _analyticsService = null;
                }
            }

            if (_instanceMutex != null)
            {
                try
                {
                    _instanceMutex.ReleaseMutex();
                    _instanceMutex.Dispose();
                }
                catch { }
                _instanceMutex = null;
            }

            Shutdown();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            ExitApplication();
            base.OnExit(e);
        }
    }
}
