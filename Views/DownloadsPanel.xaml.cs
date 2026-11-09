using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using MasseterDrop.Helpers;
using MasseterDrop.Models;
using MasseterDrop.Services;
using MasseterDrop.ViewModels;
using Point = System.Windows.Point;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace MasseterDrop.Views
{
    public partial class DownloadsPanel : Window
    {
        private readonly SettingsService _settingsService;
        private readonly DownloadsViewModel _viewModel;
        private bool _isSettingsOpen = false;

        public DownloadsViewModel ViewModel => _viewModel;

        public DownloadsPanel(SettingsService settingsService, DownloadsViewModel viewModel)
        {
            InitializeComponent();
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));

            DataContext = _viewModel;
            _viewModel.RequestOpenSettings += OnRequestOpenSettings;

            Loaded += DownloadsPanel_Loaded;
            Deactivated += DownloadsPanel_Deactivated;
            KeyDown += DownloadsPanel_KeyDown;
        }

        private void DownloadsPanel_Loaded(object sender, RoutedEventArgs e)
        {
            ThemeService.Apply(this, _settingsService.CurrentSettings.Theme);
            PositionNearTaskbar();
        }

        public void PositionNearTaskbar()
        {
            // Responsive height based on available screen working area
            double maxAllowedHeight = Math.Max(350, SystemParameters.WorkArea.Height - 48);
            if (Height > maxAllowedHeight)
            {
                Height = maxAllowedHeight;
            }

            double w = ActualWidth > 0 ? ActualWidth : Width;
            double h = ActualHeight > 0 ? ActualHeight : Height;

            Point pos = TaskbarService.CalculatePanelPosition(w, h);
            Left = pos.X;
            Top = pos.Y;
        }

        public void TogglePanel()
        {
            if (Visibility == Visibility.Visible && IsActive)
            {
                HidePanel();
            }
            else
            {
                ShowPanel();
            }
        }

        public void ShowPanel()
        {
            PositionNearTaskbar();
            Show();
            WindowState = WindowState.Normal;
            Activate();
            Topmost = true;
            Focus();

            // Animate subtle fade-in
            var fadeIn = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(160));
            MainBorder.BeginAnimation(UIElement.OpacityProperty, fadeIn);
        }

        public void HidePanel()
        {
            var fadeOut = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(120));
            fadeOut.Completed += (s, e) =>
            {
                Hide();
                MainBorder.Opacity = 1.0;
            };
            MainBorder.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        }

        private void DownloadsPanel_Deactivated(object? sender, EventArgs e)
        {
            if (_isSettingsOpen) return;

            if (_settingsService.CurrentSettings.AutoDismissOnDeactivate)
            {
                HidePanel();
            }
        }

        private void DownloadsPanel_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                HidePanel();
                e.Handled = true;
            }
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            HidePanel();
        }

        private void DownloadList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (_viewModel.SelectedDownload != null)
            {
                _viewModel.OpenFileCommand.Execute(_viewModel.SelectedDownload);
            }
        }

        private void OnRequestOpenSettings()
        {
            _isSettingsOpen = true;
            var settingsWin = new SettingsWindow(_settingsService, () =>
            {
                ThemeService.Apply(this, _settingsService.CurrentSettings.Theme);
                _viewModel.ReloadSettings();
            })
            {
                Owner = this
            };

            settingsWin.Closed += (s, e) =>
            {
                _isSettingsOpen = false;
                PositionNearTaskbar();
            };

            settingsWin.ShowDialog();
        }
    }
}
