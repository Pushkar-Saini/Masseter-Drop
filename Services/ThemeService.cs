using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Win32;
using MasseterDrop.Helpers;

namespace MasseterDrop.Services
{
    public static class ThemeService
    {
        private static readonly IReadOnlyDictionary<string, string> LightPalette = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["#17191E"] = "#F6F7F9", ["#1B1E24"] = "#F1F3F6", ["#22252C"] = "#FFFFFF",
            ["#2B2F38"] = "#EAECF0", ["#323642"] = "#D5D9E0", ["#4A5060"] = "#ADB5C2",
            ["#5B6271"] = "#697386", ["#717886"] = "#5F6875", ["#A1A6B0"] = "#626B78",
            ["#F5F6F8"] = "#20232A", ["#1C2E4A"] = "#E2EDFC", ["#3B2326"] = "#FBE8E8"
        };

        public static bool IsDark(string? theme)
        {
            if (string.Equals(theme, "Light", StringComparison.OrdinalIgnoreCase)) return false;
            if (!string.Equals(theme, "System", StringComparison.OrdinalIgnoreCase)) return true;
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize");
                return Convert.ToInt32(key?.GetValue("AppsUseLightTheme", 0) ?? 0) == 0;
            }
            catch { return true; }
        }

        public static void Apply(Window window, string? theme)
        {
            bool dark = IsDark(theme);
            var pending = new Stack<DependencyObject>();
            var visited = new HashSet<DependencyObject>();
            pending.Push(window);
            while (pending.Count > 0)
            {
                var element = pending.Pop();
                if (!visited.Add(element)) continue;
                ApplyToElement(element, dark);
                int count = VisualTreeHelper.GetChildrenCount(element);
                for (int i = 0; i < count; i++) pending.Push(VisualTreeHelper.GetChild(element, i));
                foreach (object child in LogicalTreeHelper.GetChildren(element))
                    if (child is DependencyObject dependencyObject) pending.Push(dependencyObject);
            }

            NativeMethods.ApplyModernWindowStyle(window, isDarkMode: dark);
        }

        private static void ApplyToElement(DependencyObject element, bool dark)
        {
            if (element is Control control)
            {
                control.Background = ConvertBrush(control.Background, dark);
                control.Foreground = ConvertBrush(control.Foreground, dark);
                control.BorderBrush = ConvertBrush(control.BorderBrush, dark);
            }
            if (element is Panel panel) panel.Background = ConvertBrush(panel.Background, dark);
            if (element is Border border)
            {
                border.Background = ConvertBrush(border.Background, dark);
                border.BorderBrush = ConvertBrush(border.BorderBrush, dark);
            }
            if (element is TextBlock text) text.Foreground = ConvertBrush(text.Foreground, dark);
            if (element is Shape shape)
            {
                shape.Fill = ConvertBrush(shape.Fill, dark);
                shape.Stroke = ConvertBrush(shape.Stroke, dark);
            }
        }

        private static Brush? ConvertBrush(Brush? brush, bool dark)
        {
            if (brush is not SolidColorBrush solid) return brush;
            Color color = solid.Color;
            string hex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
            if (dark)
            {
                foreach (var pair in LightPalette)
                    if (string.Equals(pair.Value, hex, StringComparison.OrdinalIgnoreCase))
                        return new SolidColorBrush((Color)ColorConverter.ConvertFromString(pair.Key));
                return brush;
            }
            return LightPalette.TryGetValue(hex, out string? lightHex)
                ? new SolidColorBrush((Color)ColorConverter.ConvertFromString(lightHex))
                : brush;
        }
    }
}
