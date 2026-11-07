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
        private DownloadHistoryService? _historyService;
        private FileOperationService? _fileOperationService;
        private TaskbarService? _taskbarService;
        private DownloadsViewModel? _downloadsViewModel;
        private DownloadsPanel? _downloadsPanel;
        private Forms.NotifyIcon? _notifyIcon;

        [STAThread]
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 1. Single Instance Check
            bool isNewInstance;
            _instanceMutex = new Mutex(true, MutexName, out isNewInstance);

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
                System.Diagnostics.Debug.WriteLine($"Unhandled exception: {args.Exception.Message}");
                args.Handled = true;
            };

            // 4. Initialize Core Services
            _settingsService = new SettingsService();
            _settingsService.LoadSettings();

            _historyService = new DownloadHistoryService();
            _fileOperationService = new FileOperationService();

            _taskbarService = new TaskbarService();
            _taskbarService.ActivationRequested += OnActivationRequested;
            _taskbarService.StartSingleInstanceServer();

            // 5. Initialize ViewModels and Main Panel
            _downloadsViewModel = new DownloadsViewModel(_settingsService, _historyService, _fileOperationService);
            _downloadsPanel = new DownloadsPanel(_settingsService, _downloadsViewModel);

            // 6. Setup System Tray Icon
            SetupNotifyIcon();

            // 7. Check whether to start minimized
            bool startMinimized = e.Args.Contains("--minimized", StringComparer.OrdinalIgnoreCase) ||
                                  _settingsService.CurrentSettings.StartMinimized;

            if (!startMinimized)
            {
                _downloadsPanel.ShowPanel();
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

                // Load icon from disk or application resource
                string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Icons", "app.ico");
                if (File.Exists(iconPath))
                {
                    _notifyIcon.Icon = new Icon(iconPath);
                }
                else
                {
                    _notifyIcon.Icon = SystemIcons.Application;
                }

                // Context Menu
                var contextMenu = new Forms.ContextMenuStrip();

                var itemPanel = new Forms.ToolStripMenuItem("Downloads Panel")
                {
                    Font = new Font(contextMenu.Font, System.Drawing.FontStyle.Bold)
                };
                itemPanel.Click += (s, e) => _downloadsPanel?.TogglePanel();

                var itemFolder = new Forms.ToolStripMenuItem("Open Downloads Folder");
                itemFolder.Click += (s, e) =>
                {
                    string folder = _settingsService?.GetEffectiveDownloadsPath() ?? string.Empty;
                    _fileOperationService?.OpenFolder(folder, out _);
                };

                var itemSettings = new Forms.ToolStripMenuItem("Settings...");
                itemSettings.Click += (s, e) =>
                {
                    _downloadsPanel?.ShowPanel();
                    _downloadsViewModel?.OpenSettingsCommand.Execute(null);
                };

                var itemExit = new Forms.ToolStripMenuItem("Exit");
                itemExit.Click += (s, e) => ExitApplication();

                contextMenu.Items.Add(itemPanel);
                contextMenu.Items.Add(itemFolder);
                contextMenu.Items.Add(itemSettings);
                contextMenu.Items.Add(new Forms.ToolStripSeparator());
                contextMenu.Items.Add(itemExit);

                _notifyIcon.ContextMenuStrip = contextMenu;

                // Left click toggles panel
                _notifyIcon.MouseClick += (s, e) =>
                {
                    if (e.Button == Forms.MouseButtons.Left)
                    {
                        _downloadsPanel?.TogglePanel();
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
                _downloadsPanel?.TogglePanel();
            });
        }

        public void ExitApplication()
        {
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
                _notifyIcon = null;
            }

            _downloadsViewModel?.Dispose();
            _taskbarService?.Dispose();

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
