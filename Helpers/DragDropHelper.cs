using System;
using System.Collections.Specialized;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MasseterDrop.Models;
using Point = System.Windows.Point;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace MasseterDrop.Helpers
{
    public static class DragDropHelper
    {
        public static readonly DependencyProperty IsDraggableProperty =
            DependencyProperty.RegisterAttached(
                "IsDraggable",
                typeof(bool),
                typeof(DragDropHelper),
                new PropertyMetadata(false, OnIsDraggableChanged));

        public static bool GetIsDraggable(DependencyObject obj) => (bool)obj.GetValue(IsDraggableProperty);
        public static void SetIsDraggable(DependencyObject obj, bool value) => obj.SetValue(IsDraggableProperty, value);

        /// <summary>
        /// Raised when a user initiates dragging a file card from the shelf.
        /// Passes only the anonymous file category.
        /// </summary>
        public static event Action<FileCategory>? FileDragged;

        private static Point _startPoint;
        private static bool _isDragging = false;

        private static void OnIsDraggableChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is FrameworkElement element)
            {
                if ((bool)e.NewValue)
                {
                    element.PreviewMouseLeftButtonDown += Element_PreviewMouseLeftButtonDown;
                    element.PreviewMouseMove += Element_PreviewMouseMove;
                    element.PreviewMouseLeftButtonUp += Element_PreviewMouseLeftButtonUp;
                }
                else
                {
                    element.PreviewMouseLeftButtonDown -= Element_PreviewMouseLeftButtonDown;
                    element.PreviewMouseMove -= Element_PreviewMouseMove;
                    element.PreviewMouseLeftButtonUp -= Element_PreviewMouseLeftButtonUp;
                }
            }
        }

        private static void Element_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element)
            {
                _startPoint = e.GetPosition(null);
                _isDragging = false;
            }
        }

        private static void Element_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _isDragging = false;
        }

        private static void Element_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || _isDragging)
                return;

            Point currentPoint = e.GetPosition(null);
            Vector diff = _startPoint - currentPoint;

            if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                if (sender is FrameworkElement element && element.DataContext is DownloadItem item)
                {
                    if (!item.IsAvailable || item.Status != DownloadStatus.Completed || !File.Exists(item.FullPath))
                    {
                        return;
                    }

                    _isDragging = true;
                    try
                    {
                        FileDragged?.Invoke(item.Category);

                        var data = new DataObject();

                        // Primary Windows OS File Drop format
                        var fileDropList = new StringCollection { item.FullPath };
                        data.SetFileDropList(fileDropList);

                        // Plain text fallback
                        data.SetText(item.FullPath);

                        // Shell IDList format for full compatibility
                        DragDrop.DoDragDrop(element, data, DragDropEffects.Copy | DragDropEffects.Link);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Drag drop exception: {ex.Message}");
                    }
                    finally
                    {
                        _isDragging = false;
                    }
                }
            }
        }
    }
}
