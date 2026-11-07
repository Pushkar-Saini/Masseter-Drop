using System;
using System.Globalization;
using System.Windows.Data;

namespace MasseterDrop.Converters
{
    public class RelativeTimeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not DateTime dt)
                return string.Empty;

            var span = DateTime.Now - dt;

            if (span.TotalSeconds < 60)
                return "Just now";

            if (span.TotalMinutes < 60)
            {
                int mins = Math.Max(1, (int)span.TotalMinutes);
                return $"{mins}m ago";
            }

            if (span.TotalHours < 24)
            {
                int hours = (int)span.TotalHours;
                return $"{hours}h ago";
            }

            if (span.TotalDays < 2)
                return "Yesterday";

            if (dt.Year == DateTime.Now.Year)
                return dt.ToString("MMM d");

            return dt.ToString("MMM d, yyyy");
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
