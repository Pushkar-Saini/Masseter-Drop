using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using MasseterDrop.Helpers;
using MasseterDrop.Models;
using MasseterDrop.Services;
using MasseterDrop.ViewModels;
using Point = System.Windows.Point;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace MasseterDrop.Views
{
    public partial class DownloadShelfWindow : Window
    {
        private readonly SettingsService _settingsService;
        private readonly DownloadsViewModel _viewModel;
        private readonly IAnalyticsService? _analyticsService;
        private readonly DispatcherTimer _autoDismissTimer;
        private readonly DispatcherTimer _countdownTicker;

        private const double AutoDismissSeconds = 8.0;
        private double _remainingSeconds = AutoDismissSeconds;
        private bool _isHovered = false;
        private bool _isAutoDismissActive = false;
        private bool _isSettingsOpen = false;
        private bool _isDismissing = false;

        // Interactive Cursor-Following Ambient Light Gradient (ChatGPT Desktop Style)
        private readonly Point _restingSpotlightPos = new Point(80, 40);
        private Point _currentSpotlightPos = new Point(80, 40);
        private Point _targetSpotlightPos = new Point(80, 40);
        private bool _isSpotlightActive = false;

        public DownloadsViewModel ViewModel => _viewModel;

        public DownloadShelfWindow(SettingsService settingsService, DownloadsViewModel viewModel, IAnalyticsService? analyticsService = null)
        {
            InitializeComponent();
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            _analyticsService = analyticsService;

            DataContext = _viewModel;

            _viewModel.FileCompletedEvent += OnFileCompleted;
            _viewModel.RequestOpenSettings += OnRequestOpenSettings;

            // Timer for the 8-second auto-dismiss
            _autoDismissTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(AutoDismissSeconds)
            };
            _autoDismissTimer.Tick += AutoDismissTimer_Tick;

            // Smooth countdown progress ticker (~30fps)
            _countdownTicker = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(50)
            };
            _countdownTicker.Tick += CountdownTicker_Tick;

            Loaded += DownloadShelfWindow_Loaded;
            SourceInitialized += DownloadShelfWindow_SourceInitialized;
            MouseEnter += DownloadShelfWindow_MouseEnter;
            MouseLeave += DownloadShelfWindow_MouseLeave;
            PreviewMouseMove += DownloadShelfWindow_PreviewMouseMove;
            PreviewKeyDown += DownloadShelfWindow_PreviewKeyDown;
            Deactivated += DownloadShelfWindow_Deactivated;
            Closed += DownloadShelfWindow_Closed;
        }

        private void DownloadShelfWindow_SourceInitialized(object? sender, EventArgs e)
        {
            var helper = new WindowInteropHelper(this);
            IntPtr hwnd = helper.Handle;

            // Zero Focus Stealing: Configure as non-activating tool window
            NativeMethods.SetNonActivatingToolWindow(hwnd);
        }

        private void DownloadShelfWindow_Loaded(object sender, RoutedEventArgs e)
        {
            PositionNearTaskbar();
        }

        public void PositionNearTaskbar()
        {
            double w = ActualWidth > 0 ? ActualWidth : Width;
            double h = ActualHeight > 0 ? ActualHeight : Height;

            Point pos = TaskbarService.CalculatePanelPosition(w, h);
            Left = pos.X;
            Top = pos.Y;
        }

        private void OnFileCompleted(DownloadItem item)
        {
            Dispatcher.Invoke(() =>
            {
                ShowAutoPopup(item);
            });
        }

        public void ShowAutoPopup(DownloadItem item)
        {
            bool wasVisible = IsVisible && !_isDismissing;
            _isDismissing = false;
            PositionNearTaskbar();
            StopSpotlightTracking(snapToRest: true);

            // Scroll to the newest file
            ShelfScrollViewer.ScrollToLeftEnd();

            // Setup entrance animation
            ShelfSurface.Opacity = 0.0;
            ShelfTranslateTransform.Y = 22.0;

            if (!IsVisible)
            {
                var helper = new WindowInteropHelper(this);
                if (helper.Handle != IntPtr.Zero)
                {
                    NativeMethods.ShowWindow(helper.Handle, NativeMethods.SW_SHOWNOACTIVATE);
                }
                Show();
            }

            if (!wasVisible)
            {
                _analyticsService?.TrackShelfDisplayed("automatic");
            }

            // Animate entrance: Opacity 0 -> 1, TranslateY 22 -> 0, Scale 0.95 -> 1.0
            var fadeIn = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            var slideUp = new DoubleAnimation(22.0, 0.0, TimeSpan.FromMilliseconds(240))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            var scaleIn = new DoubleAnimation(0.95, 1.0, TimeSpan.FromMilliseconds(240))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            ShelfSurface.BeginAnimation(UIElement.OpacityProperty, fadeIn);
            ShelfTranslateTransform.BeginAnimation(TranslateTransform.YProperty, slideUp);
            ShelfScaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleIn);
            ShelfScaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleIn);

            // Start 8-second auto-dismiss countdown
            StartAutoDismiss();
        }

        public void ToggleShelf()
        {
            if (IsVisible && !_isDismissing)
            {
                DismissShelf("tray_toggle");
            }
            else
            {
                ShowManual(isReopen: true);
            }
        }

        public void ShowManual(bool isReopen = false)
        {
            bool wasVisible = IsVisible && !_isDismissing;
            _isDismissing = false;
            PositionNearTaskbar();
            StopSpotlightTracking(snapToRest: true);
            ShelfScrollViewer.ScrollToLeftEnd();

            ShelfSurface.Opacity = 0.0;
            ShelfTranslateTransform.Y = 18.0;

            Show();

            if (!wasVisible)
            {
                if (isReopen)
                {
                    _analyticsService?.TrackShelfReopened("tray");
                }
                _analyticsService?.TrackShelfDisplayed("manual");
            }

            var fadeIn = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            var slideUp = new DoubleAnimation(18.0, 0.0, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            var scaleIn = new DoubleAnimation(0.96, 1.0, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            ShelfSurface.BeginAnimation(UIElement.OpacityProperty, fadeIn);
            ShelfTranslateTransform.BeginAnimation(TranslateTransform.YProperty, slideUp);
            ShelfScaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleIn);
            ShelfScaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleIn);

            // In manual mode, we do not abruptly close in 8s unless idle
            StopAutoDismiss();
            DismissalProgressBar.Value = 100;
        }

        public void DismissShelf(string reason = "manual")
        {
            if (_isDismissing || !IsVisible) return;
            _isDismissing = true;

            _analyticsService?.TrackShelfDismissed(reason);

            StopAutoDismiss();
            StopSpotlightTracking(snapToRest: true);

            var fadeOut = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(160))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };
            var slideDown = new DoubleAnimation(0.0, 16.0, TimeSpan.FromMilliseconds(160))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };
            var scaleOut = new DoubleAnimation(1.0, 0.96, TimeSpan.FromMilliseconds(160))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };

            fadeOut.Completed += (s, e) =>
            {
                Hide();
                ShelfSurface.Opacity = 1.0;
                ShelfTranslateTransform.Y = 0.0;
                ShelfScaleTransform.ScaleX = 1.0;
                ShelfScaleTransform.ScaleY = 1.0;
                _isDismissing = false;
            };

            ShelfSurface.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            ShelfTranslateTransform.BeginAnimation(TranslateTransform.YProperty, slideDown);
            ShelfScaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleOut);
            ShelfScaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleOut);
        }

        private void StartAutoDismiss()
        {
            _remainingSeconds = AutoDismissSeconds;
            _isAutoDismissActive = true;
            DismissalProgressBar.Value = 100;

            _autoDismissTimer.Stop();
            _autoDismissTimer.Interval = TimeSpan.FromSeconds(AutoDismissSeconds);
            _autoDismissTimer.Start();

            _countdownTicker.Stop();
            _countdownTicker.Start();
        }

        private void StopAutoDismiss()
        {
            _isAutoDismissActive = false;
            _autoDismissTimer.Stop();
            _countdownTicker.Stop();
            DismissalProgressBar.Value = 100;
        }

        private void AutoDismissTimer_Tick(object? sender, EventArgs e)
        {
            if (!_isHovered && !_isSettingsOpen)
            {
                DismissShelf("auto_dismiss");
            }
        }

        private void CountdownTicker_Tick(object? sender, EventArgs e)
        {
            if (_isHovered) return;

            _remainingSeconds -= 0.05;
            if (_remainingSeconds <= 0)
            {
                _remainingSeconds = 0;
                DismissalProgressBar.Value = 0;
                _countdownTicker.Stop();
            }
            else
            {
                DismissalProgressBar.Value = (_remainingSeconds / AutoDismissSeconds) * 100.0;
            }
        }

        private void DownloadShelfWindow_MouseEnter(object sender, MouseEventArgs e)
        {
            _isHovered = true;
            _autoDismissTimer.Stop();
            _countdownTicker.Stop();
        }

        private void DownloadShelfWindow_MouseLeave(object sender, MouseEventArgs e)
        {
            _isHovered = false;

            // Smoothly glide ambient spotlight back to resting top-left position
            _targetSpotlightPos = _restingSpotlightPos;
            StartSpotlightTracking();

            if (_isAutoDismissActive && IsVisible && !_isDismissing)
            {
                // Resume with at least 4 seconds remaining so it never disappears abruptly
                if (_remainingSeconds < 4.0)
                {
                    _remainingSeconds = 4.0;
                }

                _autoDismissTimer.Stop();
                _autoDismissTimer.Interval = TimeSpan.FromSeconds(_remainingSeconds);
                _autoDismissTimer.Start();

                _countdownTicker.Stop();
                _countdownTicker.Start();
            }
        }

        private void DownloadShelfWindow_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            Point pos = e.GetPosition(ShelfSurface);

            // Clamp within shelf bounds
            double maxX = ShelfSurface.ActualWidth > 0 ? ShelfSurface.ActualWidth : Width;
            double maxY = ShelfSurface.ActualHeight > 0 ? ShelfSurface.ActualHeight : Height;
            double clampedX = Math.Max(0, Math.Min(maxX, pos.X));
            double clampedY = Math.Max(0, Math.Min(maxY, pos.Y));

            _targetSpotlightPos = new Point(clampedX, clampedY);
            StartSpotlightTracking();
        }

        private void DownloadShelfWindow_Closed(object? sender, EventArgs e)
        {
            StopSpotlightTracking(snapToRest: true);
        }

        private void StartSpotlightTracking()
        {
            if (!_isSpotlightActive)
            {
                _isSpotlightActive = true;
                CompositionTarget.Rendering += OnSpotlightRender;
            }
        }

        private void StopSpotlightTracking(bool snapToRest = false)
        {
            if (_isSpotlightActive)
            {
                CompositionTarget.Rendering -= OnSpotlightRender;
                _isSpotlightActive = false;
            }

            if (snapToRest && SpotlightTransform != null)
            {
                _currentSpotlightPos = _restingSpotlightPos;
                _targetSpotlightPos = _restingSpotlightPos;
                SpotlightTransform.X = _restingSpotlightPos.X;
                SpotlightTransform.Y = _restingSpotlightPos.Y;
            }
        }

        private void OnSpotlightRender(object? sender, EventArgs e)
        {
            if (SpotlightTransform == null) return;

            // Fluid spring lerp towards target position
            double dx = _targetSpotlightPos.X - _currentSpotlightPos.X;
            double dy = _targetSpotlightPos.Y - _currentSpotlightPos.Y;

            // Smooth lerp factor (0.20 delivers responsive yet organic fluid inertia)
            _currentSpotlightPos.X += dx * 0.20;
            _currentSpotlightPos.Y += dy * 0.20;

            SpotlightTransform.X = _currentSpotlightPos.X;
            SpotlightTransform.Y = _currentSpotlightPos.Y;

            // Idle check: if settled, stop render loop to consume 0% CPU
            if (Math.Abs(dx) < 0.25 && Math.Abs(dy) < 0.25)
            {
                if (!_isHovered)
                {
                    StopSpotlightTracking(snapToRest: true);
                }
                else
                {
                    // Stationary mouse inside panel: pause rendering until mouse moves again
                    if (_isSpotlightActive)
                    {
                        CompositionTarget.Rendering -= OnSpotlightRender;
                        _isSpotlightActive = false;
                    }
                }
            }
        }

        private void DownloadShelfWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                DismissShelf("escape_key");
                e.Handled = true;
            }
        }

        private void DownloadShelfWindow_Deactivated(object? sender, EventArgs e)
        {
            if (_isSettingsOpen) return;

            if (_settingsService.CurrentSettings.AutoDismissOnDeactivate && !_isHovered && IsVisible)
            {
                DismissShelf("deactivated");
            }
        }

        private void Card_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is DownloadItem item)
            {
                _viewModel.OpenFileCommand.Execute(item);
            }
        }

        private void ShelfScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            // Allow smooth horizontal scrolling with normal mouse wheel
            if (sender is ScrollViewer scroller)
            {
                if (e.Delta < 0)
                {
                    scroller.ScrollToHorizontalOffset(scroller.HorizontalOffset + 60);
                }
                else
                {
                    scroller.ScrollToHorizontalOffset(scroller.HorizontalOffset - 60);
                }
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
            DismissShelf("close_button");
        }

        private void DismissMenuItem_Click(object sender, RoutedEventArgs e)
        {
            DismissShelf("context_menu");
        }

        private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.OpenDownloadsFolderCommand.Execute(null);
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            OnRequestOpenSettings();
        }

        private void OnRequestOpenSettings()
        {
            _isSettingsOpen = true;
            StopAutoDismiss();

            var settingsWin = new SettingsWindow(_settingsService, () =>
            {
                _viewModel.ReloadSettings();
            }, _analyticsService)
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
